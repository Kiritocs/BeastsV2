using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeastsV2;

public partial class Main
{
    #region Shared wait primitives

    private async Task<T> PollAutomationValueAsync<T>(
        Func<T> valueProvider,
        Func<T, bool> completionPredicate,
        int timeoutMs,
        int pollDelayMs,
        int initialDelayMs = 0,
        Func<T, Task> onPendingAsync = null)
    {
        var startedAt = DateTime.UtcNow;
        var adjustedTimeoutMs = GetAutomationTimeoutMs(timeoutMs);
        var adjustedPollDelayMs = Math.Max(1, pollDelayMs);

        if (initialDelayMs > 0)
        {
            await DelayAutomationAsync(initialDelayMs);
        }

        var lastObservedValue = valueProvider();
        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < adjustedTimeoutMs)
        {
            ThrowIfAutomationStopRequested();

            if (completionPredicate(lastObservedValue))
            {
                return lastObservedValue;
            }

            if (onPendingAsync != null)
            {
                await onPendingAsync(lastObservedValue);
            }

            await DelayAutomationAsync(adjustedPollDelayMs);
            lastObservedValue = valueProvider();
        }

        return valueProvider();
    }

    private async Task<bool> WaitForAutomationConditionAsync(
        Func<bool> condition,
        int timeoutMs,
        int pollDelayMs,
        int initialDelayMs = 0)
    {
        return await PollAutomationValueAsync(
            condition,
            isSatisfied => isSatisfied,
            timeoutMs,
            pollDelayMs,
            initialDelayMs);
    }

    private async Task<bool> WaitForAbortableAutomationConditionAsync(
        Func<bool> condition,
        int timeoutMs,
        int pollDelayMs,
        Func<Task<bool>> onPendingAsync,
        int initialDelayMs = 0)
    {
        var aborted = false;
        var completed = await PollAutomationValueAsync(
            condition,
            isSatisfied => isSatisfied || aborted,
            timeoutMs,
            pollDelayMs,
            initialDelayMs,
            onPendingAsync: async isSatisfied =>
            {
                if (isSatisfied || aborted || onPendingAsync == null)
                {
                    return;
                }

                aborted = await onPendingAsync();
            });

        return completed && !aborted;
    }

    private async Task<bool> EnsureAbortableAutomationOpenAsync(
        Func<bool> isOpen,
        int timeoutMs,
        int pollDelayMs,
        Func<Task<bool>> onPendingAbortAsync,
        string timeoutStatus = null)
    {
        if (isOpen?.Invoke() == true)
        {
            return true;
        }

        var aborted = false;
        var opened = await WaitForAbortableAutomationConditionAsync(
            () => isOpen?.Invoke() == true,
            timeoutMs,
            pollDelayMs,
            async () =>
            {
                aborted = onPendingAbortAsync != null && await onPendingAbortAsync();
                return aborted;
            });
        if (opened)
        {
            return true;
        }

        if (!aborted && !string.IsNullOrWhiteSpace(timeoutStatus))
        {
            UpdateAutomationStatus(timeoutStatus);
        }

        return false;
    }

    private async Task<bool> EnsurePollingAutomationOpenAsync(
        Func<bool> isOpen,
        Func<Task<bool>> advanceOpenAsync,
        int retryDelayMs = 0)
    {
        if (isOpen?.Invoke() == true)
        {
            return true;
        }

        while (isOpen?.Invoke() != true)
        {
            ThrowIfAutomationStopRequested();

            if (advanceOpenAsync != null && !await advanceOpenAsync())
            {
                return false;
            }

            if (isOpen?.Invoke() == true)
            {
                return true;
            }

            if (retryDelayMs > 0)
            {
                await DelayAutomationAsync(retryDelayMs);
            }
        }

        return true;
    }

    private async Task<TResult> RetryAutomationAsync<TResult>(
        Func<int, Task<TResult>> attemptAsync,
        Func<TResult, bool> isResolved,
        int maxAttempts,
        int retryDelayMs = 0,
        int firstAttemptNumber = 0)
    {
        if (attemptAsync == null)
        {
            return default;
        }

        var attemptCount = Math.Max(1, maxAttempts);
        for (var attemptIndex = 0; attemptIndex < attemptCount; attemptIndex++)
        {
            ThrowIfAutomationStopRequested();

            var result = await attemptAsync(firstAttemptNumber + attemptIndex);
            if (isResolved?.Invoke(result) == true)
            {
                return result;
            }

            if (attemptIndex < attemptCount - 1 && retryDelayMs > 0)
            {
                await DelayAutomationAsync(retryDelayMs);
            }
        }

        return default;
    }

    private TResult RetryAutomation<TResult>(
        Func<int, TResult> attemptFunc,
        Func<TResult, bool> isResolved,
        int maxAttempts,
        int retryDelayMs = 0,
        int firstAttemptNumber = 0)
    {
        if (attemptFunc == null)
        {
            return default;
        }

        var attemptCount = Math.Max(1, maxAttempts);
        for (var attemptIndex = 0; attemptIndex < attemptCount; attemptIndex++)
        {
            ThrowIfAutomationStopRequested();

            var result = attemptFunc(firstAttemptNumber + attemptIndex);
            if (isResolved?.Invoke(result) == true)
            {
                return result;
            }

            if (attemptIndex < attemptCount - 1 && retryDelayMs > 0)
            {
                Thread.Sleep(retryDelayMs);
            }
        }

        return default;
    }

    private async Task<int> WaitForQuantityChangeToSettleAsync(
        int previousQuantity,
        Func<int?> quantityProvider,
        int timeoutMs,
        int pollDelayMs,
        int stableWindowMs)
    {
        var adjustedPollDelayMs = Math.Max(1, pollDelayMs);
        var adjustedStableWindowMs = Math.Max(1, stableWindowMs);
        var startedAt = DateTime.UtcNow;
        var changedQuantity = previousQuantity;
        var hasObservedChange = false;
        DateTime? lastChangeAtUtc = null;

        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMs)
        {
            ThrowIfAutomationStopRequested();

            var now = DateTime.UtcNow;
            var currentQuantity = quantityProvider();
            if (!currentQuantity.HasValue)
            {
                await DelayAutomationAsync(adjustedPollDelayMs);
                continue;
            }

            if (!hasObservedChange)
            {
                if (currentQuantity.Value == previousQuantity)
                {
                    await DelayAutomationAsync(adjustedPollDelayMs);
                    continue;
                }

                changedQuantity = currentQuantity.Value;
                hasObservedChange = true;
                lastChangeAtUtc = now;
                await DelayAutomationAsync(adjustedPollDelayMs);
                continue;
            }

            if (currentQuantity.Value == changedQuantity)
            {
                if (lastChangeAtUtc.HasValue && (now - lastChangeAtUtc.Value).TotalMilliseconds >= adjustedStableWindowMs)
                {
                    return currentQuantity.Value;
                }

                await DelayAutomationAsync(adjustedPollDelayMs);
                continue;
            }

            changedQuantity = currentQuantity.Value;
            lastChangeAtUtc = now;
            await DelayAutomationAsync(adjustedPollDelayMs);
        }

        return hasObservedChange ? changedQuantity : previousQuantity;
    }

    #endregion
}