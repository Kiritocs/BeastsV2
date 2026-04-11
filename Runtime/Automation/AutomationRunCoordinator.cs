using System;
using System.Threading;
using System.Threading.Tasks;
using BeastsV2.Runtime.State;

namespace BeastsV2.Runtime.Automation;

internal sealed record AutomationRunCoordinatorCallbacks(
    Action BeginOverlaySession,
    Action EndOverlaySession,
    Action ResetAutomationState,
    Action ReleaseAutomationModifierKeys,
    Action<string> ShowAutomationError,
    Action<string, bool> UpdateAutomationStatus,
    Action<string, Exception> LogFailure,
    Func<string, AutomationUiCleanupOptions, Task> PrepareAutomationUiAsync);

internal sealed class AutomationRunCoordinator
{
    private readonly AutomationRuntimeState _state;
    private readonly AutomationRunCoordinatorCallbacks _callbacks;

    public AutomationRunCoordinator(AutomationRuntimeState state, AutomationRunCoordinatorCallbacks callbacks)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void BeginRun(bool isBestiaryClearRunning = false)
    {
        _state.IsAutomationRunning = true;
        _state.IsBestiaryClearRunning = isBestiaryClearRunning;
        _state.IsAutomationStopRequested = false;
        _state.CancellationTokenSource = new CancellationTokenSource();
        _callbacks.BeginOverlaySession();
        _callbacks.ResetAutomationState();
    }

    public void EndRun(bool clearBestiaryDeleteModeOverride = false)
    {
        _state.IsAutomationRunning = false;
        _state.IsBestiaryClearRunning = false;
        _state.IsAutomationStopRequested = false;

        if (clearBestiaryDeleteModeOverride)
        {
            _state.BestiaryDeleteModeOverride = null;
        }

        _state.CancellationTokenSource?.Dispose();
        _state.CancellationTokenSource = null;
        _callbacks.EndOverlaySession();
        _callbacks.ResetAutomationState();
        _callbacks.ReleaseAutomationModifierKeys();
    }

    public bool TryQueueRun()
    {
        if (!_state.IsAutomationRunning)
        {
            return true;
        }

        RequestStop();
        return false;
    }

    public async Task ExecuteRunAsync(
        Func<CancellationToken, Task> action,
        string failureLabel,
        string cancelledStatus = null,
        bool isBestiaryClearRunning = false,
        bool clearBestiaryDeleteModeOverride = false)
    {
        BeginRun(isBestiaryClearRunning);

        try
        {
            await action(_state.CancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (!string.IsNullOrWhiteSpace(cancelledStatus))
            {
                _callbacks.UpdateAutomationStatus(cancelledStatus, false);
            }
        }
        catch (Exception ex)
        {
            _callbacks.LogFailure($"{failureLabel} failed.", ex);
            _callbacks.ShowAutomationError($"{failureLabel} failed: {ex.Message}");
        }
        finally
        {
            EndRun(clearBestiaryDeleteModeOverride);
        }
    }

    public async Task RunQueuedAsync(
        Func<CancellationToken, Task> action,
        string failureLabel,
        string cancelledStatus = null,
        bool isBestiaryClearRunning = false,
        bool clearBestiaryDeleteModeOverride = false,
        AutomationUiCleanupOptions uiCleanupOptions = null)
    {
        if (!TryQueueRun())
        {
            return;
        }

        await ExecuteRunAsync(
            async ct =>
            {
                await _callbacks.PrepareAutomationUiAsync(failureLabel, uiCleanupOptions);
                ct.ThrowIfCancellationRequested();
                await action(ct);
            },
            failureLabel,
            cancelledStatus,
            isBestiaryClearRunning,
            clearBestiaryDeleteModeOverride);
    }

    public void RequestStop()
    {
        if (!_state.IsAutomationRunning || _state.IsAutomationStopRequested)
        {
            return;
        }

        _state.IsAutomationStopRequested = true;
        _state.CancellationTokenSource?.Cancel();
        if (!_state.IsBestiaryClearRunning)
        {
            _callbacks.UpdateAutomationStatus("Stopping restock...", false);
        }
    }

    public void ThrowIfStopRequested()
    {
        if (_state.IsAutomationStopRequested)
        {
            throw new OperationCanceledException("Automation stop requested.");
        }
    }
}