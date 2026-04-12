using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ImGuiNET;
using SharpDX;

namespace BeastsV2;

public partial class Main
{
    private sealed record BestiaryDisplayedBeastSnapshot(int Count, string FirstLabel, RectangleF FirstRect);

    #region Bestiary UI state

    private Element BestiaryChallengesPanel => GameController?.IngameState?.IngameUi?.ChallengesPanel;

    private IEnumerable<Element> GetVisibleBestiaryFamilyGroups(Element beastsDisplay) =>
        beastsDisplay?.Children?.Where(x => x?.IsVisible == true) ?? Enumerable.Empty<Element>();

    private bool IsInMenagerie()
    {
        return GameController?.Area?.CurrentArea?.Name.EqualsIgnoreCase(MenagerieAreaName) == true;
    }

    private bool IsBestiaryChallengePanelOpen()
    {
        if (BestiaryChallengesPanel?.IsVisible != true)
        {
            return false;
        }

        return TryGetBestiaryCapturedBeastsButton()?.IsVisible == true;
    }

    private bool IsBestiaryPanelVisible()
    {
        if (BestiaryChallengesPanel?.IsVisible != true)
        {
            return false;
        }

        return TryGetBestiaryPanel()?.IsVisible == true;
    }

    private bool IsBestiaryCapturedBeastsTabVisible()
    {
        if (BestiaryChallengesPanel?.IsVisible != true)
        {
            return false;
        }

        return TryGetBestiaryCapturedBeastsTab()?.IsVisible == true;
    }

    private bool IsBestiaryCapturedBeastsWindowOpen()
    {
        return TryGetBestiaryCapturedBeastsDisplay(out _, out _);
    }

    private void EnsureBestiaryCapturedBeastsTabVisible(string actionContext)
    {
        if (IsBestiaryCapturedBeastsTabVisible())
        {
            return;
        }

        throw new InvalidOperationException($"Captured Beasts tab is not visible while {actionContext}.");
    }

    private bool IsBestiaryWorldUiOpen()
    {
        return IsAutomationBlockingUiOpen() ||
               IsBestiaryCapturedBeastsWindowOpen() ||
               IsBestiaryChallengePanelOpen();
    }

    private bool CanUseInventoryBeastQuickAction()
    {
        return IsInMenagerie() || IsBestiaryPanelVisible();
    }

    private bool ShouldDeleteBestiaryBeasts()
    {
        return BestiaryWorkflow.ShouldDeleteBeasts();
    }

    #endregion
    #region Bestiary wait helpers

    private async Task<bool> WaitForBestiaryConditionAsync(Func<bool> condition, int timeoutMs, int pollDelayMs = -1)
    {
        var timing = AutomationTiming;
        var adjustedPollDelayMs = pollDelayMs >= 0 ? pollDelayMs : timing.FastPollDelayMs;
        return await WaitForAutomationConditionAsync(
            condition,
            timeoutMs,
            adjustedPollDelayMs,
            timing.UiCheckInitialSettleDelayMs);
    }

    private async Task<Entity> WaitForBestiaryEntityAsync(Func<Entity> resolver, int timeoutMs)
    {
        return await PollAutomationValueAsync(
            resolver,
            entity => entity != null,
            timeoutMs,
            AutomationTiming.FastPollDelayMs,
            AutomationTiming.UiCheckInitialSettleDelayMs);
    }

    private async Task<bool> WaitForAreaNameAsync(string areaName, int timeoutMs)
    {
        return await WaitForBestiaryConditionAsync(
            () => GameController?.Area?.CurrentArea?.Name.EqualsIgnoreCase(areaName) == true,
            timeoutMs);
    }

    private async Task<Entity> WaitForMenagerieEinharAsync()
    {
        return await WaitForAutomationEntityAsync(
            () => FindNearestAutomationEntity(
                entity => entity.Metadata.EqualsIgnoreCase(MenagerieEinharMetadata),
                requireVisible: true),
            () => FindNearestAutomationEntity(
                entity => entity.Metadata.EqualsIgnoreCase(MenagerieEinharMetadata),
                requireVisible: false),
            "Einhar",
            "Moving to Einhar...",
            15000);
    }

