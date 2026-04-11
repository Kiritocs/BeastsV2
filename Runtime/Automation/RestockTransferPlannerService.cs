using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements.InventoryElements;
using SharpDX;

namespace BeastsV2.Runtime.Automation;

internal sealed record RestockTransferSnapshotPlan(int AvailableBeforeTransfer, int? InventoryBeforeTransfer, int RemainingRequestedQuantity);
internal sealed record RestockLandingPlanData(IReadOnlyList<(int X, int Y)> ExpectedLandingSlots, int ExpectedSlotFillBeforeTransfer);
internal sealed record RestockBatchTransferTarget(SharpDX.Vector2 Position, int StackSize);
internal sealed record RestockMapStashBatchTransferPlan(IReadOnlyList<RestockBatchTransferTarget> Targets, int AttemptedTransferQuantity, RestockLandingPlanData LandingPlan);
internal sealed record RestockVisibleStashTransferPlanData(
    bool UseBulkCtrlRightClick,
    bool UsePartialShiftClick,
    int StackSizeBeforeTransfer,
    int MatchingStackCount,
    int? KnownFullStackSize,
    string TransferMode,
    RestockLandingPlanData LandingPlan);
internal sealed record RestockMapStashExecutionPlan(IList<Element> VisiblePageItems, Element NextPageItem, int CurrentVisibleQuantity, RestockTransferSnapshotPlan TransferSnapshot, RestockMapStashBatchTransferPlan TransferPlan);
internal sealed record RestockVisibleStashExecutionPlan(NormalInventoryItem NextItem, RestockTransferSnapshotPlan TransferSnapshot, RestockVisibleStashTransferPlanData TransferPlan);

internal sealed record RestockTransferPlannerCallbacks(
    Func<IList<Element>> GetVisibleMapStashPageItems,
    Func<IList<Element>, string, Element> FindNextMatchingMapStashPageItem,
    Func<IList<Element>, string, int> CountMatchingMapStashPageItems,
    Func<string, Task<Element>> WaitForNextMatchingMapStashPageItemAsync,
    Func<StashAutomationTargetSettings, string, Task<bool>> EnsureMapStashPageWithItemSelectedAsync,
    Func<string, int> GetVisibleMapStashPageMatchingQuantity,
    Func<string, int> GetVisibleMatchingItemQuantity,
    Func<string, int?> TryGetVisiblePlayerInventoryMatchingQuantity,
    Func<IList<NormalInventoryItem>> GetVisibleStashItems,
    Func<NormalInventoryItem, int> GetVisibleInventoryItemStackQuantity,
    Func<IEnumerable<NormalInventoryItem>, string, int?> GetKnownFullStackSize,
    Func<int, List<(int X, int Y)>> GetPlayerInventoryNextFreeCells,
    Func<IReadOnlyList<(int X, int Y)>, int> CountOccupiedPlayerInventoryCells,
    Action<string> LogDebug,
    Func<IReadOnlyList<(int X, int Y)>, string> DescribePlayerInventoryCells);

internal sealed class RestockTransferPlannerService
{
    private readonly RestockTransferPlannerCallbacks _callbacks;

