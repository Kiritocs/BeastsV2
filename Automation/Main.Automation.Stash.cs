using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2;

public partial class Main
{
    private sealed record FragmentScarabTabResolution(Element ScarabTab, int Attempts, bool Aborted);

    #region Stash interaction

    private bool IsAutomationStashVisible() =>
        GameController?.IngameState?.IngameUi?.StashElement?.IsVisible == true;

    private async Task<bool> EnsureStashOpenForAutomationAsync()
    {
        var timing = AutomationTiming;
        return await EnsurePollingAutomationOpenAsync(
            IsAutomationStashVisible,
            () => TryAdvanceWorldEntityOpenStepAsync(
                () => FindNearestAutomationEntity(entity => entity.Type == EntityType.Stash, requireVisible: true),
                () => FindNearestAutomationEntity(entity => entity.Type == EntityType.Stash, requireVisible: false),
                "stash",
                "stash",
                "Could not find a stash in the current area.",
                "Could not navigate to the stash.",
                ClickStashEntityAsync),
            timing.StashOpenPollDelayMs);
    }

    private async Task<bool> ClickStashEntityAsync(Entity stashEntity, string statusMessage)
    {
        var timing = AutomationTiming;
        return await TryInteractWithWorldEntityAsync(
            stashEntity,
            "stash",
            statusMessage,
            "Could not find a clickable stash position.",
            "Could not hover the stash.",
            MouseButtons.Left,
            timing.UiClickPreDelayMs,
            timing.OpenStashPostClickDelayMs);
    }

    private float? GetPlayerDistanceToEntity(Entity entity)
    {
        var pp = GameController?.Game?.IngameState?.Data?.LocalPlayer?.GetComponent<Positioned>();
        var ep = entity?.GetComponent<Positioned>();
        return pp != null && ep != null ? Vector2.Distance(pp.GridPosNum, ep.GridPosNum) : null;
    }

    private static bool IsScreenPositionVisible(Vector2 position, float width, float height)
    {
        return !float.IsNaN(position.X) && !float.IsNaN(position.Y) &&
               !float.IsInfinity(position.X) && !float.IsInfinity(position.Y) &&
               position.X >= 0 && position.Y >= 0 && position.X <= width && position.Y <= height;
    }

    #endregion
    #region Stash item queries

    private IList<NormalInventoryItem> GetVisibleStashItems() => GameController?.IngameState?.IngameUi?.StashElement?.VisibleStash?.VisibleInventoryItems;

    private int GetVisibleMatchingItemQuantity(string metadata) =>
        GetVisibleMatchingQuantity(GetVisibleStashItems, metadata, CountMatchingItemQuantity);

    private int? TryGetVisibleStashMatchingQuantity(string metadata) =>
        TryGetVisibleMatchingQuantity(GetVisibleStashItems, metadata, CountMatchingItemQuantity);

    private string BuildVisibleBestiaryStashCapacityFailureMessage(IReadOnlyList<NormalInventoryItem> items, string destinationDescription)
    {
        if (items == null || items.Count <= 0)
        {
            return null;
        }

        if (!TryGetVisibleStashOccupiedCells(out var occupiedCells, out var columns, out var rows))
        {
            return $"Bestiary auto-stash preflight failed. Could not inspect {destinationDescription} capacity.";
        }

        var requiredFootprints = items.Select(GetVisibleInventoryItemFootprint).ToList();
        if (CanFitItemFootprintsInGrid(occupiedCells, columns, rows, requiredFootprints))
        {
            LogDebug($"Bestiary auto-stash preflight passed for {destinationDescription}. requiredItems={requiredFootprints.Count}, requiredCells={CountRequiredGridCells(requiredFootprints)}, freeCells={CountFreeGridCells(occupiedCells, columns, rows)}");
            return null;
        }

        var requiredCells = CountRequiredGridCells(requiredFootprints);
        var freeCells = CountFreeGridCells(occupiedCells, columns, rows);
        var spaceDetail = freeCells < requiredCells
            ? $"needs {requiredCells} stash cells but only {freeCells} are free"
            : $"has {freeCells} free cells, but the remaining layout cannot fit the required item footprints ({requiredCells} total cells)";
        return $"Bestiary auto-stash preflight failed. {destinationDescription} {spaceDetail}.";
    }

