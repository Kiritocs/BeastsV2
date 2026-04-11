using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore.PoEMemory.Elements.InventoryElements;
using BeastsV2.Runtime.State;

namespace BeastsV2.Runtime.Automation;

internal sealed record BestiaryCapturedMonsterStashCallbacks(
    Func<Task> CloseBestiaryWorldUiAsync,
    Func<Task<bool>> EnsureStashOpenForAutomationAsync,
    Func<bool, int> ResolveBestiaryCapturedMonsterStashTabIndex,
    Func<string, string, string, int> ResolveConfiguredTabIndex,
    Func<IReadOnlyList<NormalInventoryItem>> GetVisibleCapturedMonsterInventoryItems,
    Func<IReadOnlyList<NormalInventoryItem>, string, string> BuildVisibleStashCapacityFailureMessage,
    Action<string, bool> UpdateAutomationStatus,
    Action ThrowIfAutomationStopRequested,
    Func<int, Task> SelectStashTabAsync,
    Func<NormalInventoryItem, Task> CtrlClickInventoryItemAsync,
    Func<int, Task<int>> WaitForCapturedMonsterInventoryItemCountToChangeAsync,
    Func<int, Task> DelayForUiCheckAsync,
    Func<int, Task> DelayAutomationAsync,
    Func<bool> IsStashVisible,
    Func<bool, Task> EnsureCapturedBeastsWindowOpenAsync,
    Func<string, Task> ApplyBestiarySearchRegexAsync,
    Func<NormalInventoryItem, bool> IsRedCapturedMonsterInventoryItem,
    Func<BestiaryAutomationSettings> GetBestiarySettings,
    Func<int> GetTabSwitchDelayMs,
    Func<int> GetClickDelayMs);

internal sealed class BestiaryCapturedMonsterStashService
{
    private readonly AutomationRuntimeState _state;
    private readonly BestiaryCapturedMonsterStashCallbacks _callbacks;

    private sealed record StashContext(int ItemizedBeastTabIndex, int RedBeastTabIndex, string ItemizedBeastTabName, string RedBeastTabName);
    private sealed record StashCapacityGroup(int ConfiguredTabIndex, string DestinationDescription, IReadOnlyList<NormalInventoryItem> Items);
    private sealed record StashProgress(int CurrentConfiguredTabIndex, int MovedCount, int ConsecutiveFailures);

    public BestiaryCapturedMonsterStashService(AutomationRuntimeState state, BestiaryCapturedMonsterStashCallbacks callbacks)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task<int> CompleteAsync(bool reopenBestiaryWindow)
    {
        var movedCount = await StashCapturedMonstersIntoConfiguredTabAsync();
        await _callbacks.CloseBestiaryWorldUiAsync();
        if (!reopenBestiaryWindow)
        {
            return movedCount;
        }

        await _callbacks.EnsureCapturedBeastsWindowOpenAsync(false);
        if (!string.IsNullOrWhiteSpace(_state.ActiveBestiarySearchRegex))
        {
            await _callbacks.ApplyBestiarySearchRegexAsync(_state.ActiveBestiarySearchRegex);
        }

        return movedCount;
    }

    private async Task<int> StashCapturedMonstersIntoConfiguredTabAsync()
    {
        if (_callbacks.GetVisibleCapturedMonsterInventoryItems().Count <= 0)
        {
            return 0;
        }

        var stashContext = await PrepareCapturedMonsterStashContextAsync();
    await EnsureCapturedMonsterStashHasCapacityAsync(stashContext);
        var progress = new StashProgress(int.MinValue, 0, 0);

        _callbacks.UpdateAutomationStatus("Stashing itemized beasts...", false);

        while (true)
        {
            _callbacks.ThrowIfAutomationStopRequested();

            progress = await EnsureCapturedMonsterStashVisibleAsync(progress);

            var capturedMonsterItems = _callbacks.GetVisibleCapturedMonsterInventoryItems();
            if (capturedMonsterItems.Count <= 0)
            {
                return progress.MovedCount;
            }

            var nextItem = capturedMonsterItems[0];
            progress = await EnsureCapturedMonsterStashTabSelectedAsync(nextItem, stashContext, progress);
            progress = await StashNextCapturedMonsterAsync(nextItem, capturedMonsterItems.Count, progress);
        }
    }

    private async Task<StashContext> PrepareCapturedMonsterStashContextAsync()
    {
        await _callbacks.CloseBestiaryWorldUiAsync();
        if (!await _callbacks.EnsureStashOpenForAutomationAsync())
        {
            throw new InvalidOperationException("Could not open the stash to store itemized beasts.");
        }

        var itemizedBeastTabIndex = _callbacks.ResolveBestiaryCapturedMonsterStashTabIndex(false);
        if (itemizedBeastTabIndex < 0)
        {
            throw new InvalidOperationException("Select an Itemized Beasts stash tab before auto-stashing itemized beasts.");
        }

        var bestiarySettings = _callbacks.GetBestiarySettings();
        return new StashContext(
            itemizedBeastTabIndex,
            _callbacks.ResolveConfiguredTabIndex(
                bestiarySettings?.SelectedRedBeastTabName.Value,
                "Red beasts",
                "Bestiary automation red beast stash"),
            bestiarySettings?.SelectedTabName.Value?.Trim(),
            bestiarySettings?.SelectedRedBeastTabName.Value?.Trim());
    }

