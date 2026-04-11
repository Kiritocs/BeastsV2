using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeastsV2.Runtime.Automation;

internal sealed record FullSequenceAutomationWorkflowCallbacks(
    Func<string, CancellationToken, Task<int>> RunBestiaryFullSequenceItemizeAsync,
    Func<CancellationToken, Task> RunMerchantListingAsync,
    Action<string, bool> UpdateAutomationStatus);

internal sealed class FullSequenceAutomationWorkflow
{
    private readonly FullSequenceAutomationWorkflowCallbacks _callbacks;

    public FullSequenceAutomationWorkflow(FullSequenceAutomationWorkflowCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task RunAsync(string regex, CancellationToken cancellationToken)
    {
        var itemizedBeastCount = await _callbacks.RunBestiaryFullSequenceItemizeAsync(regex, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (itemizedBeastCount > 0)
        {
            await _callbacks.RunMerchantListingAsync(cancellationToken);
        }
        else
        {
            _callbacks.UpdateAutomationStatus("Skipping Faustus listing. No beasts were itemized during this full sequence.", true);
        }
    }
}