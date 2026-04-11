using System;
using System.Threading.Tasks;

namespace BeastsV2.Runtime.Automation;

internal sealed record RestockTransferBatchExecution(int TransferredQuantity, int FinalRemainingAvailable, bool ObservedTransfer);

internal sealed record RestockTransferBatchCallbacks(
    Func<string, int> GetVisibleMapStashPageMatchingQuantity,
    Func<string, int> GetVisibleMatchingItemQuantity,
    Func<StashAutomationTargetSettings, string, int, bool, Task<int>> TryTransferNextMatchingItemAsync,
    Action<string> LogDebug,
    Action<string, int, int> UpdateRestockLoadingStatus,
    Func<StashAutomationTargetSettings, Task> EnsureSpecialStashSubTabSelectedAsync,
    Func<int> GetTabSwitchDelayMs,
    Func<int, Task> DelayAutomationAsync);

internal sealed class RestockTransferBatchService
{
    private readonly RestockTransferBatchCallbacks _callbacks;

    private sealed record TransferAttemptResult(int TransferredQuantity, int RemainingAvailable, bool MovedAnyItem);

    public RestockTransferBatchService(RestockTransferBatchCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task<RestockTransferBatchExecution> ExecuteAsync(
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
        var transferred = 0;
        var observedTransfer = false;
        var finalRemainingAvailable = GetVisibleSourceQuantity(sourceMetadata, useMapStashPageItems);

        for (var retryAttempt = 0; retryAttempt < 3 && transferred < transferGoal; retryAttempt++)
        {
            _callbacks.LogDebug($"Target '{label}' transfer attempt {retryAttempt + 1}/3. transferred={transferred}, goal={transferGoal}");
            var attemptResult = await ExecuteAttemptAsync(
                target,
                label,
                sourceMetadata,
                requestedQuantity,
                inventoryQuantityBeforeTransfer,
                useMapStashPageItems,
                transferred,
                transferGoal,
                retryAttempt);

            observedTransfer |= attemptResult.MovedAnyItem;
            transferred += attemptResult.TransferredQuantity;
            finalRemainingAvailable = attemptResult.RemainingAvailable;
            transferred = ReconcileTransferredQuantity(
                transferred,
                availableInStash,
                finalRemainingAvailable,
                useMapStashPageItems,
                attemptResult.MovedAnyItem);

            if (transferred >= transferGoal || finalRemainingAvailable <= 0)
            {
                break;
            }

            if (!attemptResult.MovedAnyItem)
            {
                _callbacks.LogDebug($"Target '{label}' retrying special stash sub-tab selection. remainingAvailable={finalRemainingAvailable}");
                await _callbacks.EnsureSpecialStashSubTabSelectedAsync(target);
                await _callbacks.DelayAutomationAsync(_callbacks.GetTabSwitchDelayMs());
            }
        }

        return new RestockTransferBatchExecution(transferred, finalRemainingAvailable, observedTransfer);
    }

    private async Task<TransferAttemptResult> ExecuteAttemptAsync(
        StashAutomationTargetSettings target,
        string label,
        string sourceMetadata,
        int requestedQuantity,
        int inventoryQuantityBeforeTransfer,
        bool useMapStashPageItems,
        int currentTransferred,
        int transferGoal,
        int retryAttempt)
    {
        var transferredThisAttempt = 0;
        var movedThisAttempt = false;

        while (currentTransferred + transferredThisAttempt < transferGoal)
        {
            var movedAmount = await _callbacks.TryTransferNextMatchingItemAsync(target, sourceMetadata, requestedQuantity, useMapStashPageItems);
            if (movedAmount <= 0)
            {
                _callbacks.LogDebug($"Target '{label}' found no transferable item on attempt {retryAttempt + 1}. transferred={currentTransferred + transferredThisAttempt}, goal={transferGoal}");
                break;
            }

            movedThisAttempt = true;
            transferredThisAttempt += movedAmount;
            var totalTransferred = currentTransferred + transferredThisAttempt;
            _callbacks.LogDebug($"Target '{label}' transferred {movedAmount}. totalTransferred={totalTransferred}, requested={requestedQuantity}");
            _callbacks.UpdateRestockLoadingStatus(label, Math.Min(inventoryQuantityBeforeTransfer + totalTransferred, requestedQuantity), requestedQuantity);
        }

        return new TransferAttemptResult(
            transferredThisAttempt,
            GetVisibleSourceQuantity(sourceMetadata, useMapStashPageItems),
            movedThisAttempt);
    }

    private int GetVisibleSourceQuantity(string sourceMetadata, bool useMapStashPageItems)
    {
        return useMapStashPageItems
            ? _callbacks.GetVisibleMapStashPageMatchingQuantity(sourceMetadata)
            : _callbacks.GetVisibleMatchingItemQuantity(sourceMetadata);
    }

    private static int ReconcileTransferredQuantity(
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
}