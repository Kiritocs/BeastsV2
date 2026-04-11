using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;

namespace BeastsV2.Runtime.Automation;

internal sealed record BestiaryUiTiming(
    int KeyTapDelayMs,
    int FastPollDelayMs,
    int UiClickPreDelayMs,
    int MinTabClickPostDelayMs,
    int StashOpenPollDelayMs,
    int StashInteractionDistance,
    int StashTabSwitchDelayMs);

internal sealed record BestiaryUiOpenCallbacks(
    Func<Task> CloseBlockingBestiaryWorldUiAsync,
    Action ThrowIfAutomationStopRequested,
    Func<bool> IsBestiaryChallengePanelOpen,
    Func<bool> IsBestiaryCapturedBeastsTabVisible,
    Func<bool> IsBestiaryCapturedBeastsWindowOpen,
    Func<Element> TryGetBestiaryChallengesBestiaryButton,
    Func<Element> TryGetBestiaryCapturedBeastsButton,
    Action<string> LogBestiaryUiState,
    Action<string, bool> UpdateAutomationStatus,
    Action<string> LogDebug,
    Func<BestiaryUiTiming> GetTiming,
    Func<Keys> GetChallengesWindowHotkey,
    Func<Keys, int, int, Task> TapKeyAsync,
    Func<Func<bool>, int, int, Task<bool>> WaitForBestiaryConditionAsync,
    Func<int, Func<int, Task<bool?>>, int, Task<bool>> RetryBestiaryOpenAsync,
    Func<Element, int, int, Func<Task<bool>>, int, Task<bool>> ClickElementAndConfirmAsync,
    Func<Task<bool>> WaitForBestiaryCapturedBeastsButtonAsync,
    Func<Task<bool>> WaitForBestiaryCapturedBeastsDisplayAsync,
    Func<string> DescribeChallengesEntriesRootPath,
    Func<Element, string> DescribeElement,
    Func<Task<Entity>> WaitForMenagerieEinharAsync,
    Func<Entity, float?> GetPlayerDistanceToEntity,
    Func<Entity, string> DescribeEntity,
    Func<Entity, Task> CtrlClickWorldEntityAsync,
    Func<Func<bool>, Func<Task<bool>>, int, Task<bool>> EnsurePollingAutomationOpenAsync);

internal sealed class BestiaryUiOpenService
{
    private readonly BestiaryUiOpenCallbacks _callbacks;

    private sealed record CapturedBeastsOpenAttemptResult(bool? Opened, Element CapturedBeastsButton);

