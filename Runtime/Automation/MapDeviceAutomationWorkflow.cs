using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeastsV2.Runtime.Automation;

internal sealed record MapDeviceAutomationWorkflowCallbacks(
    Action<string, bool> UpdateAutomationStatus,
    Func<bool> IsAtlasVisible,
    Func<Task> CloseBlockingUiAsync,
    Func<Task<bool>> EnsureMapDeviceWindowOpenAsync,
    Func<StashAutomationSettings, Task> SelectConfiguredMapOnAtlasIfNeededAsync,
    Func<Task> DelayInitialUiSettleAsync,
    Func<StashAutomationSettings, CancellationToken, Task<bool>> TryRestockMissingItemsAsync,
    Func<StashAutomationSettings, CancellationToken, Task> LoadConfiguredPlanAsync,
    Action CapturePreparedMapCostBreakdown,
    Action MoveCursorToActivateButton);

internal sealed class MapDeviceAutomationWorkflow
{
    private readonly MapDeviceAutomationWorkflowCallbacks _callbacks;

    public MapDeviceAutomationWorkflow(MapDeviceAutomationWorkflowCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task RunBodyAsync(StashAutomationSettings automation, CancellationToken cancellationToken)
    {
        _callbacks.UpdateAutomationStatus("Preparing map device...", false);
        if (!_callbacks.IsAtlasVisible())
        {
            await _callbacks.CloseBlockingUiAsync();
        }

        if (!await _callbacks.EnsureMapDeviceWindowOpenAsync())
        {
            return;
        }

        await _callbacks.SelectConfiguredMapOnAtlasIfNeededAsync(automation);
        await _callbacks.DelayInitialUiSettleAsync();

        cancellationToken.ThrowIfCancellationRequested();
        if (await _callbacks.TryRestockMissingItemsAsync(automation, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _callbacks.EnsureMapDeviceWindowOpenAsync())
            {
                return;
            }

            await _callbacks.SelectConfiguredMapOnAtlasIfNeededAsync(automation);
            await _callbacks.DelayInitialUiSettleAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _callbacks.LoadConfiguredPlanAsync(automation, cancellationToken);

        _callbacks.CapturePreparedMapCostBreakdown();
        _callbacks.MoveCursorToActivateButton();
        _callbacks.UpdateAutomationStatus("Map device loaded. Cursor moved to Activate.", false);
    }
}