using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using SharpDX;
using Vector2 = System.Numerics.Vector2;
using BeastsV2.Runtime.Automation;

namespace BeastsV2;

public partial class Main
{
    private sealed record RestockVisibleSource(string Metadata, int AvailableInStash, bool UsesMapStashPageItems);
    private sealed record RestockTransferSnapshot(int AvailableBeforeTransfer, int? InventoryBeforeTransfer, int RemainingRequestedQuantity);
    private sealed record RestockLandingPlan(IReadOnlyList<(int X, int Y)> ExpectedLandingSlots, int ExpectedSlotFillBeforeTransfer);
    private sealed record RestockTransferAttemptResult(int TransferredQuantity, int RemainingAvailable, bool MovedAnyItem);
    private sealed record RestockTransferExecutionResult(int TransferredQuantity, int FinalRemainingAvailable, bool ObservedTransfer);
    private sealed record RestockTransferContext(
        int SlotQuantity,
        int RequestedQuantity,
        int InventoryQuantityBeforeTransfer,
        string SourceMetadata,
        int AvailableInStash,
        int RemainingRequestedQuantity,
        bool UsesMapStashPageItems);
    private sealed record RestockTransferConfirmationRequest(
        string SourceMetadata,
        RestockLandingPlan LandingPlan,
        RestockTransferSnapshot TransferSnapshot,
        Func<Task<int>> WaitForAvailableAfterTransferAsync,
        Func<Task<int?>> WaitForInventoryAfterTransferAsync,
        string StashQuantityLabel,
        string InventoryQuantityLabel,
        string MismatchLabel,
        bool AllowStashFallback,
        int AttemptedTransferQuantity = 0,
        string ShortfallLabel = null,
        int SlotFillExtraDelayMs = 0);

    private async Task<int> RetryRestockTransferAsync(Func<Task<int?>> attemptAsync)
    {
        var movedAmount = await RetryAutomationAsync(
            _ => attemptAsync(),
            result => result.HasValue,
            maxAttempts: 3,
            retryDelayMs: AutomationTiming.FastPollDelayMs);
        return movedAmount.HasValue ? Math.Max(0, movedAmount.Value) : 0;
    }

    private int GetConfiguredTargetVisibleInventoryQuantity(StashAutomationTargetSettings target, out string sourceMetadata)
    {
        sourceMetadata = null;

        var inventoryItems = GetVisiblePlayerInventoryItems();
        var inventorySourceItem = FindInventoryItemForMapDeviceTarget(inventoryItems, target);
        sourceMetadata = inventorySourceItem?.Item?.Metadata;
        return string.IsNullOrWhiteSpace(sourceMetadata)
            ? 0
            : GetVisiblePlayerInventoryMatchingQuantity(sourceMetadata);
    }

    private static int GetMapStashPageItemStackQuantity(Element item)
    {
        return Math.Max(1, item?.Entity?.GetComponent<Stack>()?.Size ?? 1);
    }

    private int GetVisibleRestockSourceQuantity(string sourceMetadata, bool useMapStashPageItems)
    {
        return useMapStashPageItems
            ? GetVisibleMapStashPageMatchingQuantity(sourceMetadata)
            : GetVisibleMatchingItemQuantity(sourceMetadata);
    }

    private static int ReconcileTransferredRestockQuantity(
        int transferred,
        int availableInStash,
        int remainingAvailable,
        bool useMapStashPageItems,
        bool movedThisAttempt)
    {
        if (useMapStashPageItems || (!movedThisAttempt && transferred <= 0))
        {
            return transferred;
        }

        return Math.Max(transferred, Math.Max(0, availableInStash - remainingAvailable));
    }