    #endregion
    #region Stash tab helpers

    private int ResolveConfiguredTabIndex(StashAutomationTargetSettings target)
    {
        return ResolveConfiguredTabIndex(target?.SelectedTabName.Value, target?.ItemName.Value, "target");
    }

    private async Task WaitForTargetStashReadyAsync(StashAutomationTargetSettings target, int tabIndex)
    {
        var timing = AutomationTiming;
        var tabSwitchDelayMs = GetConfiguredTabSwitchDelayMs();
        var expectedInventoryType = GetExpectedInventoryType(target);
        var timeoutMs = Math.Max(
            timing.VisibleTabTimeoutMs,
            Math.Max(1500, tabSwitchDelayMs));

        var stashReady = await WaitForAutomationConditionAsync(
            () => IsTargetStashReady(GameController?.IngameState?.IngameUi?.StashElement, tabIndex, expectedInventoryType),
            timeoutMs,
            timing.FastPollDelayMs);
        if (stashReady)
        {
            return;
        }

        LogDebug($"WaitForTargetStashReadyAsync timed out. targetTab={tabIndex}, expectedType={(expectedInventoryType.HasValue ? expectedInventoryType.Value.ToString() : "any")}, stash={DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
        throw new InvalidOperationException($"Timed out loading stash tab {tabIndex}.");
    }

    private static InventoryType? GetExpectedInventoryType(StashAutomationTargetSettings target)
    {
        var selectedTabName = target?.SelectedTabName.Value?.Trim();
        if (TryGetConfiguredMapTier(target).HasValue || selectedTabName.EqualsIgnoreCase("Maps"))
        {
            return InventoryType.MapStash;
        }

        if (selectedTabName.EqualsIgnoreCase("Fragments"))
        {
            return InventoryType.FragmentStash;
        }

        return null;
    }

    private static bool IsTargetStashReady(StashElement stash, int tabIndex, InventoryType? expectedInventoryType)
    {
        if (stash?.IsVisible != true || stash.IndexVisibleStash != tabIndex || stash.VisibleStash == null)
        {
            return false;
        }

        if (stash.VisibleStash.InvType == InventoryType.InvalidInventory)
        {
            return false;
        }

        return !expectedInventoryType.HasValue || stash.VisibleStash.InvType == expectedInventoryType.Value;
    }

    private int ResolveBestiaryCapturedMonsterStashTabIndex(bool preferRedBeastTab)
    {
        if (preferRedBeastTab)
        {
            var redBeastTabIndex = ResolveConfiguredTabIndex(
                Settings?.BestiaryAutomation?.SelectedRedBeastTabName.Value,
                "Red beasts",
                "Bestiary automation red beast stash");
            if (redBeastTabIndex >= 0)
            {
                return redBeastTabIndex;
            }
        }

        return ResolveConfiguredTabIndex(
            Settings?.BestiaryAutomation?.SelectedTabName.Value,
            preferRedBeastTab ? "Itemized beasts (fallback)" : "Itemized beasts",
            preferRedBeastTab ? "Bestiary automation captured beast stash fallback" : "Bestiary automation stash");
    }

    private int ResolveConfiguredTabIndex(string configuredTabNameValue, string subjectLabel, string selectionContext)
    {
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        if (stash?.IsVisible != true)
        {
            LogDebug("ResolveConfiguredTabIndex aborted because stash is not visible.");
            return -1;
        }

        var stashTabNames = GetAvailableStashTabNames(stash);
        var configuredTabName = configuredTabNameValue?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredTabName))
        {
            for (var i = 0; i < stashTabNames.Count; i++)
            {
                if (!stashTabNames[i].EqualsIgnoreCase(configuredTabName))
                {
                    continue;
                }

                return i;
            }

            LogDebug($"Configured tab '{configuredTabName}' for {selectionContext} was not found. Available tabs: {string.Join(", ", stashTabNames.Select((name, index) => $"{index}:{name}"))}");
        }

