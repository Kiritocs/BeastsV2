using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore.PoEMemory;

namespace BeastsV2.Runtime.Automation;

internal sealed record BestiaryClearCallbacks(
    Func<bool> ShouldDeleteBeasts,
    Action ReleaseAutomationModifierKeys,
    Action HoldCtrlKey,
    Action ReleaseCtrlKey,
    Action ThrowIfAutomationStopRequested,
    Action<string> EnsureCapturedBeastsTabVisible,
    Func<int> GetPlayerInventoryFreeCellCount,
    Func<bool> ShouldAutoStashItemizedBeasts,
    Action<string, bool> UpdateAutomationStatus,
    Action<string> Log,
    Func<Task<int>> StashCapturedMonstersAndReturnToBestiaryAsync,
    Func<IReadOnlyList<Element>> GetVisibleCapturedBeasts,
    Func<Task<bool>> WaitForCapturedBeastsToPopulateAsync,
    Func<int> GetTotalCapturedBeastCount,
    Func<Element, bool, bool> CanClickBestiaryBeast,
    Func<int, Task> DelayAutomationAsync,
    Func<int> GetBestiaryReleaseTimeoutMs,
    Func<int> GetBestiaryReleasePollDelayMs,
    Func<Element, bool, Task> ClickBestiaryBeastAsync,
    Func<int, Task<int>> WaitForBestiaryReleaseVisibleCountAsync);

internal sealed class BestiaryClearService
{
    private readonly BestiaryClearCallbacks _callbacks;

    private sealed record ClearProgress(int ReleasedBeastCount, int ConsecutiveFailures, DateTime? FirstItemizedBeastAtUtc, bool FirstReleaseTimingLogged);
    private sealed record InventoryCapacityResult(ClearProgress Progress, bool ShouldStop);
    private sealed record ItemizeBatchResult(
        int CurrentDisplayedCount,
        int CurrentFreeCellCount,
        int ReleasedBeastCount,
        int DisplayedReleasedCount,
        long? FirstInventoryChangeMs,
        long? LastInventoryChangeMs,
        long? FirstDisplayedChangeMs,
        long? LastDisplayedChangeMs);
    private sealed record DeleteBatchResult(int CurrentDisplayedCount, int ReleasedBeastCount, long? FirstDisplayedChangeMs, long? LastDisplayedChangeMs);