    private async Task<RestockTransferAttemptResult> ExecuteRestockTransferAttemptAsync(
        StashAutomationTargetSettings target,
        string label,
        string sourceMetadata,
        int requestedQuantity,
        int inventoryQuantityBeforeTransfer,
        bool useMapStashPageItems,
        int currentTransferred,
        int transferGoal,
        int retryAttempt,
        int clickDelayMs)
    {
        var transferredThisAttempt = 0;
        var movedThisAttempt = false;

        while (currentTransferred + transferredThisAttempt < transferGoal)
        {
            var movedAmount = await TryTransferNextMatchingItemAsync(target, sourceMetadata, requestedQuantity, useMapStashPageItems);
            if (movedAmount <= 0)
            {
                LogDebug($"Target '{label}' found no transferable item on attempt {retryAttempt + 1}. transferred={currentTransferred + transferredThisAttempt}, goal={transferGoal}");
                break;
            }

            movedThisAttempt = true;
            transferredThisAttempt += movedAmount;
            var totalTransferred = currentTransferred + transferredThisAttempt;
            LogDebug($"Target '{label}' transferred {movedAmount}. totalTransferred={totalTransferred}, requested={requestedQuantity}");
            UpdateRestockLoadingStatus(label, Math.Min(inventoryQuantityBeforeTransfer + totalTransferred, requestedQuantity), requestedQuantity);
            await DelayAutomationAsync(clickDelayMs);
        }

        return new(
            transferredThisAttempt,
            GetVisibleRestockSourceQuantity(sourceMetadata, useMapStashPageItems),
            movedThisAttempt);
    }

    private async Task<RestockTransferExecutionResult> ExecuteRestockTransferBatchesAsync(
        StashAutomationSettings automation,
        StashAutomationTargetSettings target,
        string label,
        string sourceMetadata,
        int requestedQuantity,
        int inventoryQuantityBeforeTransfer,
        int availableInStash,
        bool useMapStashPageItems,
        int transferGoal)
    {
        var execution = await RestockTransferBatches.ExecuteAsync(
            automation,
            target,
            label,
            sourceMetadata,
            requestedQuantity,
            inventoryQuantityBeforeTransfer,
            availableInStash,
            useMapStashPageItems,
            transferGoal);
        return new(execution.TransferredQuantity, execution.FinalRemainingAvailable, execution.ObservedTransfer);
    }

    private RestockTransferSnapshot CaptureRestockTransferSnapshot(
        string sourceMetadata,
        int requestedQuantity,
        Func<string, int> getAvailableQuantity)
    {
        var availableBeforeTransfer = getAvailableQuantity(sourceMetadata);
        var inventoryBeforeTransfer = TryGetVisiblePlayerInventoryMatchingQuantity(sourceMetadata);
        return new RestockTransferSnapshot(
            availableBeforeTransfer,
            inventoryBeforeTransfer,
            Math.Max(0, requestedQuantity - (inventoryBeforeTransfer ?? 0)));
    }

    private async Task ExecuteMapStashBatchTransferPlanAsync(RestockMapStashBatchTransferPlan transferPlan)
    {
        var timing = AutomationTiming;
        foreach (var batchTarget in transferPlan.Targets)
        {
            ThrowIfAutomationStopRequested();
            await ClickAtAsync(
                batchTarget.Position,
                holdCtrl: true,
                preClickDelayMs: timing.CtrlClickPreDelayMs,
                postClickDelayMs: timing.CtrlClickPostDelayMs);
        }
    }

    private async Task ExecuteVisibleStashTransferPlanAsync(
        NormalInventoryItem nextItem,
        string sourceMetadata,
        RestockTransferSnapshotPlan transferSnapshot,
        RestockVisibleStashTransferPlanData transferPlan)
    {
        if (transferPlan.UseBulkCtrlRightClick)
        {
            LogDebug($"Using ctrl-right-click bulk transfer for metadata='{sourceMetadata}'. available={transferSnapshot.AvailableBeforeTransfer}, remainingRequested={transferSnapshot.RemainingRequestedQuantity}, matchingStacks={transferPlan.MatchingStackCount}");
            await CtrlRightClickInventoryItemAsync(nextItem);
            return;
        }

        if (transferPlan.UsePartialShiftClick)
        {
            LogDebug($"Using shift-click partial transfer for metadata='{sourceMetadata}'. available={transferSnapshot.AvailableBeforeTransfer}, remainingRequested={transferSnapshot.RemainingRequestedQuantity}, fullStackSize={transferPlan.KnownFullStackSize.Value}, targetSlot={DescribePlayerInventoryCells(transferPlan.LandingPlan.ExpectedLandingSlots)}");
            await ShiftClickInventoryItemAsync(nextItem);
            await InputCurrencyShiftClickQuantityAsync(transferSnapshot.RemainingRequestedQuantity);
            await DelayForUiCheckAsync(100);

            var targetInventoryCell = transferPlan.LandingPlan.ExpectedLandingSlots.FirstOrDefault();
            if (transferPlan.LandingPlan.ExpectedLandingSlots.Count <= 0)
            {
                throw new InvalidOperationException($"No free inventory slot found for partial transfer of '{sourceMetadata}'.");
            }

            LogDebug($"Placing partial transfer into inventory slot ({targetInventoryCell.X},{targetInventoryCell.Y}). metadata='{sourceMetadata}'");
            await PlaceItemIntoPlayerInventoryCellAsync(targetInventoryCell.X, targetInventoryCell.Y);
            return;
        }

        await CtrlClickInventoryItemAsync(nextItem);
    }