    private async Task<bool> WaitForBestiaryCapturedBeastsButtonAsync()
    {
        return await WaitForBestiaryConditionAsync(
            () => TryGetBestiaryCapturedBeastsButton()?.IsVisible == true,
            4000);
    }

    private async Task<bool> RetryBestiaryOpenAsync(int maxAttempts, Func<int, Task<bool?>> attemptAsync, int retryDelayMs)
    {
        var result = await RetryAutomationAsync(
            attemptAsync,
            response => response.HasValue,
            maxAttempts,
            retryDelayMs,
            firstAttemptNumber: 1);
        return result ?? false;
    }

    private async Task<bool> WaitForBestiaryCapturedBeastsDisplayAsync()
    {
        return await WaitForBestiaryConditionAsync(
            () => IsBestiaryCapturedBeastsTabVisible() && TryGetBestiaryCapturedBeastsDisplay(out _, out _),
            4000,
            Math.Max(AutomationTiming.FastPollDelayMs, 25));
    }

    private async Task<bool> WaitForBestiaryCapturedBeastsToPopulateAsync()
    {
        return await WaitForBestiaryConditionAsync(
            () => IsBestiaryCapturedBeastsTabVisible() && (GetBestiaryTotalCapturedBeastCount() > 0 || GetVisibleBestiaryCapturedBeasts().Count > 0),
            500,
            Math.Max(AutomationTiming.FastPollDelayMs, 25));
    }

    private async Task<int> WaitForBestiaryReleaseVisibleCountAsync(int previousVisibleCount)
    {
        await WaitForBestiaryConditionAsync(
            () => IsBestiaryCapturedBeastsTabVisible() && GetBestiaryTotalCapturedBeastCount() < previousVisibleCount,
            BestiaryReleaseTimeoutMs,
            AutomationTiming.BestiaryReleasePollDelayMs);
        EnsureBestiaryCapturedBeastsTabVisible("waiting for captured beasts to update");
        return GetBestiaryTotalCapturedBeastCount();
    }

    private async Task<int> WaitForBestiaryItemizeFreeCellCountAsync(int previousFreeCellCount)
    {
        await WaitForBestiaryConditionAsync(
            () => GetPlayerInventoryFreeCellCount() < previousFreeCellCount,
            BestiaryReleaseTimeoutMs,
            AutomationTiming.BestiaryReleasePollDelayMs);
        EnsureBestiaryCapturedBeastsTabVisible("waiting for itemized beasts to reach inventory");
        return GetPlayerInventoryFreeCellCount();
    }