        if (string.IsNullOrWhiteSpace(configuredTabName))
        {
            LogDebug($"No configured stash tab name for {selectionContext} '{subjectLabel}'. Available tabs: {string.Join(", ", stashTabNames.Select((name, index) => $"{index}:{name}"))}");
        }

        return -1;
    }

    private static List<string> GetAvailableStashTabNames(StashElement stash)
    {
        var totalStashes = (int)stash.TotalStashes;
        var names = new List<string>(totalStashes);
        var inventories = stash.Inventories;
        for (var i = 0; i < totalStashes; i++)
        {
            var name = i >= 0 && i < inventories.Count
                ? inventories[i]?.TabName
                : null;
            names.Add(string.IsNullOrWhiteSpace(name) ? $"Tab {i}" : name);
        }

        return names;
    }

    private bool IsVisibleStashTabReady(int tabIndex)
    {
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        return stash?.IsVisible == true && stash.IndexVisibleStash == tabIndex && stash.VisibleStash != null;
    }

    private async Task SelectStashTabAsync(int tabIndex)
    {
        var timing = AutomationTiming;
        var tabSwitchDelayMs = GetConfiguredTabSwitchDelayMs();
        var stash = GetValidatedVisibleStashForSelection(tabIndex, "Stash is not open.");

        if (IsVisibleStashTabReady(tabIndex))
        {
            LogDebug($"SelectStashTabAsync skipping because stash tab {tabIndex} is already visible.");
            return;
        }

        LogDebug($"Selecting stash tab {tabIndex}. Starting state: {DescribeStash(stash)}");

        var maxSteps = Math.Max(3, (int)stash.TotalStashes * 2);
        for (var step = 0; step < maxSteps; step++)
        {
            ThrowIfAutomationStopRequested();
            stash = GetValidatedVisibleStashForSelection(tabIndex, "Stash closed while switching tabs.");
            if (await TryCompleteStashTabSelectionAsync(stash, tabIndex, step))
            {
                return;
            }

            await AdvanceStashTabSelectionStepAsync(tabIndex, stash.IndexVisibleStash, step, maxSteps, tabSwitchDelayMs, timing);
        }

        LogDebug($"SelectStashTabAsync exhausted step loop for targetIndex={tabIndex}. Waiting for visible tab. {DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
        await WaitForVisibleTabAsync(tabIndex);
    }

    private StashElement GetValidatedVisibleStashForSelection(int tabIndex, string closedMessage)
    {
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        if (stash?.IsVisible != true)
        {
            throw new InvalidOperationException(closedMessage);
        }

        if (tabIndex < 0 || tabIndex >= stash.TotalStashes)
        {
            LogDebug($"SelectStashTabAsync received invalid tab index {tabIndex}. {DescribeStash(stash)}");
            throw new InvalidOperationException("Select a valid stash tab name before running restock.");
        }

        return stash;
    }

    private async Task<bool> TryCompleteStashTabSelectionAsync(StashElement stash, int tabIndex, int step)
    {
        if (stash?.IndexVisibleStash != tabIndex)
        {
            return false;
        }

        LogDebug($"SelectStashTabAsync reached target tab index {tabIndex} after {step} steps. Waiting for stash contents to load.");
        await WaitForVisibleTabAsync(tabIndex);
        return true;
    }

    private async Task AdvanceStashTabSelectionStepAsync(
        int tabIndex,
        int currentIndex,
        int step,
        int maxSteps,
        int tabSwitchDelayMs,
        AutomationTimingValues timing)
    {
        var key = tabIndex < currentIndex ? Keys.Left : Keys.Right;
        LogDebug($"SelectStashTabAsync step {step + 1}/{maxSteps}. currentIndex={currentIndex}, targetIndex={tabIndex}, key={key}");
        await TapKeyAsync(key, timing.KeyTapDelayMs, 0);

        var changedIndex = await WaitForVisibleTabIndexChangeAsync(currentIndex, Math.Max(timing.TabChangeTimeoutMs, tabSwitchDelayMs));
        LogDebug($"SelectStashTabAsync step {step + 1} result. previousIndex={currentIndex}, changedIndex={changedIndex}");
        if (changedIndex == currentIndex)
        {
            await DelayAutomationAsync(Math.Max(timing.TabRetryDelayMs, tabSwitchDelayMs / 2));
        }
    }

    private async Task EnsureFragmentStashScarabTabSelectedAsync()
    {
        var timing = AutomationTiming;
        var tabSwitchDelayMs = GetConfiguredTabSwitchDelayMs();
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        if (stash?.IsVisible != true || stash.VisibleStash?.InvType != InventoryType.FragmentStash)
        {
            LogDebug($"EnsureFragmentStashScarabTabSelectedAsync skipped. {DescribeStash(stash)}");
            _lastAutomationFragmentScarabTabIndex = -1;
            return;
        }

        if (stash.IndexVisibleStash == _lastAutomationFragmentScarabTabIndex)
        {
            LogDebug($"EnsureFragmentStashScarabTabSelectedAsync skipping because stash tab {stash.IndexVisibleStash} already selected scarab tab previously.");
            return;
        }

        LogDebug($"Ensuring fragment scarab tab using path {DescribePath(FragmentStashScarabTabPath)}. {DescribeStash(stash)}");
        LogDebug($"Fragment stash path trace: {DescribePathLookup(stash, FragmentStashScarabTabPath)}");
        var resolution = await ResolveFragmentScarabTabAsync(tabSwitchDelayMs, timing);
        if (resolution.Aborted)
        {
            return;
        }

        if (resolution.ScarabTab != null)
        {
            stash = GameController?.IngameState?.IngameUi?.StashElement;
            LogDebug($"Fragment scarab tab found on attempt {resolution.Attempts}. {DescribeElement(resolution.ScarabTab)}");
            await ClickElementAsync(
                resolution.ScarabTab,
                timing.UiClickPreDelayMs,
                Math.Max(timing.MinTabClickPostDelayMs, tabSwitchDelayMs));
            _lastAutomationFragmentScarabTabIndex = stash?.IndexVisibleStash ?? -1;
            LogDebug($"Fragment scarab tab clicked. rememberedStashIndex={_lastAutomationFragmentScarabTabIndex}");
            return;
        }

        LogDebug($"EnsureFragmentStashScarabTabSelectedAsync timed out after {resolution.Attempts} attempts. path={DescribePath(FragmentStashScarabTabPath)}, stash={DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
    }

    private async Task<FragmentScarabTabResolution> ResolveFragmentScarabTabAsync(int tabSwitchDelayMs, AutomationTimingValues timing)
    {
        var timeoutMs = Math.Max(timing.FragmentTabBaseTimeoutMs, tabSwitchDelayMs + timing.FragmentTabBaseTimeoutMs);
        var attempts = 0;
        var aborted = false;
        var scarabTab = await PollAutomationValueAsync(
            () =>
            {
                attempts++;
                var currentStash = GameController?.IngameState?.IngameUi?.StashElement;
                if (currentStash?.IsVisible != true || currentStash.VisibleStash?.InvType != InventoryType.FragmentStash)
                {
                    aborted = true;
                    _lastAutomationFragmentScarabTabIndex = -1;
                    return null;
                }

                return TryGetElementByPathQuietly(currentStash, FragmentStashScarabTabPath) ?? FindFragmentScarabTabDynamically(currentStash);
            },
            tab => tab != null || aborted,
            timeoutMs,
            timing.FastPollDelayMs,
            onPendingAsync: _ =>
            {
                if (aborted)
                {
                    LogDebug($"EnsureFragmentStashScarabTabSelectedAsync aborted during polling. {DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
                    return Task.CompletedTask;
                }

                if (attempts == 1 || attempts % 5 == 0)
                {
                    var currentStash = GameController?.IngameState?.IngameUi?.StashElement;
                    LogDebug($"Fragment scarab tab not found on attempt {attempts}. path={DescribePath(FragmentStashScarabTabPath)}, stash={DescribeStash(currentStash)}");
                    LogDebug($"Fragment scarab path trace attempt {attempts}: {DescribePathLookup(currentStash, FragmentStashScarabTabPath)}");
                }

                return Task.CompletedTask;
            });

        return new(scarabTab, attempts, aborted);
    }

    private async Task WaitForVisibleTabAsync(int tabIndex)
    {
        var timing = AutomationTiming;
        var tabSwitchDelayMs = GetConfiguredTabSwitchDelayMs();
        var timeoutMs = Math.Max(
            timing.VisibleTabTimeoutMs,
            Math.Max(1500, tabSwitchDelayMs));
        var tabVisible = await WaitForAutomationConditionAsync(
            () => IsVisibleStashTabReady(tabIndex),
            timeoutMs,
            timing.FastPollDelayMs);
        if (tabVisible)
        {
            return;
        }

        LogDebug($"WaitForVisibleTabAsync timed out. targetTab={tabIndex}, stash={DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
        throw new InvalidOperationException($"Timed out switching to stash tab {tabIndex}.");
    }

    private async Task<int> WaitForVisibleTabIndexChangeAsync(int previousTabIndex, int timeoutMs)
    {
        var changedIndex = await PollAutomationValueAsync(
            () =>
            {
                var stash = GameController?.IngameState?.IngameUi?.StashElement;
                return stash?.IsVisible == true ? stash.IndexVisibleStash : previousTabIndex;
            },
            currentIndex => currentIndex != previousTabIndex,
            timeoutMs,
            AutomationTiming.FastPollDelayMs);
        if (changedIndex != previousTabIndex)
        {
            return changedIndex;
        }

        LogDebug($"WaitForVisibleTabIndexChangeAsync timed out. previousTabIndex={previousTabIndex}, timeoutMs={timeoutMs}, stash={DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
        return previousTabIndex;
    }

    #endregion
    #region Quantity wait helpers

    private int GetQuantitySettleTimeoutMs(int extraBaseDelayMs)
    {
        var timing = AutomationTiming;
        var clickDelayMs = GetConfiguredClickDelayMs();
        var normalizedExtraBaseDelayMs = Math.Max(0, extraBaseDelayMs);

        return GetAutomationTimeoutMs(Math.Max(
            timing.QuantityChangeBaseDelayMs + normalizedExtraBaseDelayMs,
            clickDelayMs + timing.QuantityChangeBaseDelayMs + normalizedExtraBaseDelayMs));
    }

    private int? TryGetVisibleMapStashPageMatchingQuantity(string metadata) =>
        TryGetVisibleMatchingQuantity(GetVisibleMapStashPageItems, metadata, CountMatchingMapStashPageItems);

    private int? TryGetPlayerInventorySlotFillCount(IReadOnlyList<(int X, int Y)> expectedSlots)
    {
        if (expectedSlots == null || expectedSlots.Count <= 0)
        {
            return null;
        }

        return CountOccupiedPlayerInventoryCells(expectedSlots);
    }

    private async Task<int> WaitForObservedQuantityToSettleAsync(int previousQuantity, Func<int?> quantityProvider, int extraBaseDelayMs = 0)
    {
        if (quantityProvider == null)
        {
            return previousQuantity;
        }

        var timing = AutomationTiming;
        var pollDelayMs = timing.FastPollDelayMs;
        var timeoutMs = GetQuantitySettleTimeoutMs(extraBaseDelayMs);
        var stableWindowMs = Math.Max(QuantitySettleStableWindowMs, pollDelayMs * 3);
        return await WaitForQuantityChangeToSettleAsync(previousQuantity, quantityProvider, timeoutMs, pollDelayMs, stableWindowMs);
    }

    #endregion
}