    private async Task<int?> ConfirmAndResolveRestockTransferAsync(RestockTransferConfirmationRequest request)
    {
        return await RestockTransferConfirmation.ConfirmAndResolveAsync(
            request.SourceMetadata,
            request.LandingPlan.ExpectedLandingSlots,
            request.LandingPlan.ExpectedSlotFillBeforeTransfer,
            request.TransferSnapshot.AvailableBeforeTransfer,
            request.TransferSnapshot.InventoryBeforeTransfer,
            request.WaitForAvailableAfterTransferAsync,
            request.WaitForInventoryAfterTransferAsync,
            request.StashQuantityLabel,
            request.InventoryQuantityLabel,
            request.MismatchLabel,
            request.AllowStashFallback,
            request.AttemptedTransferQuantity,
            request.ShortfallLabel,
            request.SlotFillExtraDelayMs);
    }

    private async Task<int?> WaitForVisibleInventoryQuantityToSettleAsync(string sourceMetadata, int? inventoryBeforeTransfer, int extraDelayMs = 0)
    {
        if (!inventoryBeforeTransfer.HasValue)
        {
            return null;
        }

        return await WaitForObservedQuantityToSettleAsync(
            inventoryBeforeTransfer.Value,
            () => TryGetVisiblePlayerInventoryMatchingQuantity(sourceMetadata),
            extraDelayMs);
    }

    private bool TrySkipRestockTargetWhenAlreadyStocked(
        string label,
        int inventoryQuantity,
        int requestedQuantity,
        int slotQuantity,
        string sourceMetadata,
        string skipReason)
    {
        if (inventoryQuantity < requestedQuantity)
        {
            return false;
        }

        UpdateAutomationStatus($"{label} already stocked: {inventoryQuantity}/{requestedQuantity}");
        LogDebug(
            $"Target '{label}' skipped {skipReason}. " +
            $"inventoryBefore={inventoryQuantity}, slotQuantity={slotQuantity}, requested={requestedQuantity}, metadata='{sourceMetadata}'.");
        return true;
    }

    private bool TrySkipConfiguredRestockTargetWhenAlreadyStocked(
        string label,
        StashAutomationTargetSettings target,
        int requestedQuantity,
        int slotQuantity,
        string skipReason,
        out int inventoryQuantity,
        out string inventorySourceMetadata)
    {
        inventoryQuantity = GetConfiguredTargetVisibleInventoryQuantity(target, out inventorySourceMetadata);
        return TrySkipRestockTargetWhenAlreadyStocked(
            label,
            inventoryQuantity,
            requestedQuantity,
            slotQuantity,
            inventorySourceMetadata,
            skipReason);
    }

    private void UpdateRestockLoadingStatus(string label, int currentQuantity, int requestedQuantity)
    {
        UpdateAutomationStatus($"Loading {label}: {currentQuantity}/{requestedQuantity}");
    }

    #region Restock automation flow

    private async Task EnsureSpecialStashSubTabSelectedAsync(StashAutomationTargetSettings target)
    {
        await EnsureMapStashTierTabSelectedAsync(target);
        await EnsureFragmentStashScarabTabSelectedAsync();
    }