    private async Task WaitForBestiaryDisplayedBeastsToStabilizeAsync(int timeoutMs = 350, int pollDelayMs = 25, int requiredStableSamples = 2)
    {
        if (!await WaitForBestiaryCapturedBeastsDisplayAsync())
        {
            return;
        }

        var adjustedTimeoutMs = Math.Max(1, timeoutMs);
        var adjustedPollDelayMs = Math.Max(1, pollDelayMs);
        var requiredStableSampleCount = Math.Max(1, requiredStableSamples);
        var startedAt = DateTime.UtcNow;
        var previousSnapshot = CaptureBestiaryDisplayedBeastSnapshot();
        var stableSampleCount = 0;

        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < adjustedTimeoutMs)
        {
            ThrowIfAutomationStopRequested();
            await DelayAutomationAsync(adjustedPollDelayMs);

            var currentSnapshot = CaptureBestiaryDisplayedBeastSnapshot();
            if (AreBestiaryDisplayedBeastSnapshotsEquivalent(previousSnapshot, currentSnapshot))
            {
                stableSampleCount++;
                if (stableSampleCount >= requiredStableSampleCount)
                {
                    return;
                }
            }
            else
            {
                stableSampleCount = 0;
                previousSnapshot = currentSnapshot;
            }
        }
    }

    private async Task<string> WaitForBestiarySearchRegexTextAsync(string expectedText, int timeoutMs)
    {
        return await PollAutomationValueAsync(
            GetBestiarySearchRegexText,
            text => string.Equals(text, expectedText, StringComparison.Ordinal),
            timeoutMs,
            AutomationTiming.FastPollDelayMs);
    }

    #endregion
    #region Bestiary element finders

    private Element TryGetBestiaryPanel()
    {
        var fixedPanel = TryGetElementByPathQuietly(BestiaryChallengesPanel, BestiaryPanelPath);
        if (fixedPanel?.IsVisible == true)
        {
            return fixedPanel;
        }

        var panelContainer = TryGetChildFromIndicesQuietly(BestiaryChallengesPanel, BestiaryPanelPath.Take(BestiaryPanelPath.Count - 1).ToArray());
        if (panelContainer?.Children == null)
        {
            return fixedPanel;
        }

        foreach (var candidate in panelContainer.Children)
        {
            if (candidate?.IsVisible != true)
            {
                continue;
            }

            if (TryGetBestiaryElementWithinPanel(candidate, BestiaryCapturedBeastsButtonContainerPath) != null ||
                TryGetBestiaryElementWithinPanel(candidate, BestiaryCapturedBeastsTabPath) != null)
            {
                return candidate;
            }
        }

        return fixedPanel;
    }

    private Element TryGetBestiaryCapturedBeastsTab()
    {
        var panel = TryGetBestiaryPanel();
        var fixedTab = TryGetBestiaryElementWithinPanel(panel, BestiaryCapturedBeastsTabPath);
        if (LooksLikeBestiaryCapturedBeastsTab(fixedTab))
        {
            return fixedTab;
        }

        return FindBestiaryCapturedBeastsTabDynamically(panel);
    }

    private Element TryGetBestiaryCapturedBeastsButton()
    {
        try
        {
            var fixedButton = TryGetBestiaryCapturedBeastsButtonContainer()?.Children?
                .FirstOrDefault(child => child != null && GetElementTextRecursive(child.Tooltip, 6)?.IndexOf("Captured Beasts", StringComparison.OrdinalIgnoreCase) >= 0);
            return fixedButton ?? TryFindBestiaryCapturedBeastsButtonWithin(TryGetBestiaryPanel());
        }
        catch
        {
            return null;
        }
    }

    private Element TryGetBestiaryCapturedBeastsButtonContainer()
    {
        try
        {
            var fixedContainer = TryGetBestiaryElementWithinResolvedPanel(BestiaryCapturedBeastsButtonContainerPath);
            if (fixedContainer?.IsVisible == true)
            {
                return fixedContainer;
            }

            return TryFindBestiaryCapturedBeastsButtonWithin(TryGetBestiaryPanel())?.Parent ?? fixedContainer;
        }
        catch
        {
            return null;
        }
    }

    private Element TryGetBestiaryChallengesBestiaryButton()
    {
        var challengeEntries = TryGetElementByPathQuietly(BestiaryChallengesPanel, BestiaryChallengesEntriesRootPath)?.Children;
        return challengeEntries?
            .FirstOrDefault(entry => entry?.IsVisible == true && TryGetChildFromIndicesQuietly(entry, BestiaryChallengesEntryTextPath)?.Text?.Trim().EqualsIgnoreCase("Bestiary") == true);
    }

    private Element TryGetBestiarySearchRegexTextElement()
    {
        var fixedElement = TryGetBestiaryElementWithinResolvedPanel(BestiarySearchRegexTextPath);
        if (fixedElement?.IsVisible == true)
        {
            return fixedElement;
        }

        var inputContainer = TryFindBestiaryFilterInputContainer(TryGetBestiaryCapturedBeastsTab());
        return inputContainer?.Children?
            .Where(child => child?.IsVisible == true)
            .OrderByDescending(child => child.GetClientRect().Width)
            .FirstOrDefault() ?? fixedElement;
    }

    private Element TryGetBestiaryElementWithinResolvedPanel(IReadOnlyList<int> absolutePath)
    {
        return TryGetBestiaryElementWithinPanel(TryGetBestiaryPanel(), absolutePath);
    }

    private Element TryGetBestiaryElementWithinPanel(Element panel, IReadOnlyList<int> absolutePath)
    {
        if (panel == null || absolutePath == null || absolutePath.Count <= BestiaryPanelPath.Count)
        {
            return null;
        }

        return TryGetChildFromIndicesQuietly(panel, absolutePath.Skip(BestiaryPanelPath.Count).ToArray());
    }

    private Element FindBestiaryCapturedBeastsTabDynamically(Element panel)
    {
        return GetVisibleBestiaryPageCandidates(panel)
            .OrderByDescending(candidate =>
            {
                var rect = candidate.GetClientRect();
                return rect.Width * rect.Height;
            })
            .FirstOrDefault(LooksLikeBestiaryCapturedBeastsTab);
    }

    private bool LooksLikeBestiaryCapturedBeastsTab(Element candidate)
    {
        if (candidate?.IsVisible != true)
        {
            return false;
        }

        var rect = candidate.GetClientRect();
        if (rect.Width < 300 || rect.Height < 300)
        {
            return false;
        }

        var footer = TryGetBestiaryCapturedBeastsFooter(candidate);
        var viewport = TryFindBestiaryCapturedBeastsViewport(candidate);
        var scrollbar = TryGetBestiaryCapturedBeastsScrollbar(candidate);
        return footer != null && viewport != null && scrollbar != null &&
               TryFindBestiaryFilterInputContainer(candidate) != null;
    }

    private IEnumerable<Element> GetVisibleBestiaryPageCandidates(Element panel)
    {
        var innerRoot = TryGetBestiaryInnerRoot(panel);
        return innerRoot?.Children?
            .Where(candidate => candidate?.IsVisible == true)
            .Where(candidate =>
            {
                var rect = candidate.GetClientRect();
                return rect.Width >= 300 && rect.Height >= 300;
            })
            ?? Enumerable.Empty<Element>();
    }

    private Element TryGetBestiaryInnerRoot(Element panel)
    {
        var innerRoot = panel?.GetChildAtIndex(0);
        return innerRoot?.Children != null ? innerRoot : null;
    }

    private Element TryGetBestiaryCapturedBeastsFooter(Element capturedTab)
    {
        var footer = capturedTab?.GetChildAtIndex(0);
        if (footer?.IsVisible != true)
        {
            return null;
        }

        var rect = footer.GetClientRect();
        return rect.Width >= 300 && rect.Height is >= 20 and <= 120 && (footer.Children?.Count ?? 0) >= 2
            ? footer
            : null;
    }

    private Element TryGetBestiaryCapturedBeastsScrollbar(Element capturedTab)
    {
        var scrollbar = capturedTab?.GetChildAtIndex(2);
        if (scrollbar?.IsVisible != true)
        {
            return null;
        }

        var rect = scrollbar.GetClientRect();
        return rect.Width is > 0 and <= 100 && rect.Height >= 300 && (scrollbar.Children?.Count ?? 0) >= 3
            ? scrollbar
            : null;
    }

    private Element TryFindBestiaryCapturedBeastsButtonWithin(Element panel)
    {
        return EnumerateDescendants(panel, includeSelf: true)
            .FirstOrDefault(candidate => candidate?.IsVisible == true && GetElementTextRecursive(candidate.Tooltip, 6)?.IndexOf("Captured Beasts", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private Element TryFindBestiaryFilterInputContainer(Element capturedTab)
    {
        foreach (var label in EnumerateDescendants(capturedTab, includeSelf: true))
        {
            if (label?.IsVisible != true || GetElementTextRecursive(label, 2)?.IndexOf("Filter Beasts", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var inputContainer = label.Parent?.Children?
                .Where(sibling => sibling != label && sibling?.IsVisible == true)
                .OrderByDescending(sibling => sibling.GetClientRect().Width)
                .FirstOrDefault();
            if (inputContainer != null)
            {
                return inputContainer;
            }
        }

        return null;
    }

    private Element TryFindBestiaryCapturedBeastsViewport(Element capturedTab)
    {
        var fixedViewport = capturedTab?.GetChildAtIndex(1);
        if (IsBestiaryCapturedBeastsViewportCandidate(fixedViewport))
        {
            return fixedViewport;
        }

        return capturedTab?.Children?
            .Where(IsBestiaryCapturedBeastsViewportCandidate)
            .OrderByDescending(child =>
            {
                var rect = child.GetClientRect();
                return rect.Width * rect.Height;
            })
            .FirstOrDefault();
    }

    private static bool IsBestiaryCapturedBeastsViewportCandidate(Element element)
    {
        if (element?.IsVisible != true)
        {
            return false;
        }

        var rect = element.GetClientRect();
        return rect.Width >= 300 && rect.Height >= 300;
    }

    private Element TryGetBestiaryDeleteButton(Element beastElement)
    {
        for (var current = beastElement; current != null; current = current.Parent)
        {
            var deleteButton = TryGetChildFromIndicesQuietly(current, BestiaryDeleteButtonPathFromBeastRow);
            if (deleteButton != null)
            {
                return deleteButton;
            }
        }

        return null;
    }

    #endregion
    #region Bestiary search

    private string GetBestiarySearchRegexText()
    {
        var textElement = TryGetBestiarySearchRegexTextElement();
        if (textElement == null)
        {
            return null;
        }

        return TryGetPropertyValueAsString(textElement, "Text")
               ?? textElement.Text
               ?? TryGetElementText(textElement)
               ?? GetElementTextRecursive(textElement, 1);
    }

    private Element TryGetBestiarySearchRegexFocusElement()
    {
        return TryFindBestiaryFilterInputContainer(TryGetBestiaryCapturedBeastsTab())
               ?? TryGetBestiarySearchRegexTextElement();
    }

    private async Task FocusBestiarySearchRegexInputAsync(int settleDelayMs = 30)
    {
        var searchFocusElement = TryGetBestiarySearchRegexFocusElement();
        if (searchFocusElement?.IsVisible != true)
        {
            return;
        }

        var timing = AutomationTiming;
        await ClickElementAsync(
            searchFocusElement,
            timing.UiClickPreDelayMs,
            Math.Max(timing.FastPollDelayMs, settleDelayMs),
            GetConfiguredBestiaryClickDelayMs());
    }

    private async Task<bool> EnsureBestiarySearchRegexInputReadyAsync(bool allowHotkeyFallback, bool forceHotkey = false)
    {
        if (!await WaitForBestiaryCapturedBeastsDisplayAsync())
        {
            EnsureBestiaryCapturedBeastsTabVisible("preparing the Bestiary search");
            throw new InvalidOperationException("Captured Beasts display is not ready while preparing the Bestiary search.");
        }

        if (!forceHotkey && TryGetBestiarySearchRegexFocusElement()?.IsVisible == true)
        {
            await FocusBestiarySearchRegexInputAsync();
            return false;
        }

        if (!allowHotkeyFallback)
        {
            return false;
        }

        var timing = AutomationTiming;
        await CtrlTapKeyAsync(Keys.F, timing.KeyTapDelayMs, timing.FastPollDelayMs);
        EnsureBestiaryCapturedBeastsTabVisible("opening the Bestiary search");
        await DelayForUiCheckAsync(timing.UiCheckInitialSettleDelayMs);
        await FocusBestiarySearchRegexInputAsync();
        return true;
    }

    private async Task ClearBestiarySearchRegexInputAsync(bool focusInput = true)
    {
        var timing = AutomationTiming;
        if (focusInput)
        {
            await FocusBestiarySearchRegexInputAsync();
        }

        await CtrlTapKeyAsync(Keys.A, timing.KeyTapDelayMs, timing.KeyTapDelayMs);
        await TapKeyAsync(Keys.Back, timing.KeyTapDelayMs, timing.FastPollDelayMs);
        await DelayForUiCheckAsync(timing.UiCheckInitialSettleDelayMs);
    }

    private async Task<string> PasteBestiarySearchRegexAsync(string regex, bool focusInput = true)
    {
        if (focusInput)
        {
            await FocusBestiarySearchRegexInputAsync();
        }

        await PasteClipboardAsync();
        await DelayForUiCheckAsync(AutomationTiming.UiCheckInitialSettleDelayMs);
        return await WaitForBestiarySearchRegexTextAsync(regex, 300) ?? GetBestiarySearchRegexText();
    }

    private bool CanReliablyReadBestiarySearchRegexText(Element textElement)
    {
        if (textElement == null)
        {
            return false;
        }

        return GetBestiarySearchRegexReadCandidates(textElement).Any(ContainsReadableBestiarySearchText);
    }

    private IEnumerable<string> GetBestiarySearchRegexReadCandidates(Element textElement)
    {
        yield return TryGetPropertyValueAsString(textElement, "TextNoTags")?.Trim();
        yield return TryInvokeMethodAsString(textElement, "GetTextWithNoTags", 512)?.Trim();
        yield return TryGetPropertyValueAsString(textElement, "Text")?.Trim();
        yield return textElement.Text?.Trim();
        yield return TryGetElementText(textElement)?.Trim();
        yield return GetElementTextRecursive(textElement, 1)?.Trim();
    }

    private static bool ContainsReadableBestiarySearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (ch > 127)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch) || ch is '|' or ',' or ' ' or '-' or '_' or '(' or ')' or '.' or '+' or '*')
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetBestiaryObservedSearchRegex(out string observedRegex)
    {
        observedRegex = null;

        var textElement = TryGetBestiarySearchRegexTextElement();
        if (!CanReliablyReadBestiarySearchRegexText(textElement))
        {
            return false;
        }

        observedRegex = GetBestiarySearchRegexReadCandidates(textElement)
            .Where(ContainsReadableBestiarySearchText)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault();
        return !string.IsNullOrWhiteSpace(observedRegex);
    }

    private static string TryInvokeMethodAsString(object instance, string methodName, params object[] args)
    {
        try
        {
            var types = args?.Select(arg => arg?.GetType() ?? typeof(object)).ToArray() ?? Array.Empty<Type>();
            return instance?.GetType().GetMethod(methodName, types)?.Invoke(instance, args)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task ApplyBestiarySearchRegexAsync(string regex)
    {
        ThrowIfAutomationStopRequested();
        if (!await WaitForBestiaryCapturedBeastsDisplayAsync())
        {
            EnsureBestiaryCapturedBeastsTabVisible("applying the Bestiary search");
            throw new InvalidOperationException("Captured Beasts display is not ready while applying the Bestiary search.");
        }

        if (string.IsNullOrWhiteSpace(regex))
        {
            throw new InvalidOperationException("Configured Bestiary Regex is empty.");
        }

        var timing = AutomationTiming;

        if (TryGetBestiaryObservedSearchRegex(out var existingRegex) &&
            string.Equals(existingRegex, regex, StringComparison.Ordinal))
        {
            LogDebug($"Bestiary search regex already matches configured value. leaving existing filter unchanged. value='{regex}'");
            await WaitForBestiaryDisplayedBeastsToStabilizeAsync();
            return;
        }

        ReleaseAutomationModifierKeys();
        ImGui.SetClipboardText(regex);

        var openedSearchWithHotkey = await EnsureBestiarySearchRegexInputReadyAsync(allowHotkeyFallback: true);
        await ClearBestiarySearchRegexInputAsync(focusInput: false);

        var searchTextElement = TryGetBestiarySearchRegexTextElement();
        var observedRegex = await PasteBestiarySearchRegexAsync(regex, focusInput: false);
        if (!string.Equals(observedRegex, regex, StringComparison.Ordinal))
        {
            if (!CanReliablyReadBestiarySearchRegexText(searchTextElement))
            {
                LogDebug($"Bestiary search regex readback unavailable after paste. continuing without strict verification. expected='{regex}', observed='{observedRegex ?? "<null>"}', path={DescribePath(BestiarySearchRegexTextPath)}, pathTrace={DescribePathLookup(GameController?.IngameState?.IngameUi?.ChallengesPanel, BestiarySearchRegexTextPath)}, element={DescribeElement(searchTextElement)}");
            }
            else
            {
                LogDebug($"Bestiary search regex mismatch after paste attempt 1. retrying after clearing input. expected='{regex}', observed='{observedRegex ?? "<null>"}', path={DescribePath(BestiarySearchRegexTextPath)}, pathTrace={DescribePathLookup(GameController?.IngameState?.IngameUi?.ChallengesPanel, BestiarySearchRegexTextPath)}, element={DescribeElement(searchTextElement)}");

                if (!openedSearchWithHotkey)
                {
                    openedSearchWithHotkey = await EnsureBestiarySearchRegexInputReadyAsync(allowHotkeyFallback: true, forceHotkey: true);
                }

                await ClearBestiarySearchRegexInputAsync(focusInput: false);
                observedRegex = await PasteBestiarySearchRegexAsync(regex, focusInput: false);
                if (!string.Equals(observedRegex, regex, StringComparison.Ordinal))
                {
                    LogDebug($"Bestiary search regex mismatch after paste attempt 2. expected='{regex}', observed='{observedRegex ?? "<null>"}', path={DescribePath(BestiarySearchRegexTextPath)}, pathTrace={DescribePathLookup(GameController?.IngameState?.IngameUi?.ChallengesPanel, BestiarySearchRegexTextPath)}, element={DescribeElement(searchTextElement)}");
                    throw new InvalidOperationException("Bestiary search regex did not match the configured value after paste.");
                }
            }
        }

        await TapKeyAsync(Keys.Enter, timing.KeyTapDelayMs, timing.FastPollDelayMs);
        await DelayForUiCheckAsync(100);
        await WaitForBestiaryDisplayedBeastsToStabilizeAsync();
    }

    #endregion
    #region Bestiary UI interaction

    private Task CloseBestiaryWorldUiAsync() => BestiaryUi.CloseWorldUiAsync();

    private Task EnsureBestiaryCapturedBeastsWindowOpenAsync(bool openViaChallengesHotkey = false) =>
        BestiaryUi.EnsureCapturedBeastsWindowOpenAsync(openViaChallengesHotkey);

    private async Task CtrlClickWorldEntityAsync(Entity entity)
    {
        var timing = AutomationTiming;
        var entityLabel = DescribeEntity(entity);
        var clicked = await TryInteractWithWorldEntityAsync(
            entity,
            entityLabel,
            statusMessage: null,
            missingPositionStatus: null,
            hoverFailureStatus: null,
            MouseButtons.Left,
            timing.UiClickPreDelayMs,
            timing.MinTabClickPostDelayMs,
            modifierKeys: [Keys.LControlKey]);
        if (clicked)
        {
            return;
        }

        if (entity?.GetComponent<Render>() == null)
        {
            throw new InvalidOperationException($"Could not find a clickable world position for {entityLabel}.");
        }

        throw new InvalidOperationException($"Could not hover {entityLabel} before clicking.");
    }

    #endregion
    #region Bestiary beast list

    private string GetBestiaryBeastLabel(Element beastElement) => BestiaryCapturedBeastsView.GetBestiaryBeastLabel(beastElement);

    private List<Element> GetVisibleBestiaryCapturedBeasts() => BestiaryCapturedBeastsView.GetVisibleCapturedBeasts();

    private int GetBestiaryTotalCapturedBeastCount() => BestiaryCapturedBeastsView.GetTotalCapturedBeastCount();

    private BestiaryDisplayedBeastSnapshot CaptureBestiaryDisplayedBeastSnapshot()
    {
        var visibleBeasts = GetVisibleBestiaryCapturedBeasts();
        if (visibleBeasts.Count <= 0)
        {
            return new(0, string.Empty, default);
        }

        var firstBeast = visibleBeasts[0];
        return new(
            visibleBeasts.Count,
            GetBestiaryBeastLabel(firstBeast) ?? string.Empty,
            firstBeast.GetClientRect());
    }

    private static bool AreBestiaryDisplayedBeastSnapshotsEquivalent(BestiaryDisplayedBeastSnapshot left, BestiaryDisplayedBeastSnapshot right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.Count != right.Count || !left.FirstLabel.Equals(right.FirstLabel, StringComparison.Ordinal))
        {
            return false;
        }

        return Math.Abs(left.FirstRect.Left - right.FirstRect.Left) < 1f &&
               Math.Abs(left.FirstRect.Top - right.FirstRect.Top) < 1f &&
               Math.Abs(left.FirstRect.Right - right.FirstRect.Right) < 1f &&
               Math.Abs(left.FirstRect.Bottom - right.FirstRect.Bottom) < 1f;
    }

    #endregion
    #region Bestiary clear and stash

    private Task<int> ClearCapturedBeastsAsync() => BestiaryClear.ClearAsync();

    private Element ResolveBestiaryClickTarget(Element beastElement, bool deleteBeasts)
    {
        if (beastElement == null)
        {
            throw new InvalidOperationException("Could not resolve the Bestiary release click position.");
        }

        if (!deleteBeasts)
        {
            return beastElement;
        }

        var clickTarget = TryGetBestiaryDeleteButton(beastElement);
        if (clickTarget != null)
        {
            return clickTarget;
        }

        LogDebug($"Could not resolve Bestiary delete button for beast. beast={DescribeElement(beastElement)}, parent={DescribeElement(beastElement.Parent)}");
        throw new InvalidOperationException("Could not resolve the Bestiary delete button.");
    }

    private bool CanClickBestiaryBeast(Element beastElement, bool deleteBeasts)
    {
        if (beastElement?.IsVisible != true)
        {
            return false;
        }

        if (!deleteBeasts)
        {
            return true;
        }

        if (beastElement.Parent == null)
        {
            return false;
        }

        var deleteButton = TryGetBestiaryDeleteButton(beastElement);
        if (deleteButton == null)
        {
            return false;
        }

        var rect = deleteButton.GetClientRect();
        return rect.Width > 0 && rect.Height > 0;
    }

    private async Task HoverBestiaryDeleteButtonAsync(Element deleteButton, Element beastElement)
    {
        if (deleteButton == null)
        {
            throw new InvalidOperationException("Cannot hover a null Bestiary delete button.");
        }

        var timing = AutomationTiming;
        var hoverPollDelayMs = Math.Max(5, timing.FastPollDelayMs);
        var hoverTimeoutMs = Math.Max(90, timing.UiClickPreDelayMs + hoverPollDelayMs * 4);
        var hovered = await PollAutomationValueAsync(
            () => IsHoveringBestiaryDeleteButton(deleteButton),
            isHovered => isHovered,
            hoverTimeoutMs,
            hoverPollDelayMs,
            onPendingAsync: _ =>
            {
                SetAutomationCursorPosition(deleteButton.GetClientRect().Center);
                return Task.CompletedTask;
            });

        if (hovered)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Could not confirm Bestiary delete button hover before clicking. beast={DescribeElement(beastElement)}, deleteButton={DescribeElement(deleteButton)}");
    }

    private bool IsHoveringBestiaryDeleteButton(Element deleteButton)
    {
        if (deleteButton == null)
        {
            return false;
        }

        var rect = deleteButton.GetClientRect();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        if (deleteButton.HasShinyHighlight)
        {
            return true;
        }
        
        var buttonAddress = deleteButton.Address;
        if (buttonAddress == 0)
        {
            return false;
        }

        var hoverElement = GameController?.IngameState?.UIHoverElement;
        if (DoesElementMatchOrDescendFromAddress(hoverElement, buttonAddress))
        {
            return true;
        }

        return false;
    }

    private static bool DoesElementMatchOrDescendFromAddress(Element element, long targetAddress)
    {
        if (element == null || targetAddress == 0)
        {
            return false;
        }

        for (var current = element; current != null; current = current.Parent)
        {
            if (current.Address == targetAddress)
            {
                return true;
            }
        }

        return false;
    }

    private int GetConfiguredBestiaryClickDelayMs()
    {
        return Math.Max(0, Settings?.AutomationTiming?.BestiaryClickDelayMs?.Value ?? 0);
    }

    private async Task ClickBestiaryBeastAsync(Element beastElement, bool deleteBeasts)
    {
        var clickTarget = ResolveBestiaryClickTarget(beastElement, deleteBeasts);
        var timing = AutomationTiming;
        var bestiaryClickDelayMs = GetConfiguredBestiaryClickDelayMs();

        if (deleteBeasts)
        {
            await HoverBestiaryDeleteButtonAsync(clickTarget, beastElement);
            await ClickCurrentCursorWithModifiersAsync(
                MouseButtons.Left,
                timing.CtrlClickPreDelayMs,
                timing.CtrlClickPostDelayMs,
                configuredClickDelayOverrideMs: bestiaryClickDelayMs,
                modifierKeys: [Keys.LControlKey]);
            return;
        }

        await ClickElementWithModifiersAsync(
            clickTarget,
            MouseButtons.Left,
            preClickDelayMs: timing.BestiaryItemizeClickPreDelayMs,
            postClickDelayMs: timing.BestiaryItemizeClickPostDelayMs,
            configuredClickDelayOverrideMs: bestiaryClickDelayMs,
            modifierKeys: []);
    }

    private async Task<int> StashCapturedMonstersAndReturnToBestiaryAsync()
    {
        return await BestiaryCapturedMonsterStash.CompleteAsync(reopenBestiaryWindow: true);
    }

    private async Task<int> StashCapturedMonstersAndCloseUiAsync()
    {
        return await BestiaryCapturedMonsterStash.CompleteAsync(reopenBestiaryWindow: false);
    }

    #endregion
}