    private async Task EnsureCapturedMonsterStashHasCapacityAsync(StashContext stashContext)
    {
        var capturedMonsterItems = _callbacks.GetVisibleCapturedMonsterInventoryItems();
        if (capturedMonsterItems.Count <= 0)
        {
            return;
        }

        foreach (var group in BuildStashCapacityGroups(capturedMonsterItems, stashContext))
        {
            await _callbacks.SelectStashTabAsync(group.ConfiguredTabIndex);
            await _callbacks.DelayAutomationAsync(_callbacks.GetTabSwitchDelayMs());

            var failureMessage = _callbacks.BuildVisibleStashCapacityFailureMessage(group.Items, group.DestinationDescription);
            if (string.IsNullOrWhiteSpace(failureMessage))
            {
                continue;
            }

            _callbacks.UpdateAutomationStatus(failureMessage, true);
            throw new InvalidOperationException(failureMessage);
        }
    }

    private IEnumerable<StashCapacityGroup> BuildStashCapacityGroups(IReadOnlyList<NormalInventoryItem> capturedMonsterItems, StashContext stashContext)
    {
        return capturedMonsterItems
            .GroupBy(item => ResolveCapturedMonsterStashTabIndex(item, stashContext))
            .Select(group => new StashCapacityGroup(
                group.Key,
                BuildCapturedMonsterStashDestinationDescription(group.FirstOrDefault(), stashContext),
                group.ToList()));
    }

    private string BuildCapturedMonsterStashDestinationDescription(NormalInventoryItem item, StashContext stashContext)
    {
        var useRedBeastTab = _callbacks.IsRedCapturedMonsterInventoryItem(item) && stashContext.RedBeastTabIndex >= 0;
        var configuredTabName = useRedBeastTab ? stashContext.RedBeastTabName : stashContext.ItemizedBeastTabName;
        var tabLabel = useRedBeastTab ? "red-beast stash tab" : "itemized-beast stash tab";
        return string.IsNullOrWhiteSpace(configuredTabName)
            ? tabLabel
            : $"{tabLabel} '{configuredTabName}'";
    }

    private async Task<StashProgress> EnsureCapturedMonsterStashVisibleAsync(StashProgress progress)
    {
        if (_callbacks.IsStashVisible())
        {
            return progress;
        }

        if (!await _callbacks.EnsureStashOpenForAutomationAsync())
        {
            throw new InvalidOperationException("Could not reopen the stash while storing itemized beasts.");
        }

        return progress with { CurrentConfiguredTabIndex = int.MinValue };
    }

    private async Task<StashProgress> EnsureCapturedMonsterStashTabSelectedAsync(
        NormalInventoryItem nextItem,
        StashContext stashContext,
        StashProgress progress)
    {
        var configuredTabIndex = ResolveCapturedMonsterStashTabIndex(nextItem, stashContext);
        if (configuredTabIndex == progress.CurrentConfiguredTabIndex)
        {
            return progress;
        }

        await _callbacks.SelectStashTabAsync(configuredTabIndex);
        await _callbacks.DelayAutomationAsync(_callbacks.GetTabSwitchDelayMs());
        return progress with { CurrentConfiguredTabIndex = configuredTabIndex };
    }

    private int ResolveCapturedMonsterStashTabIndex(NormalInventoryItem nextItem, StashContext stashContext)
    {
        return _callbacks.IsRedCapturedMonsterInventoryItem(nextItem) && stashContext.RedBeastTabIndex >= 0
            ? stashContext.RedBeastTabIndex
            : stashContext.ItemizedBeastTabIndex;
    }

    private async Task<StashProgress> StashNextCapturedMonsterAsync(
        NormalInventoryItem nextItem,
        int previousCount,
        StashProgress progress)
    {
        await _callbacks.CtrlClickInventoryItemAsync(nextItem);
        var currentCount = await WaitForConfirmedCapturedMonsterStashCountAsync(previousCount);
        var updatedProgress = ResolveCapturedMonsterStashProgress(progress, previousCount, currentCount);
        await DelayAfterCapturedMonsterStashAttemptAsync(previousCount, currentCount);
        return updatedProgress;
    }

    private async Task<int> WaitForConfirmedCapturedMonsterStashCountAsync(int previousCount)
    {
        var currentCount = await _callbacks.WaitForCapturedMonsterInventoryItemCountToChangeAsync(previousCount);
        if (currentCount < previousCount)
        {
            return currentCount;
        }

        await _callbacks.DelayForUiCheckAsync(250);
        return _callbacks.GetVisibleCapturedMonsterInventoryItems().Count;
    }

    private StashProgress ResolveCapturedMonsterStashProgress(StashProgress progress, int previousCount, int currentCount)
    {
        if (currentCount >= previousCount)
        {
            var currentConfiguredTabIndex = _callbacks.IsStashVisible()
                ? progress.CurrentConfiguredTabIndex
                : int.MinValue;
            var consecutiveFailures = progress.ConsecutiveFailures + 1;
            if (consecutiveFailures >= 3)
            {
                throw new InvalidOperationException("Stashing itemized beasts stalled while moving beasts into the stash.");
            }

            return progress with
            {
                CurrentConfiguredTabIndex = currentConfiguredTabIndex,
                ConsecutiveFailures = consecutiveFailures
            };
        }

        return progress with
        {
            MovedCount = progress.MovedCount + (previousCount - currentCount),
            ConsecutiveFailures = 0
        };
    }

    private async Task DelayAfterCapturedMonsterStashAttemptAsync(int previousCount, int currentCount)
    {
        var fastPollDelay = 15;
        var clickDelay = _callbacks.GetClickDelayMs();
        await _callbacks.DelayAutomationAsync(currentCount >= previousCount ? fastPollDelay : clickDelay);
    }
}