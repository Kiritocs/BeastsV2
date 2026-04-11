using System;
using System.Threading;
using System.Threading.Tasks;
using ExileCore.Shared.Nodes;
using BeastsV2.Runtime.State;

namespace BeastsV2.Runtime.Automation;

internal sealed record BestiaryAutomationWorkflowCallbacks(
    Action RequestAutomationStop,
    Func<Task> LaunchClearAutomationAsync,
    Func<bool, Task> EnsureCapturedBeastsWindowOpenAsync,
    Func<Task> EnsureTravelToMenagerieAsync,
    Action<string> EnsureCapturedBeastsTabVisible,
    Func<int> GetBestiaryTotalCapturedBeastCount,
    Action<int> EnsureFullSequenceCanStartItemizing,
    Func<bool, Task<bool>> EnsureItemizingCapacityAsync,
    Func<Task<int>> ClearCapturedBeastsAsync,
    Func<Task<int>> StashCapturedMonstersAndCloseUiAsync,
    Action<string, bool> UpdateAutomationStatus,
    Action<string> LogDebug,
    Func<string, Task> ApplyBestiarySearchRegexAsync,
    Func<int> GetPlayerInventoryFreeCellCount);

internal sealed class BestiaryAutomationWorkflow
{
    private readonly AutomationRuntimeState _state;
    private readonly Func<BestiaryAutomationSettings> _settings;
    private readonly BestiaryAutomationWorkflowCallbacks _callbacks;

    public BestiaryAutomationWorkflow(
        AutomationRuntimeState state,
        Func<BestiaryAutomationSettings> settings,
        BestiaryAutomationWorkflowCallbacks callbacks)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public bool ShouldDeleteBeasts()
    {
        return _state.BestiaryDeleteModeOverride == true;
    }

    public bool ShouldAutoStashItemizedBeasts()
    {
        return _state.BestiaryAutoStashOverride ?? true;
    }

    public Task TriggerClearAsync(bool deleteBeasts, string triggerSource, bool isAutomationRunning)
    {
        if (isAutomationRunning)
        {
            _callbacks.RequestAutomationStop();
            return Task.CompletedTask;
        }

        _state.BestiaryDeleteModeOverride = deleteBeasts;
        _callbacks.LogDebug($"Bestiary clear triggered by {triggerSource}. mode={(deleteBeasts ? "delete" : "itemize")}");
        return _callbacks.LaunchClearAutomationAsync();
    }

    public async Task RunClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deleteBeasts = ShouldDeleteBeasts();
        if (deleteBeasts)
        {
            await _callbacks.EnsureCapturedBeastsWindowOpenAsync(true);
        }
        else
        {
            await _callbacks.EnsureTravelToMenagerieAsync();
            await _callbacks.EnsureCapturedBeastsWindowOpenAsync(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _callbacks.EnsureCapturedBeastsTabVisible("starting Bestiary clear automation");

        if (!deleteBeasts)
        {
            if (!await _callbacks.EnsureItemizingCapacityAsync(false))
            {
                return;
            }
        }
        else
        {
            _callbacks.LogDebug("Bestiary clear starting in delete mode.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var releasedBeastCount = await _callbacks.ClearCapturedBeastsAsync();
        if (!deleteBeasts)
        {
            await _callbacks.StashCapturedMonstersAndCloseUiAsync();
        }

        var processedAnyBeasts = releasedBeastCount > 0;
        _callbacks.UpdateAutomationStatus(
            processedAnyBeasts
                ? $"Bestiary clear complete. {(deleteBeasts ? "Deleted" : "Itemized")} {releasedBeastCount} {BeastLabel(releasedBeastCount)}."
                : "Bestiary clear complete. No captured beasts were visible.",
            true);
    }

    public async Task<int> RunRegexItemizeBodyAsync(string regex, bool isFullSequence, CancellationToken cancellationToken)
    {
        _state.BestiaryDeleteModeOverride = false;
        _state.BestiaryAutoStashOverride = isFullSequence ? false : _settings()?.RegexItemizeAutoStash?.Value;

        cancellationToken.ThrowIfCancellationRequested();
        await _callbacks.EnsureTravelToMenagerieAsync();

        cancellationToken.ThrowIfCancellationRequested();
        await _callbacks.EnsureCapturedBeastsWindowOpenAsync(false);

        cancellationToken.ThrowIfCancellationRequested();
        _callbacks.UpdateAutomationStatus("Applying Bestiary Regex...", false);
        _state.ActiveBestiarySearchRegex = regex ?? string.Empty;
        await _callbacks.ApplyBestiarySearchRegexAsync(regex);

        if (isFullSequence)
        {
            _callbacks.EnsureFullSequenceCanStartItemizing(_callbacks.GetBestiaryTotalCapturedBeastCount());
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await _callbacks.EnsureItemizingCapacityAsync(isFullSequence))
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _callbacks.UpdateAutomationStatus("Itemizing Bestiary regex matches...", false);
        var itemizedBeastCount = await _callbacks.ClearCapturedBeastsAsync();
        var inventoryIsFullAfterItemize = _callbacks.GetPlayerInventoryFreeCellCount() <= 0;

        cancellationToken.ThrowIfCancellationRequested();
        if (ShouldAutoStashItemizedBeasts())
        {
            await _callbacks.StashCapturedMonstersAndCloseUiAsync();
        }

        var inventoryFullDuringSequence = isFullSequence && inventoryIsFullAfterItemize;
        _callbacks.UpdateAutomationStatus(
            _state.BestiaryInventoryFullStop
                ? $"Bestiary regex itemize stopped. Itemized {itemizedBeastCount} {BeastLabel(itemizedBeastCount)}. Inventory is full."
                : inventoryFullDuringSequence
                    ? $"Bestiary regex itemize complete. Itemized {itemizedBeastCount} {BeastLabel(itemizedBeastCount)}. Inventory is full, continuing sequence."
                    : itemizedBeastCount > 0
                        ? $"Bestiary regex itemize complete. Itemized {itemizedBeastCount} {BeastLabel(itemizedBeastCount)}."
                        : "Bestiary regex itemize complete. No captured beasts matched the configured Bestiary Regex.",
            true);

        return itemizedBeastCount;
    }

    private static string BeastLabel(int count) => $"beast{BeastsV2Helpers.PluralSuffix(count)}";
}