    private async Task<int> RestockConfiguredTargetAsync(
        StashAutomationSettings automation,
        string label,
        string idSuffix,
        StashAutomationTargetSettings target)
    {
        var transferContext = await PrepareRestockTransferContextAsync(label, idSuffix, target, automation);
        if (transferContext == null)
        {
            return 0;
        }

        UpdateRestockLoadingStatus(label, transferContext.InventoryQuantityBeforeTransfer, transferContext.RequestedQuantity);

        var transferGoal = transferContext.UsesMapStashPageItems
            ? transferContext.RemainingRequestedQuantity
            : Math.Min(transferContext.RemainingRequestedQuantity, transferContext.AvailableInStash);
        var transferExecution = await ExecuteRestockTransferBatchesAsync(
            automation,
            target,
            label,
            transferContext.SourceMetadata,
            transferContext.RequestedQuantity,
            transferContext.InventoryQuantityBeforeTransfer,
            transferContext.AvailableInStash,
            transferContext.UsesMapStashPageItems,
            transferGoal);
        var transferred = transferExecution.TransferredQuantity;
        var finalRemainingAvailable = transferExecution.FinalRemainingAvailable;
        var inventoryTransferred = Math.Max(0, GetVisiblePlayerInventoryMatchingQuantity(transferContext.SourceMetadata) - transferContext.InventoryQuantityBeforeTransfer);
        if (transferContext.UsesMapStashPageItems && transferExecution.ObservedTransfer && inventoryTransferred > transferred)
        {
            LogDebug($"Target '{label}' reconciled transferred count from inventory delta. previousTransferred={transferred}, inventoryDelta={inventoryTransferred}");
            transferred = inventoryTransferred;
        }

        if (transferred > 0 && finalRemainingAvailable > 0 && transferred < transferGoal)
        {
            throw new InvalidOperationException(
                $"{label} transfer stalled after {transferContext.InventoryQuantityBeforeTransfer + transferred}/{transferContext.RequestedQuantity}. {finalRemainingAvailable} still remain in stash.");
        }

        var finalInventoryQuantity = transferContext.InventoryQuantityBeforeTransfer + transferred;
        if (transferContext.UsesMapStashPageItems && finalInventoryQuantity < transferContext.RequestedQuantity && finalRemainingAvailable <= 0)
        {
            throw new InvalidOperationException(BuildMapStashInsufficientItemsMessage(label, finalInventoryQuantity, transferContext.RequestedQuantity));
        }

        if (transferred > 0 && finalInventoryQuantity < transferContext.RequestedQuantity && finalRemainingAvailable <= 0)
        {
            UpdateAutomationStatus($"Loaded {label}: {finalInventoryQuantity}/{transferContext.RequestedQuantity}. No more matching items found.");
        }

        if (transferred <= 0)
        {
            throw new InvalidOperationException($"No {label} were transferred.");
        }

        return transferred;
    }

    private static string BuildMapStashInsufficientItemsMessage(string label, int loadedQuantity, int requestedQuantity)
    {
        return $"Not enough {label} in map stash. Loaded {loadedQuantity}/{requestedQuantity} and no more matching items were found.";
    }

    #endregion
    #region Restock preparation and source resolution