    public BestiaryClearService(BestiaryClearCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task<int> ClearAsync()
    {
        var progress = new ClearProgress(0, 0, null, false);
        var deleteBeasts = _callbacks.ShouldDeleteBeasts();

        _callbacks.ReleaseAutomationModifierKeys();

        var holdCtrlForBestiaryClicks = !deleteBeasts;
        if (holdCtrlForBestiaryClicks)
        {
            _callbacks.HoldCtrlKey();
        }

        try
        {
            while (true)
            {
                _callbacks.ThrowIfAutomationStopRequested();
                _callbacks.EnsureCapturedBeastsTabVisible("clearing captured beasts");

                var progressBeforeCapacityCheck = progress;
                var inventoryCapacity = await EnsureInventoryCapacityAsync(deleteBeasts, holdCtrlForBestiaryClicks, progress);
                progress = inventoryCapacity.Progress;
                if (inventoryCapacity.ShouldStop)
                {
                    return progress.ReleasedBeastCount;
                }

                if (!Equals(progressBeforeCapacityCheck, progress) || deleteBeasts)
                {
                    // Inventory changes or delete-mode clicks can invalidate the current row set.
                }

                var visibleBeastBatch = await GetCapturedBeastsForClearAsync();
                if (visibleBeastBatch.Count <= 0)
                {
                    return progress.ReleasedBeastCount;
                }

                progress = await ReleaseVisibleCapturedBeastBatchAsync(
                    visibleBeastBatch,
                    deleteBeasts,
                    holdCtrlForBestiaryClicks,
                    progress);
            }
        }
        finally
        {
            if (holdCtrlForBestiaryClicks)
            {
                _callbacks.ReleaseCtrlKey();
            }
        }
    }

    private async Task<InventoryCapacityResult> EnsureInventoryCapacityAsync(
        bool deleteBeasts,
        bool holdCtrlForBestiaryClicks,
        ClearProgress progress)
    {
        if (deleteBeasts || _callbacks.GetPlayerInventoryFreeCellCount() > 0)
        {
            return new(progress, false);
        }

        progress = LogInventoryFilledAfterItemizing(progress);
        if (!_callbacks.ShouldAutoStashItemizedBeasts())
        {
            _callbacks.UpdateAutomationStatus("Bestiary clear stopped. Inventory is full and regex itemize auto-stash is disabled.", true);
            return new(progress, true);
        }

        await _callbacks.StashCapturedMonstersAndReturnToBestiaryAsync();
        if (holdCtrlForBestiaryClicks)
        {
            _callbacks.HoldCtrlKey();
        }

        if (_callbacks.GetPlayerInventoryFreeCellCount() <= 0)
        {
            throw new InvalidOperationException("Inventory is full and itemized beasts could not be moved to stash.");
        }

        return new(progress, false);
    }

    private ClearProgress LogInventoryFilledAfterItemizing(ClearProgress progress)
    {
        if (!progress.FirstItemizedBeastAtUtc.HasValue)
        {
            return progress;
        }

        var elapsed = DateTime.UtcNow - progress.FirstItemizedBeastAtUtc.Value;
        _callbacks.Log($"Inventory filled after {BeastsV2Helpers.FormatDuration(elapsed)} ({elapsed.TotalSeconds:F1}s) from the first itemized beast.");
        return progress with { FirstItemizedBeastAtUtc = null };
    }

    private async Task<IReadOnlyList<Element>> GetCapturedBeastsForClearAsync()
    {
        var visibleBeasts = _callbacks.GetVisibleCapturedBeasts();
        if (visibleBeasts.Count > 0)
        {
            return visibleBeasts;
        }

        if (!await _callbacks.WaitForCapturedBeastsToPopulateAsync())
        {
            return visibleBeasts;
        }

        return _callbacks.GetVisibleCapturedBeasts();
    }

    private async Task<ClearProgress> ReleaseVisibleCapturedBeastBatchAsync(
        IReadOnlyList<Element> visibleBeastBatch,
        bool deleteBeasts,
        bool holdCtrlForBestiaryClicks,
        ClearProgress progress)
    {
        var startingDisplayedCount = _callbacks.GetTotalCapturedBeastCount();
        var startingFreeCellCount = deleteBeasts ? 0 : _callbacks.GetPlayerInventoryFreeCellCount();
        var targetBeasts = visibleBeastBatch
            .Where(element => element?.IsVisible == true && _callbacks.CanClickBestiaryBeast(element, deleteBeasts))
            .Take(deleteBeasts ? visibleBeastBatch.Count : Math.Min(startingFreeCellCount, visibleBeastBatch.Count))
            .ToList();

        if (targetBeasts.Count <= 0)
        {
            return ResolveReleaseProgress(progress, deleteBeasts, releasedCount: 0);
        }

        if (holdCtrlForBestiaryClicks)
        {
            _callbacks.HoldCtrlKey();
        }

        var clickStopwatch = Stopwatch.StartNew();
        var attemptedClickCount = 0;
        foreach (var beastElement in targetBeasts)
        {
            _callbacks.ThrowIfAutomationStopRequested();

            if (beastElement?.IsVisible != true || !_callbacks.CanClickBestiaryBeast(beastElement, deleteBeasts))
            {
                _callbacks.Log(
                    $"Bestiary batch click target invalidated before click. deleteMode={deleteBeasts}, batchIndex={attemptedClickCount + 1}/{targetBeasts.Count}");
                continue;
            }

            attemptedClickCount++;
            await _callbacks.ClickBestiaryBeastAsync(beastElement, deleteBeasts);
        }

        if (attemptedClickCount <= 0)
        {
            return ResolveReleaseProgress(progress, deleteBeasts, releasedCount: 0);
        }

        var clickElapsedMs = clickStopwatch.ElapsedMilliseconds;

        var releaseWaitStopwatch = Stopwatch.StartNew();
        var releaseWaitElapsedMs = releaseWaitStopwatch.ElapsedMilliseconds;
        if (deleteBeasts)
        {
            var batchResult = await WaitForBestiaryDeleteBatchResultAsync(startingDisplayedCount, attemptedClickCount);
            releaseWaitElapsedMs = releaseWaitStopwatch.ElapsedMilliseconds;
            var displayedReleased = Math.Max(0, startingDisplayedCount - batchResult.CurrentDisplayedCount);

            _callbacks.Log(
                $"Bestiary release batch startingAt={progress.ReleasedBeastCount + 1}. deleteMode=True, targetedClicks={attemptedClickCount}, startingDisplayedCount={startingDisplayedCount}, currentDisplayedCount={batchResult.CurrentDisplayedCount}, displayedReleased={displayedReleased}, deletedByDisplayedCount={batchResult.ReleasedBeastCount}, firstDisplayedChangeMs={FormatNullableDurationMs(batchResult.FirstDisplayedChangeMs)}, lastDisplayedChangeMs={FormatNullableDurationMs(batchResult.LastDisplayedChangeMs)}, clickMs={clickElapsedMs}, releaseWaitMs={releaseWaitElapsedMs}, elapsedMs={clickElapsedMs + releaseWaitElapsedMs}");

            if (!progress.FirstReleaseTimingLogged)
            {
                _callbacks.Log(
                    $"Bestiary first release timing. deleteMode=True, targetedClicks={attemptedClickCount}, startingDisplayedCount={startingDisplayedCount}, currentDisplayedCount={batchResult.CurrentDisplayedCount}, displayedReleased={displayedReleased}, deletedByDisplayedCount={batchResult.ReleasedBeastCount}, firstDisplayedChangeMs={FormatNullableDurationMs(batchResult.FirstDisplayedChangeMs)}, lastDisplayedChangeMs={FormatNullableDurationMs(batchResult.LastDisplayedChangeMs)}, clickMs={clickElapsedMs}, releaseWaitMs={releaseWaitElapsedMs}, elapsedMs={clickElapsedMs + releaseWaitElapsedMs}");
            }

            return ResolveReleaseProgress(
                progress with { FirstReleaseTimingLogged = true },
                deleteBeasts: true,
                releasedCount: batchResult.ReleasedBeastCount);
        }

        var itemizeBatchResult = await WaitForBestiaryItemizeBatchResultAsync(startingDisplayedCount, startingFreeCellCount, attemptedClickCount);
        releaseWaitElapsedMs = releaseWaitStopwatch.ElapsedMilliseconds;
        var itemizeDisplayedReleased = Math.Max(itemizeBatchResult.DisplayedReleasedCount, Math.Max(0, startingDisplayedCount - itemizeBatchResult.CurrentDisplayedCount));

        _callbacks.Log(
            $"Bestiary release batch startingAt={progress.ReleasedBeastCount + 1}. deleteMode=False, targetedClicks={attemptedClickCount}, startingDisplayedCount={startingDisplayedCount}, currentDisplayedCount={itemizeBatchResult.CurrentDisplayedCount}, displayedReleased={itemizeDisplayedReleased}, startingFreeCells={startingFreeCellCount}, currentFreeCells={itemizeBatchResult.CurrentFreeCellCount}, itemizedByInventory={itemizeBatchResult.ReleasedBeastCount}, firstInventoryChangeMs={FormatNullableDurationMs(itemizeBatchResult.FirstInventoryChangeMs)}, lastInventoryChangeMs={FormatNullableDurationMs(itemizeBatchResult.LastInventoryChangeMs)}, firstDisplayedChangeMs={FormatNullableDurationMs(itemizeBatchResult.FirstDisplayedChangeMs)}, lastDisplayedChangeMs={FormatNullableDurationMs(itemizeBatchResult.LastDisplayedChangeMs)}, clickMs={clickElapsedMs}, releaseWaitMs={releaseWaitElapsedMs}, elapsedMs={clickElapsedMs + releaseWaitElapsedMs}");

        if (!progress.FirstReleaseTimingLogged)
        {
            _callbacks.Log(
                $"Bestiary first release timing. deleteMode=False, targetedClicks={attemptedClickCount}, startingDisplayedCount={startingDisplayedCount}, currentDisplayedCount={itemizeBatchResult.CurrentDisplayedCount}, displayedReleased={itemizeDisplayedReleased}, startingFreeCells={startingFreeCellCount}, currentFreeCells={itemizeBatchResult.CurrentFreeCellCount}, itemizedByInventory={itemizeBatchResult.ReleasedBeastCount}, firstInventoryChangeMs={FormatNullableDurationMs(itemizeBatchResult.FirstInventoryChangeMs)}, lastInventoryChangeMs={FormatNullableDurationMs(itemizeBatchResult.LastInventoryChangeMs)}, firstDisplayedChangeMs={FormatNullableDurationMs(itemizeBatchResult.FirstDisplayedChangeMs)}, lastDisplayedChangeMs={FormatNullableDurationMs(itemizeBatchResult.LastDisplayedChangeMs)}, clickMs={clickElapsedMs}, releaseWaitMs={releaseWaitElapsedMs}, elapsedMs={clickElapsedMs + releaseWaitElapsedMs}");
        }

        if (itemizeDisplayedReleased > itemizeBatchResult.ReleasedBeastCount)
        {
            _callbacks.Log(
                $"Bestiary itemize batch mismatch. targetedClicks={attemptedClickCount}, startingDisplayedCount={startingDisplayedCount}, currentDisplayedCount={itemizeBatchResult.CurrentDisplayedCount}, displayedReleased={itemizeDisplayedReleased}, startingFreeCells={startingFreeCellCount}, currentFreeCells={itemizeBatchResult.CurrentFreeCellCount}, itemizedByInventory={itemizeBatchResult.ReleasedBeastCount}, firstInventoryChangeMs={FormatNullableDurationMs(itemizeBatchResult.FirstInventoryChangeMs)}, lastInventoryChangeMs={FormatNullableDurationMs(itemizeBatchResult.LastInventoryChangeMs)}, firstDisplayedChangeMs={FormatNullableDurationMs(itemizeBatchResult.FirstDisplayedChangeMs)}, lastDisplayedChangeMs={FormatNullableDurationMs(itemizeBatchResult.LastDisplayedChangeMs)}, clickMs={clickElapsedMs}, releaseWaitMs={releaseWaitElapsedMs}");
        }

        return ResolveReleaseProgress(
            progress with { FirstReleaseTimingLogged = true },
            deleteBeasts: false,
            releasedCount: itemizeBatchResult.ReleasedBeastCount);
    }

    private async Task<ItemizeBatchResult> WaitForBestiaryItemizeBatchResultAsync(
        int startingDisplayedCount,
        int startingFreeCellCount,
        int targetedClickCount)
    {
        var baseTimeoutMs = Math.Max(1, _callbacks.GetBestiaryReleaseTimeoutMs());
        var pollDelayMs = Math.Max(1, _callbacks.GetBestiaryReleasePollDelayMs());
        var settleAfterChangeMs = Math.Max(125, pollDelayMs * 12);
        var mismatchWaitMs = Math.Max(baseTimeoutMs * 2, settleAfterChangeMs * 3);
        var timeoutMs = baseTimeoutMs + Math.Max(0, targetedClickCount - 1) * Math.Max(75, baseTimeoutMs / 2) + mismatchWaitMs;
        var stopwatch = Stopwatch.StartNew();
        long? firstInventoryChangeMs = null;
        long? lastInventoryChangeMs = null;
        long? firstDisplayedChangeMs = null;
        long? lastDisplayedChangeMs = null;
        long? firstMismatchMs = null;
        var currentDisplayedCount = startingDisplayedCount;
        var currentFreeCellCount = startingFreeCellCount;
        var releasedBeastCount = 0;
        var displayedReleasedCount = 0;

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            _callbacks.ThrowIfAutomationStopRequested();
            currentDisplayedCount = _callbacks.GetTotalCapturedBeastCount();
            currentFreeCellCount = _callbacks.GetPlayerInventoryFreeCellCount();
            var currentReleasedCount = Math.Max(0, startingFreeCellCount - currentFreeCellCount);
            var currentDisplayedReleasedCount = Math.Max(0, startingDisplayedCount - currentDisplayedCount);

            if (currentReleasedCount > releasedBeastCount)
            {
                releasedBeastCount = currentReleasedCount;
                firstInventoryChangeMs ??= stopwatch.ElapsedMilliseconds;
                lastInventoryChangeMs = stopwatch.ElapsedMilliseconds;
            }

            if (currentDisplayedReleasedCount > displayedReleasedCount)
            {
                displayedReleasedCount = currentDisplayedReleasedCount;
                firstDisplayedChangeMs ??= stopwatch.ElapsedMilliseconds;
                lastDisplayedChangeMs = stopwatch.ElapsedMilliseconds;
            }

            if (releasedBeastCount >= targetedClickCount || currentFreeCellCount <= 0)
            {
                break;
            }

            var displayInventoryMismatch = displayedReleasedCount > releasedBeastCount;
            if (displayInventoryMismatch)
            {
                firstMismatchMs ??= stopwatch.ElapsedMilliseconds;
            }
            else
            {
                firstMismatchMs = null;
            }

            var lastObservedChangeMs = MaxNullable(lastInventoryChangeMs, lastDisplayedChangeMs);
            if (lastObservedChangeMs.HasValue)
            {
                var settledSinceLastChange = stopwatch.ElapsedMilliseconds - lastObservedChangeMs.Value >= settleAfterChangeMs;
                if (!displayInventoryMismatch && settledSinceLastChange)
                {
                    break;
                }

                if (displayInventoryMismatch &&
                    settledSinceLastChange &&
                    firstMismatchMs.HasValue &&
                    stopwatch.ElapsedMilliseconds - firstMismatchMs.Value >= mismatchWaitMs)
                {
                    break;
                }
            }

            await _callbacks.DelayAutomationAsync(pollDelayMs);
        }

        currentDisplayedCount = _callbacks.GetTotalCapturedBeastCount();
        currentFreeCellCount = _callbacks.GetPlayerInventoryFreeCellCount();
        releasedBeastCount = Math.Max(releasedBeastCount, Math.Max(0, startingFreeCellCount - currentFreeCellCount));
        displayedReleasedCount = Math.Max(displayedReleasedCount, Math.Max(0, startingDisplayedCount - currentDisplayedCount));
        return new(
            currentDisplayedCount,
            currentFreeCellCount,
            releasedBeastCount,
            displayedReleasedCount,
            firstInventoryChangeMs,
            lastInventoryChangeMs,
            firstDisplayedChangeMs,
            lastDisplayedChangeMs);
    }