    public RestockTransferPlannerService(RestockTransferPlannerCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task<RestockMapStashExecutionPlan> TryPrepareMapStashExecutionPlanAsync(
        StashAutomationTargetSettings target,
        string sourceMetadata,
        int requestedQuantity)
    {
        var candidate = await ResolveMapStashTransferCandidateAsync(target, sourceMetadata);
        if (candidate.NextPageItem?.Entity == null && candidate.CurrentVisibleQuantity > 0)
        {
            _callbacks.LogDebug($"Visible map stash page still reports {candidate.CurrentVisibleQuantity} matching item(s) for metadata='{sourceMetadata}'. Deferring page scan until current page is empty.");
            return null;
        }

        if (candidate.NextPageItem?.Entity == null)
        {
            return null;
        }

        var transferSnapshot = CaptureTransferSnapshot(sourceMetadata, requestedQuantity, _callbacks.GetVisibleMapStashPageMatchingQuantity);
        if (transferSnapshot.RemainingRequestedQuantity <= 0)
        {
            return null;
        }

        var transferPlan = BuildMapStashBatchTransferPlan(
            candidate.VisiblePageItems,
            candidate.NextPageItem,
            sourceMetadata,
            transferSnapshot.RemainingRequestedQuantity);
        return transferPlan.AttemptedTransferQuantity > 0
            ? new RestockMapStashExecutionPlan(candidate.VisiblePageItems, candidate.NextPageItem, candidate.CurrentVisibleQuantity, transferSnapshot, transferPlan)
            : null;
    }

    public RestockVisibleStashExecutionPlan TryPrepareVisibleStashExecutionPlan(string sourceMetadata, int requestedQuantity)
    {
        var visibleItems = _callbacks.GetVisibleStashItems();
        var matchingVisibleItems = visibleItems?
            .Where(item => item?.Item?.Metadata.EqualsIgnoreCase(sourceMetadata) == true)
            .OrderByScreenPosition(item => item.GetClientRect())
            .ToList();
        var nextItem = matchingVisibleItems?.FirstOrDefault();
        if (nextItem?.Item == null)
        {
            return null;
        }

        var transferSnapshot = CaptureTransferSnapshot(sourceMetadata, requestedQuantity, _callbacks.GetVisibleMatchingItemQuantity);
        var transferPlan = BuildVisibleStashTransferPlan(matchingVisibleItems, nextItem, sourceMetadata, transferSnapshot);
        return new RestockVisibleStashExecutionPlan(nextItem, transferSnapshot, transferPlan);
    }

    public void LogMapStashExecutionPlan(string sourceMetadata, RestockMapStashBatchTransferPlan transferPlan)
    {
        _callbacks.LogDebug($"Predicted inventory landing {SlotLabel(transferPlan.LandingPlan.ExpectedLandingSlots.Count)} for stash transfer. metadata='{sourceMetadata}', mode='ctrl-click-batch', targetCount={transferPlan.Targets.Count}, attemptedQuantity={transferPlan.AttemptedTransferQuantity}, slots={_callbacks.DescribePlayerInventoryCells(transferPlan.LandingPlan.ExpectedLandingSlots)}, occupiedBefore={transferPlan.LandingPlan.ExpectedSlotFillBeforeTransfer}/{transferPlan.LandingPlan.ExpectedLandingSlots.Count}");
        _callbacks.LogDebug($"Batch transferring visible map stash page items. metadata='{sourceMetadata}', remainingRequested={transferPlan.AttemptedTransferQuantity}, targetCount={transferPlan.Targets.Count}, attemptedQuantity={transferPlan.AttemptedTransferQuantity}");
    }

    public void LogVisibleStashExecutionPlan(string sourceMetadata, RestockVisibleStashTransferPlanData transferPlan)
    {
        _callbacks.LogDebug($"Predicted inventory landing {SlotLabel(transferPlan.LandingPlan.ExpectedLandingSlots.Count)} for stash transfer. metadata='{sourceMetadata}', mode='{transferPlan.TransferMode}', matchingStacks={transferPlan.MatchingStackCount}, fullStackSize={(transferPlan.KnownFullStackSize.HasValue ? transferPlan.KnownFullStackSize.Value.ToString() : "null")}, slots={_callbacks.DescribePlayerInventoryCells(transferPlan.LandingPlan.ExpectedLandingSlots)}, occupiedBefore={transferPlan.LandingPlan.ExpectedSlotFillBeforeTransfer}/{transferPlan.LandingPlan.ExpectedLandingSlots.Count}");
    }

    private RestockTransferSnapshotPlan CaptureTransferSnapshot(
        string sourceMetadata,
        int requestedQuantity,
        Func<string, int> getAvailableQuantity)
    {
        var availableBeforeTransfer = getAvailableQuantity(sourceMetadata);
        var inventoryBeforeTransfer = _callbacks.TryGetVisiblePlayerInventoryMatchingQuantity(sourceMetadata);
        return new RestockTransferSnapshotPlan(
            availableBeforeTransfer,
            inventoryBeforeTransfer,
            Math.Max(0, requestedQuantity - (inventoryBeforeTransfer ?? 0)));
    }

    private RestockLandingPlanData CaptureLandingPlan(int expectedLandingSlotCount)
    {
        var expectedLandingSlots = _callbacks.GetPlayerInventoryNextFreeCells(expectedLandingSlotCount);
        return new RestockLandingPlanData(
            expectedLandingSlots,
            _callbacks.CountOccupiedPlayerInventoryCells(expectedLandingSlots));
    }

    private RestockMapStashBatchTransferPlan BuildMapStashBatchTransferPlan(
        IList<Element> visiblePageItems,
        Element nextPageItem,
        string sourceMetadata,
        int remainingRequestedQuantity)
    {
        var targets = (visiblePageItems ?? (nextPageItem?.Entity != null ? [nextPageItem] : []))
            .Where(item => item?.Entity?.Metadata.EqualsIgnoreCase(sourceMetadata) == true)
            .OrderByScreenPosition(item => item.GetClientRect())
            .Take(remainingRequestedQuantity)
            .Select(item => new RestockBatchTransferTarget(item.GetClientRect().Center, GetMapStashPageItemStackQuantity(item)))
            .ToList();

        return new RestockMapStashBatchTransferPlan(
            targets,
            targets.Sum(x => x.StackSize),
            CaptureLandingPlan(targets.Count));
    }

    private RestockVisibleStashTransferPlanData BuildVisibleStashTransferPlan(
        IList<NormalInventoryItem> matchingVisibleItems,
        NormalInventoryItem nextItem,
        string sourceMetadata,
        RestockTransferSnapshotPlan transferSnapshot)
    {
        var stackSizeBeforeTransfer = _callbacks.GetVisibleInventoryItemStackQuantity(nextItem);
        var matchingStackCount = matchingVisibleItems?.Count ?? 0;
        var knownFullStackSize = _callbacks.GetKnownFullStackSize(matchingVisibleItems, sourceMetadata);
        var hasStackableQuantity = stackSizeBeforeTransfer > 1 ||
                                   matchingVisibleItems?.Any(item => _callbacks.GetVisibleInventoryItemStackQuantity(item) > 1) == true ||
                                   transferSnapshot.AvailableBeforeTransfer > matchingStackCount;
        var usePartialShiftClick = knownFullStackSize.HasValue &&
                                   transferSnapshot.RemainingRequestedQuantity > 0 &&
                                   transferSnapshot.RemainingRequestedQuantity < knownFullStackSize.Value &&
                                   transferSnapshot.AvailableBeforeTransfer > transferSnapshot.RemainingRequestedQuantity &&
                                   hasStackableQuantity;
        var useBulkCtrlRightClick = transferSnapshot.AvailableBeforeTransfer > 0 &&
                                    transferSnapshot.AvailableBeforeTransfer <= transferSnapshot.RemainingRequestedQuantity &&
                                    matchingStackCount > 0 &&
                                    hasStackableQuantity;
        var transferMode = useBulkCtrlRightClick ? "ctrl-right-click" : usePartialShiftClick ? "shift-click-partial" : "ctrl-click";

        return new RestockVisibleStashTransferPlanData(
            useBulkCtrlRightClick,
            usePartialShiftClick,
            stackSizeBeforeTransfer,
            matchingStackCount,
            knownFullStackSize,
            transferMode,
            CaptureLandingPlan(useBulkCtrlRightClick ? matchingStackCount : 1));
    }

    private async Task<(IList<Element> VisiblePageItems, Element NextPageItem, int CurrentVisibleQuantity)> ResolveMapStashTransferCandidateAsync(
        StashAutomationTargetSettings target,
        string sourceMetadata)
    {
        var visiblePageItems = _callbacks.GetVisibleMapStashPageItems();
        var nextPageItem = _callbacks.FindNextMatchingMapStashPageItem(visiblePageItems, sourceMetadata);
        var currentVisibleQuantity = _callbacks.CountMatchingMapStashPageItems(visiblePageItems, sourceMetadata);

        if (nextPageItem?.Entity == null)
        {
            if (currentVisibleQuantity > 0)
            {
                nextPageItem = await _callbacks.WaitForNextMatchingMapStashPageItemAsync(sourceMetadata);
            }
            else
            {
                _callbacks.LogDebug($"Current map stash page has no remaining matches for metadata='{sourceMetadata}'. Searching other pages immediately.");
            }

            visiblePageItems = _callbacks.GetVisibleMapStashPageItems();
            currentVisibleQuantity = _callbacks.CountMatchingMapStashPageItems(visiblePageItems, sourceMetadata);
            nextPageItem = _callbacks.FindNextMatchingMapStashPageItem(visiblePageItems, sourceMetadata);
        }

        if (nextPageItem?.Entity == null && currentVisibleQuantity <= 0)
        {
            if (await _callbacks.EnsureMapStashPageWithItemSelectedAsync(target, sourceMetadata))
            {
                nextPageItem = await _callbacks.WaitForNextMatchingMapStashPageItemAsync(sourceMetadata);
            }
        }

        if (nextPageItem?.Entity == null)
        {
            return (visiblePageItems, null, currentVisibleQuantity);
        }

        visiblePageItems = _callbacks.GetVisibleMapStashPageItems();
        nextPageItem = _callbacks.FindNextMatchingMapStashPageItem(visiblePageItems, sourceMetadata) ?? nextPageItem;
        currentVisibleQuantity = _callbacks.CountMatchingMapStashPageItems(visiblePageItems, sourceMetadata);
        return (visiblePageItems, nextPageItem, currentVisibleQuantity);
    }

    private static int GetMapStashPageItemStackQuantity(Element item)
    {
        return Math.Max(1, item?.Entity?.GetComponent<ExileCore.PoEMemory.Components.Stack>()?.Size ?? 1);
    }

    private static string SlotLabel(int count) => count == 1 ? "slot" : "slots";
}