    private async Task PrepareConfiguredTargetAsync(StashAutomationSettings automation, StashAutomationTargetSettings target, int tabIndex)
    {
        LogDebug($"Preparing target. {DescribeTarget(target)}, requestedTabIndex={tabIndex}");
        await SelectStashTabAsync(tabIndex);
        await WaitForTargetStashReadyAsync(target, tabIndex);
        LogDebug($"After SelectStashTabAsync: {DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
        await EnsureSpecialStashSubTabSelectedAsync(target);
        LogDebug($"After EnsureSpecialStashSubTabSelectedAsync: {DescribeStash(GameController?.IngameState?.IngameUi?.StashElement)}");
        await DelayAutomationAsync(GetConfiguredTabSwitchDelayMs());
        await PrepareConfiguredMapStashTargetAsync(target);
    }

    private async Task<RestockTransferContext> PrepareRestockTransferContextAsync(
        string label,
        string idSuffix,
        StashAutomationTargetSettings target,
        StashAutomationSettings automation)
    {
        var slotQuantity = GetConfiguredTargetQuantity(target);
        var requestedQuantity = Math.Max(slotQuantity, GetCumulativeConfiguredTargetQuantity(automation, idSuffix, target));
        LogDebug($"Restock target '{label}' starting. {DescribeTarget(target)}");
        if (!target.Enabled.Value || requestedQuantity <= 0)
        {
            LogDebug($"Restock target '{label}' skipped. enabled={target.Enabled.Value}, requestedQuantity={requestedQuantity}");
            return null;
        }

        if (TrySkipConfiguredRestockTargetWhenAlreadyStocked(
            label,
            target,
            requestedQuantity,
            slotQuantity,
            "before stash navigation because inventory already satisfies the cumulative requested total",
            out _,
            out _))
        {
            return null;
        }

        UpdateAutomationStatus($"Loading {label}...");

        var tabIndex = ResolveConfiguredTabIndex(target);
        LogDebug($"Target '{label}' resolved stash tab index {tabIndex} for tab '{target.SelectedTabName.Value}'.");
        await PrepareConfiguredTargetAsync(automation, target, tabIndex);

        var visibleItems = GetVisibleStashItems();
        if (visibleItems == null)
        {
            throw new InvalidOperationException($"No visible stash items found for {label}.");
        }

        if (TrySkipConfiguredRestockTargetWhenAlreadyStocked(
            label,
            target,
            requestedQuantity,
            slotQuantity,
            "because inventory already satisfies the cumulative requested total before stash source resolution",
            out var inventoryQuantityBeforeTransfer,
            out _))
        {
            return null;
        }

        return ResolvePreparedRestockTransferContext(
            label,
            target,
            visibleItems,
            slotQuantity,
            requestedQuantity,
            inventoryQuantityBeforeTransfer);
    }

    private RestockTransferContext ResolvePreparedRestockTransferContext(
        string label,
        StashAutomationTargetSettings target,
        IList<NormalInventoryItem> visibleItems,
        int slotQuantity,
        int requestedQuantity,
        int inventoryQuantityBeforeTransfer)
    {
        var source = ResolveVisibleRestockSource(label, target, visibleItems);
        var sourceMetadata = source.Metadata;
        var availableInStash = source.AvailableInStash;
        inventoryQuantityBeforeTransfer = GetVisiblePlayerInventoryMatchingQuantity(sourceMetadata);
        var remainingRequestedQuantity = Math.Max(0, requestedQuantity - inventoryQuantityBeforeTransfer);

        LogDebug($"Target '{label}' source resolved. useMapStashPageItems={source.UsesMapStashPageItems}, configuredName='{target.ItemName.Value?.Trim()}', metadata='{sourceMetadata}', available={availableInStash}, visibleItems={visibleItems.Count}, inventoryBefore={inventoryQuantityBeforeTransfer}, slotQuantity={slotQuantity}, cumulativeRequested={requestedQuantity}, remainingRequested={remainingRequestedQuantity}");

        if (string.IsNullOrWhiteSpace(sourceMetadata))
        {
            throw new InvalidOperationException($"Source item metadata is unavailable for {label}.");
        }

        if (TrySkipRestockTargetWhenAlreadyStocked(
            label,
            inventoryQuantityBeforeTransfer,
            requestedQuantity,
            slotQuantity,
            sourceMetadata,
            "because inventory already satisfies the cumulative requested total"))
        {
            return null;
        }

        if (availableInStash <= 0)
        {
            throw new InvalidOperationException($"No {label} were found in the visible stash tab.");
        }

        return new(
            slotQuantity,
            requestedQuantity,
            inventoryQuantityBeforeTransfer,
            sourceMetadata,
            availableInStash,
            remainingRequestedQuantity,
            source.UsesMapStashPageItems);
    }

    private async Task PrepareConfiguredMapStashTargetAsync(StashAutomationTargetSettings target)
    {
        if (!IsMapStashTarget(target))
        {
            return;
        }

        await EnsureMapStashPageSelectedAsync(target, 1);
        if (Settings?.StashAutomation?.EnableMapRegexFilter?.Value == true)
        {
            var regex = Settings?.StashAutomation?.MapRegexPattern?.Value?.Trim();
            await ApplyMapStashSearchRegexAsync(!string.IsNullOrWhiteSpace(regex)
                ? regex
                : throw new InvalidOperationException("Map regex filter is enabled, but Map Regex Pattern is empty."));
        }

        await EnsureMapStashPageWithItemSelectedAsync(target);
    }

    private RestockVisibleSource ResolveVisibleRestockSource(
        string label,
        StashAutomationTargetSettings target,
        IList<NormalInventoryItem> visibleItems)
    {
        return IsMapStashTarget(target)
            ? ResolveVisibleMapStashSource(label, target)
            : ResolveVisibleStashSource(label, target, visibleItems);
    }

    private RestockVisibleSource ResolveVisibleMapStashSource(string label, StashAutomationTargetSettings target)
    {
        var configuredItemName = target?.ItemName.Value?.Trim();
        var visiblePageItems = GetVisibleMapStashPageItems();
        var sourcePageItem = FindMapStashPageItemByName(visiblePageItems, configuredItemName);
        if (sourcePageItem?.Entity == null)
        {
            throw new InvalidOperationException(Settings?.StashAutomation?.EnableMapRegexFilter?.Value == true
                ? $"No highlighted source item found for {label}. Check the configured map regex and current map-stash search results."
                : $"No source item found for {label}.");
        }

        return new RestockVisibleSource(
            sourcePageItem.Entity.Metadata,
            CountMatchingMapStashPageItems(visiblePageItems, sourcePageItem.Entity.Metadata),
            UsesMapStashPageItems: true);
    }

    private static RestockVisibleSource ResolveVisibleStashSource(
        string label,
        StashAutomationTargetSettings target,
        IList<NormalInventoryItem> visibleItems)
    {
        var configuredItemName = target?.ItemName.Value?.Trim();
        var sourceItem = FindStashItemByName(visibleItems, configuredItemName);
        if (sourceItem?.Item == null)
        {
            throw new InvalidOperationException($"No source item found for {label}.");
        }

        return new RestockVisibleSource(
            sourceItem.Item.Metadata,
            CountMatchingItemQuantity(visibleItems, sourceItem.Item.Metadata),
            UsesMapStashPageItems: false);
    }

    private async Task<int> TryTransferNextMatchingItemAsync(StashAutomationTargetSettings target, string sourceMetadata, int requestedQuantity, bool useMapStashPageItems)
    {
        ThrowIfAutomationStopRequested();
        return useMapStashPageItems
            ? await TryTransferNextMatchingMapStashItemAsync(target, sourceMetadata, requestedQuantity)
            : await TryTransferNextMatchingVisibleStashItemAsync(target, sourceMetadata, requestedQuantity);
    }

    private async Task<int> TryTransferNextMatchingMapStashItemAsync(StashAutomationTargetSettings target, string sourceMetadata, int requestedQuantity)
    {
        return await RetryRestockTransferAsync(async () =>
        {
            var executionPlan = await TryPrepareMapStashTransferExecutionPlanAsync(target, sourceMetadata, requestedQuantity);
            if (executionPlan == null)
            {
                return 0;
            }

            LogMapStashTransferExecutionPlan(sourceMetadata, executionPlan.TransferPlan);
            await ExecuteMapStashBatchTransferPlanAsync(executionPlan.TransferPlan);

            var movedAmount = await ConfirmAndResolveRestockTransferAsync(BuildMapStashTransferConfirmationRequest(sourceMetadata, executionPlan));
            if (movedAmount.HasValue)
            {
                return movedAmount.Value;
            }

            return null;
        });
    }

    private async Task<int> TryTransferNextMatchingVisibleStashItemAsync(StashAutomationTargetSettings target, string sourceMetadata, int requestedQuantity)
    {
        return await RetryRestockTransferAsync(async () =>
        {
            var executionPlan = TryPrepareVisibleStashTransferExecutionPlan(sourceMetadata, requestedQuantity);
            if (executionPlan == null)
            {
                return 0;
            }

            LogVisibleStashTransferExecutionPlan(sourceMetadata, executionPlan.TransferPlan);
            await ExecuteVisibleStashTransferPlanAsync(sourceMetadata, executionPlan);

            var movedAmount = await ConfirmAndResolveRestockTransferAsync(BuildVisibleStashTransferConfirmationRequest(sourceMetadata, executionPlan));
            if (movedAmount.HasValue)
            {
                return movedAmount.Value;
            }

            return null;
        });
    }

    private Task<RestockMapStashExecutionPlan> TryPrepareMapStashTransferExecutionPlanAsync(
        StashAutomationTargetSettings target,
        string sourceMetadata,
        int requestedQuantity) =>
        RestockTransferPlanning.TryPrepareMapStashExecutionPlanAsync(target, sourceMetadata, requestedQuantity);

    private RestockTransferConfirmationRequest BuildMapStashTransferConfirmationRequest(
        string sourceMetadata,
        RestockMapStashExecutionPlan executionPlan)
    {
        return new(
            sourceMetadata,
            new RestockLandingPlan(executionPlan.TransferPlan.LandingPlan.ExpectedLandingSlots, executionPlan.TransferPlan.LandingPlan.ExpectedSlotFillBeforeTransfer),
            new RestockTransferSnapshot(executionPlan.TransferSnapshot.AvailableBeforeTransfer, executionPlan.TransferSnapshot.InventoryBeforeTransfer, executionPlan.TransferSnapshot.RemainingRequestedQuantity),
            () => WaitForObservedQuantityToSettleAsync(executionPlan.TransferSnapshot.AvailableBeforeTransfer, () => TryGetVisibleMapStashPageMatchingQuantity(sourceMetadata), MapTransferExtraConfirmationDelayMs),
            () => WaitForVisibleInventoryQuantityToSettleAsync(sourceMetadata, executionPlan.TransferSnapshot.InventoryBeforeTransfer, MapTransferExtraConfirmationDelayMs),
            "map stash batch",
            "inventory batch",
            "Map stash transfer quantity mismatch",
            executionPlan.TransferPlan.AttemptedTransferQuantity <= 1,
            executionPlan.TransferPlan.AttemptedTransferQuantity,
            "Map stash batch transfer shortfall",
            MapTransferExtraConfirmationDelayMs);
    }

    private void LogMapStashTransferExecutionPlan(string sourceMetadata, RestockMapStashBatchTransferPlan transferPlan) =>
        RestockTransferPlanning.LogMapStashExecutionPlan(sourceMetadata, transferPlan);

    private RestockVisibleStashExecutionPlan TryPrepareVisibleStashTransferExecutionPlan(string sourceMetadata, int requestedQuantity) =>
        RestockTransferPlanning.TryPrepareVisibleStashExecutionPlan(sourceMetadata, requestedQuantity);

    private RestockTransferConfirmationRequest BuildVisibleStashTransferConfirmationRequest(
        string sourceMetadata,
        RestockVisibleStashExecutionPlan executionPlan)
    {
        return new(
            sourceMetadata,
            new RestockLandingPlan(executionPlan.TransferPlan.LandingPlan.ExpectedLandingSlots, executionPlan.TransferPlan.LandingPlan.ExpectedSlotFillBeforeTransfer),
            new RestockTransferSnapshot(executionPlan.TransferSnapshot.AvailableBeforeTransfer, executionPlan.TransferSnapshot.InventoryBeforeTransfer, executionPlan.TransferSnapshot.RemainingRequestedQuantity),
            () => WaitForObservedQuantityToSettleAsync(executionPlan.TransferSnapshot.AvailableBeforeTransfer, () => TryGetVisibleStashMatchingQuantity(sourceMetadata)),
            () => WaitForVisibleInventoryQuantityToSettleAsync(sourceMetadata, executionPlan.TransferSnapshot.InventoryBeforeTransfer),
            "stash",
            "inventory",
            "Stash transfer quantity mismatch",
            executionPlan.TransferPlan.UseBulkCtrlRightClick || executionPlan.TransferPlan.UsePartialShiftClick || executionPlan.TransferPlan.StackSizeBeforeTransfer <= 1);
    }

    private void LogVisibleStashTransferExecutionPlan(string sourceMetadata, RestockVisibleStashTransferPlanData transferPlan) =>
        RestockTransferPlanning.LogVisibleStashExecutionPlan(sourceMetadata, transferPlan);

    private async Task ExecuteVisibleStashTransferPlanAsync(string sourceMetadata, RestockVisibleStashExecutionPlan executionPlan)
    {
        await ExecuteVisibleStashTransferPlanAsync(
            executionPlan.NextItem,
            sourceMetadata,
            executionPlan.TransferSnapshot,
            executionPlan.TransferPlan);
    }
    #endregion
}

