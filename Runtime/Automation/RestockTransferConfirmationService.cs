using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BeastsV2.Runtime.Automation;

internal sealed record RestockTransferConfirmationCallbacks(
    Action<string> LogDebug,
    Func<IReadOnlyList<(int X, int Y)>, string> DescribePlayerInventoryCells,
    Func<int, Func<int?>, int, Task<int>> WaitForObservedQuantityToSettleAsync,
    Func<IReadOnlyList<(int X, int Y)>, int?> TryGetPlayerInventorySlotFillCount,
    Func<Task> DelayForUiCheckAsync,
    Func<string, int?> TryGetVisiblePlayerInventoryMatchingQuantity);

internal sealed class RestockTransferConfirmationService
{
    private readonly RestockTransferConfirmationCallbacks _callbacks;

    private sealed record TransferQuantityConfirmation(int AvailableAfterTransfer, int? InventoryAfterTransfer);
    private sealed record TransferQuantityDelta(int StashMovedAmount, int InventoryMovedAmount);

    public RestockTransferConfirmationService(RestockTransferConfirmationCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task<int?> ConfirmAndResolveAsync(
        string sourceMetadata,
        IReadOnlyList<(int X, int Y)> expectedLandingSlots,
        int expectedSlotFillBeforeTransfer,
        int availableBeforeTransfer,
        int? inventoryBeforeTransfer,
        Func<Task<int>> waitForAvailableAfterTransferAsync,
        Func<Task<int?>> waitForInventoryAfterTransferAsync,
        string stashQuantityLabel,
        string inventoryQuantityLabel,
        string mismatchLabel,
        bool allowStashFallback,
        int attemptedTransferQuantity = 0,
        string shortfallLabel = null,
        int slotFillExtraDelayMs = 0)
    {
        var (stashMovedAmount, inventoryMovedAmount) = await ConfirmTransferAsync(
            sourceMetadata,
            expectedLandingSlots,
            expectedSlotFillBeforeTransfer,
            availableBeforeTransfer,
            inventoryBeforeTransfer,
            waitForAvailableAfterTransferAsync,
            waitForInventoryAfterTransferAsync,
            stashQuantityLabel,
            inventoryQuantityLabel,
            mismatchLabel,
            slotFillExtraDelayMs);

        if (!string.IsNullOrWhiteSpace(shortfallLabel) &&
            attemptedTransferQuantity > 0 &&
            (stashMovedAmount > 0 || inventoryMovedAmount > 0) &&
            attemptedTransferQuantity != Math.Max(stashMovedAmount, inventoryMovedAmount))
        {
            _callbacks.LogDebug($"{shortfallLabel}. metadata='{sourceMetadata}', attempted={attemptedTransferQuantity}, stashMoved={stashMovedAmount}, inventoryMoved={inventoryMovedAmount}.");
        }

        var movedAmount = SelectConfirmedMovedAmount(
            stashMovedAmount,
            inventoryMovedAmount,
            inventoryBeforeTransfer.HasValue,
            allowStashFallback);
        return movedAmount > 0 ? movedAmount : null;
    }

    private async Task<(int StashMovedAmount, int InventoryMovedAmount)> ConfirmTransferAsync(
        string sourceMetadata,
        IReadOnlyList<(int X, int Y)> expectedLandingSlots,
        int expectedSlotFillBeforeTransfer,
        int availableBeforeTransfer,
        int? inventoryBeforeTransfer,
        Func<Task<int>> waitForAvailableAfterTransferAsync,
        Func<Task<int?>> waitForInventoryAfterTransferAsync,
        string stashQuantityLabel,
        string inventoryQuantityLabel,
        string mismatchLabel,
        int slotFillExtraDelayMs)
    {
        await ConfirmPredictedLandingSlotsAsync(sourceMetadata, expectedLandingSlots, expectedSlotFillBeforeTransfer, slotFillExtraDelayMs);

        var confirmation = await WaitForTransferQuantityConfirmationAsync(
            sourceMetadata,
            availableBeforeTransfer,
            inventoryBeforeTransfer,
            waitForAvailableAfterTransferAsync,
            waitForInventoryAfterTransferAsync,
            stashQuantityLabel,
            inventoryQuantityLabel);
        var deltas = CalculateTransferQuantityDelta(availableBeforeTransfer, inventoryBeforeTransfer, confirmation);
        var reconciledInventoryMovedAmount = await ReconcileInventoryTransferDeltaAsync(
            sourceMetadata,
            inventoryBeforeTransfer,
            confirmation.InventoryAfterTransfer,
            deltas.StashMovedAmount,
            deltas.InventoryMovedAmount);

        LogTransferQuantityMismatchIfNeeded(sourceMetadata, mismatchLabel, deltas.StashMovedAmount, reconciledInventoryMovedAmount);
        return (deltas.StashMovedAmount, reconciledInventoryMovedAmount);
    }

    private async Task ConfirmPredictedLandingSlotsAsync(
        string sourceMetadata,
        IReadOnlyList<(int X, int Y)> expectedLandingSlots,
        int expectedSlotFillBeforeTransfer,
        int slotFillExtraDelayMs)
    {
        if (expectedLandingSlots.Count <= 0)
        {
            return;
        }

        _callbacks.LogDebug($"Waiting for predicted inventory slot fill confirmation. metadata='{sourceMetadata}', expectedSlots={expectedLandingSlots.Count}, previousFilled={expectedSlotFillBeforeTransfer}, slots={_callbacks.DescribePlayerInventoryCells(expectedLandingSlots)}");
        var expectedSlotFillAfterTransfer = await _callbacks.WaitForObservedQuantityToSettleAsync(expectedSlotFillBeforeTransfer, () => _callbacks.TryGetPlayerInventorySlotFillCount(expectedLandingSlots), slotFillExtraDelayMs);
        _callbacks.LogDebug($"Predicted inventory slot fill confirmation complete. metadata='{sourceMetadata}', expectedSlots={expectedLandingSlots.Count}, previousFilled={expectedSlotFillBeforeTransfer}, currentFilled={expectedSlotFillAfterTransfer}, landed={(expectedSlotFillAfterTransfer > expectedSlotFillBeforeTransfer)}, slots={_callbacks.DescribePlayerInventoryCells(expectedLandingSlots)}");
    }

    private async Task<TransferQuantityConfirmation> WaitForTransferQuantityConfirmationAsync(
        string sourceMetadata,
        int availableBeforeTransfer,
        int? inventoryBeforeTransfer,
        Func<Task<int>> waitForAvailableAfterTransferAsync,
        Func<Task<int?>> waitForInventoryAfterTransferAsync,
        string stashQuantityLabel,
        string inventoryQuantityLabel)
    {
        _callbacks.LogDebug($"Waiting for {stashQuantityLabel} quantity confirmation. metadata='{sourceMetadata}', previousQuantity={availableBeforeTransfer}");
        var availableAfterTransfer = await waitForAvailableAfterTransferAsync();
        _callbacks.LogDebug($"{char.ToUpperInvariant(stashQuantityLabel[0])}{stashQuantityLabel.Substring(1)} quantity confirmation complete. metadata='{sourceMetadata}', previousQuantity={availableBeforeTransfer}, currentQuantity={availableAfterTransfer}");

        _callbacks.LogDebug($"Waiting for {inventoryQuantityLabel} quantity confirmation. metadata='{sourceMetadata}', previousQuantity={(inventoryBeforeTransfer.HasValue ? inventoryBeforeTransfer.Value.ToString() : "null")}");
        var inventoryAfterTransfer = await waitForInventoryAfterTransferAsync();
        _callbacks.LogDebug($"{char.ToUpperInvariant(inventoryQuantityLabel[0])}{inventoryQuantityLabel.Substring(1)} quantity confirmation complete. metadata='{sourceMetadata}', previousQuantity={(inventoryBeforeTransfer.HasValue ? inventoryBeforeTransfer.Value.ToString() : "null")}, currentQuantity={(inventoryAfterTransfer.HasValue ? inventoryAfterTransfer.Value.ToString() : "null")}");

        return new(availableAfterTransfer, inventoryAfterTransfer);
    }

    private static TransferQuantityDelta CalculateTransferQuantityDelta(
        int availableBeforeTransfer,
        int? inventoryBeforeTransfer,
        TransferQuantityConfirmation confirmation)
    {
        return new(
            Math.Max(0, availableBeforeTransfer - confirmation.AvailableAfterTransfer),
            inventoryBeforeTransfer.HasValue && confirmation.InventoryAfterTransfer.HasValue
                ? Math.Max(0, confirmation.InventoryAfterTransfer.Value - inventoryBeforeTransfer.Value)
                : 0);
    }

    private async Task<int> ReconcileInventoryTransferDeltaAsync(
        string sourceMetadata,
        int? inventoryBeforeTransfer,
        int? inventoryAfterTransfer,
        int stashMovedAmount,
        int inventoryMovedAmount)
    {
        if (!inventoryBeforeTransfer.HasValue || inventoryMovedAmount > 0 || stashMovedAmount <= 0)
        {
            return inventoryMovedAmount;
        }

        _callbacks.LogDebug($"Inventory confirmation fallback delay triggered. metadata='{sourceMetadata}', stashMoved={stashMovedAmount}, inventoryBefore={inventoryBeforeTransfer.Value}, inventoryAfter={(inventoryAfterTransfer.HasValue ? inventoryAfterTransfer.Value.ToString() : "null")}");
        await _callbacks.DelayForUiCheckAsync();
        inventoryAfterTransfer = _callbacks.TryGetVisiblePlayerInventoryMatchingQuantity(sourceMetadata);
        var reconciledInventoryMovedAmount = inventoryAfterTransfer.HasValue
            ? Math.Max(0, inventoryAfterTransfer.Value - inventoryBeforeTransfer.Value)
            : 0;
        _callbacks.LogDebug($"Inventory confirmation fallback recheck complete. metadata='{sourceMetadata}', inventoryBefore={inventoryBeforeTransfer.Value}, inventoryAfter={(inventoryAfterTransfer.HasValue ? inventoryAfterTransfer.Value.ToString() : "null")}");
        return reconciledInventoryMovedAmount;
    }

    private void LogTransferQuantityMismatchIfNeeded(
        string sourceMetadata,
        string mismatchLabel,
        int stashMovedAmount,
        int inventoryMovedAmount)
    {
        if (stashMovedAmount <= 0 || inventoryMovedAmount <= 0 || stashMovedAmount == inventoryMovedAmount)
        {
            return;
        }

        _callbacks.LogDebug($"{mismatchLabel}. metadata='{sourceMetadata}', stashMoved={stashMovedAmount}, inventoryMoved={inventoryMovedAmount}. Using inventory delta.");
    }

    private static int SelectConfirmedMovedAmount(
        int stashMovedAmount,
        int inventoryMovedAmount,
        bool hasInventoryBaseline,
        bool allowStashFallback)
    {
        return hasInventoryBaseline
            ? inventoryMovedAmount > 0
                ? inventoryMovedAmount
                : allowStashFallback
                    ? stashMovedAmount
                    : 0
            : stashMovedAmount;
    }
}