    public BestiaryUiOpenService(BestiaryUiOpenCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public Task CloseWorldUiAsync()
    {
        return _callbacks.CloseBlockingBestiaryWorldUiAsync();
    }

    public async Task EnsureCapturedBeastsWindowOpenAsync(bool openViaChallengesHotkey)
    {
        if (_callbacks.IsBestiaryCapturedBeastsTabVisible())
        {
            _callbacks.LogDebug("Bestiary Captured Beasts window is already open");
            _callbacks.LogBestiaryUiState("EnsureBestiaryCapturedBeastsWindowOpenAsync early return: strict captured page visible");
            return;
        }

        if (_callbacks.IsBestiaryCapturedBeastsWindowOpen())
        {
            _callbacks.LogBestiaryUiState("EnsureBestiaryCapturedBeastsWindowOpenAsync early return: strict captured display already open");
            return;
        }

        var capturedBeastsButton = _callbacks.TryGetBestiaryCapturedBeastsButton();

        if (capturedBeastsButton == null)
        {
            var challengePanelOpened = openViaChallengesHotkey
                ? await EnsureBestiaryChallengePanelOpenFromConfiguredHotkeyAsync()
                : await EnsureMenagerieEinharInteractionAsync();
            if (!challengePanelOpened)
            {
                throw new InvalidOperationException(openViaChallengesHotkey
                    ? "Timed out opening the Challenges window with the configured hotkey."
                    : "Timed out opening the challenge panel.");
            }

            _callbacks.LogBestiaryUiState("EnsureBestiaryCapturedBeastsWindowOpenAsync after Einhar interaction");
            capturedBeastsButton = _callbacks.TryGetBestiaryCapturedBeastsButton();
        }
        else
        {
            _callbacks.LogBestiaryUiState("EnsureBestiaryCapturedBeastsWindowOpenAsync using existing Bestiary panel state");
        }

        if (_callbacks.IsBestiaryCapturedBeastsTabVisible())
        {
            _callbacks.LogBestiaryUiState("EnsureBestiaryCapturedBeastsWindowOpenAsync early return after panel ready: strict captured page visible");
            return;
        }

        if (_callbacks.IsBestiaryCapturedBeastsWindowOpen())
        {
            _callbacks.LogBestiaryUiState("EnsureBestiaryCapturedBeastsWindowOpenAsync early return after panel ready: strict captured display already open");
            return;
        }

        if (capturedBeastsButton == null)
        {
            _callbacks.LogBestiaryUiState("EnsureBestiaryCapturedBeastsWindowOpenAsync could not resolve captured beasts button");
            throw new InvalidOperationException("Could not find the captured beasts button.");
        }

        var displayOpened = await _callbacks.RetryBestiaryOpenAsync(
            2,
            async attempt =>
            {
                var attemptResult = await TryOpenCapturedBeastsDisplayAsync(capturedBeastsButton, attempt);
                capturedBeastsButton = attemptResult.CapturedBeastsButton;
                return attemptResult.Opened;
            },
            0);
        if (displayOpened)
        {
            return;
        }

        throw new InvalidOperationException("Timed out opening the captured beasts window.");
    }

    private async Task<bool> EnsureBestiaryChallengePanelOpenFromConfiguredHotkeyAsync()
    {
        _callbacks.ThrowIfAutomationStopRequested();

        if (_callbacks.IsBestiaryChallengePanelOpen())
        {
            _callbacks.LogBestiaryUiState("Challenges hotkey open skipped because challenge panel is already open");
            return true;
        }

        var hotkey = _callbacks.GetChallengesWindowHotkey();
        if (hotkey == Keys.None)
        {
            throw new InvalidOperationException("Set the Bestiary Automation Challenges Window Hotkey to match the Path of Exile Challenges keybind before using the delete hotkey or Delete All outside The Menagerie.");
        }

        var timing = _callbacks.GetTiming();
        var panelOpened = await _callbacks.RetryBestiaryOpenAsync(
            2,
            async attempt =>
            {
                if (_callbacks.IsBestiaryChallengePanelOpen())
                {
                    return true;
                }

                _callbacks.UpdateAutomationStatus("Opening Challenges...", false);
                _callbacks.LogDebug($"Opening Bestiary challenge panel with configured hotkey. attempt={attempt}, key={hotkey}");
                await _callbacks.TapKeyAsync(hotkey, timing.KeyTapDelayMs, timing.FastPollDelayMs);

                var bestiaryButtonVisible = await _callbacks.WaitForBestiaryConditionAsync(
                    () => _callbacks.TryGetBestiaryChallengesBestiaryButton()?.IsVisible == true,
                    2000,
                    Math.Max(timing.FastPollDelayMs, 25));
                if (!bestiaryButtonVisible)
                {
                    _callbacks.LogDebug($"Challenges hotkey attempt {attempt} did not reveal a visible Bestiary entry under path {_callbacks.DescribeChallengesEntriesRootPath()}.");
                    return null;
                }

                var bestiaryButton = _callbacks.TryGetBestiaryChallengesBestiaryButton();
                _callbacks.LogDebug($"Clicking Bestiary entry from Challenges panel. attempt={attempt}, button={_callbacks.DescribeElement(bestiaryButton)}");
                var opened = await _callbacks.ClickElementAndConfirmAsync(
                    bestiaryButton,
                    timing.UiClickPreDelayMs,
                    Math.Max(timing.MinTabClickPostDelayMs, timing.FastPollDelayMs),
                    _callbacks.WaitForBestiaryCapturedBeastsButtonAsync,
                    0);
                _callbacks.LogDebug($"Challenges hotkey attempt {attempt} result. panelOpened={opened}");
                _callbacks.LogBestiaryUiState($"Challenges hotkey attempt {attempt} after waiting for challenge panel");
                return opened ? true : null;
            },
            timing.StashOpenPollDelayMs);

        return panelOpened || _callbacks.IsBestiaryChallengePanelOpen();
    }

    private async Task<CapturedBeastsOpenAttemptResult> TryOpenCapturedBeastsDisplayAsync(Element capturedBeastsButton, int attempt)
    {
        var timing = _callbacks.GetTiming();
        _callbacks.LogDebug($"Clicking Captured Beasts button. attempt={attempt}, button={_callbacks.DescribeElement(capturedBeastsButton)}");
        var opened = await _callbacks.ClickElementAndConfirmAsync(
            capturedBeastsButton,
            timing.UiClickPreDelayMs,
            Math.Max(timing.MinTabClickPostDelayMs, timing.StashTabSwitchDelayMs),
            _callbacks.WaitForBestiaryCapturedBeastsDisplayAsync,
            200);
        _callbacks.LogDebug($"Captured Beasts button click result. attempt={attempt}, displayOpened={opened}");
        _callbacks.LogBestiaryUiState($"EnsureBestiaryCapturedBeastsWindowOpenAsync after button click attempt {attempt}");
        if (opened)
        {
            return new(true, capturedBeastsButton);
        }

        var refreshedButton = _callbacks.TryGetBestiaryCapturedBeastsButton();
        if (refreshedButton == null)
        {
            _callbacks.LogBestiaryUiState($"EnsureBestiaryCapturedBeastsWindowOpenAsync lost captured beasts button after attempt {attempt}");
            return new(false, null);
        }

        return new(null, refreshedButton);
    }

    private bool IsBestiaryChallengePanelReadyForAutomation()
    {
        return _callbacks.IsBestiaryChallengePanelOpen() || _callbacks.TryGetBestiaryCapturedBeastsButton()?.IsVisible == true;
    }

    private async Task<bool> TryAdvanceMenagerieEinharInteractionAsync(int attempt)
    {
        var timing = _callbacks.GetTiming();
        _callbacks.LogBestiaryUiState($"Einhar interaction attempt {attempt} before locating Einhar");

        var einhar = await _callbacks.WaitForMenagerieEinharAsync();
        if (einhar == null)
        {
            _callbacks.LogBestiaryUiState($"Einhar interaction attempt {attempt} failed to locate Einhar");
            _callbacks.UpdateAutomationStatus("Could not find Einhar in The Menagerie.", false);
            return false;
        }

        var distance = _callbacks.GetPlayerDistanceToEntity(einhar);
        var statusMessage = distance.HasValue && distance.Value <= timing.StashInteractionDistance
            ? "Opening Einhar..."
            : "Moving to Einhar...";

        _callbacks.LogDebug($"Einhar interaction attempt {attempt}. einhar={_callbacks.DescribeEntity(einhar)}, distance={(distance.HasValue ? distance.Value.ToString("0.#") : "null")}, status='{statusMessage}'");
        _callbacks.UpdateAutomationStatus(statusMessage, false);
        await _callbacks.CtrlClickWorldEntityAsync(einhar);
        _callbacks.LogBestiaryUiState($"Einhar interaction attempt {attempt} after ctrl-click");

        var panelReady = IsBestiaryChallengePanelReadyForAutomation() || await _callbacks.WaitForBestiaryCapturedBeastsButtonAsync();
        _callbacks.LogDebug($"Einhar interaction attempt {attempt} result. panelReady={panelReady}");
        _callbacks.LogBestiaryUiState($"Einhar interaction attempt {attempt} after waiting for challenge panel");
        if (!panelReady)
        {
            _callbacks.LogDebug($"Einhar interaction attempt {attempt} did not detect the challenge panel. Retrying after delay {timing.StashOpenPollDelayMs}ms.");
        }

        return true;
    }

    private async Task<bool> EnsureMenagerieEinharInteractionAsync()
    {
        if (IsBestiaryChallengePanelReadyForAutomation())
        {
            _callbacks.LogBestiaryUiState("Einhar interaction skipped because challenge panel is already open");
            return true;
        }

        var timing = _callbacks.GetTiming();
        var attempt = 0;
        return await _callbacks.EnsurePollingAutomationOpenAsync(
            IsBestiaryChallengePanelReadyForAutomation,
            () => TryAdvanceMenagerieEinharInteractionAsync(++attempt),
            timing.StashOpenPollDelayMs);
    }
}