    private async Task<DeleteBatchResult> WaitForBestiaryDeleteBatchResultAsync(
        int startingDisplayedCount,
        int targetedClickCount)
    {
        var baseTimeoutMs = Math.Max(1, _callbacks.GetBestiaryReleaseTimeoutMs());
        var pollDelayMs = Math.Max(1, _callbacks.GetBestiaryReleasePollDelayMs());
        var timeoutMs = baseTimeoutMs + Math.Max(0, targetedClickCount - 1) * Math.Max(75, baseTimeoutMs / 2);
        var settleAfterChangeMs = Math.Max(125, pollDelayMs * 12);
        var stopwatch = Stopwatch.StartNew();
        long? firstDisplayedChangeMs = null;
        long? lastDisplayedChangeMs = null;
        var currentDisplayedCount = startingDisplayedCount;
        var releasedBeastCount = 0;

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            _callbacks.ThrowIfAutomationStopRequested();
            currentDisplayedCount = _callbacks.GetTotalCapturedBeastCount();
            var currentReleasedCount = Math.Max(0, startingDisplayedCount - currentDisplayedCount);

            if (currentReleasedCount > releasedBeastCount)
            {
                releasedBeastCount = currentReleasedCount;
                firstDisplayedChangeMs ??= stopwatch.ElapsedMilliseconds;
                lastDisplayedChangeMs = stopwatch.ElapsedMilliseconds;

                if (releasedBeastCount >= targetedClickCount || currentDisplayedCount <= 0)
                {
                    break;
                }
            }
            else if (lastDisplayedChangeMs.HasValue && stopwatch.ElapsedMilliseconds - lastDisplayedChangeMs.Value >= settleAfterChangeMs)
            {
                break;
            }

            await _callbacks.DelayAutomationAsync(pollDelayMs);
        }

        currentDisplayedCount = _callbacks.GetTotalCapturedBeastCount();
        releasedBeastCount = Math.Max(releasedBeastCount, Math.Max(0, startingDisplayedCount - currentDisplayedCount));
        return new(currentDisplayedCount, releasedBeastCount, firstDisplayedChangeMs, lastDisplayedChangeMs);
    }

    private static string FormatNullableDurationMs(long? durationMs)
    {
        return durationMs.HasValue ? durationMs.Value.ToString() : "-";
    }

    private static long? MaxNullable(long? left, long? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return Math.Max(left.Value, right.Value);
    }

    private static ClearProgress ResolveReleaseProgress(
        ClearProgress progress,
        bool deleteBeasts,
        int releasedCount)
    {
        if (releasedCount <= 0)
        {
            var consecutiveFailures = progress.ConsecutiveFailures + 1;
            if (consecutiveFailures >= 12)
            {
                throw new InvalidOperationException("Bestiary clear stalled while releasing captured beasts.");
            }

            return progress with { ConsecutiveFailures = consecutiveFailures };
        }

        var firstItemizedBeastAtUtc = !deleteBeasts && releasedCount > 0 && !progress.FirstItemizedBeastAtUtc.HasValue
            ? DateTime.UtcNow
            : progress.FirstItemizedBeastAtUtc;

        return progress with
        {
            ReleasedBeastCount = progress.ReleasedBeastCount + releasedCount,
            ConsecutiveFailures = 0,
            FirstItemizedBeastAtUtc = firstItemizedBeastAtUtc
        };
    }
}