using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BeastsV2.Runtime.Automation;

internal sealed record MerchantAutomationWorkflowCallbacks(
    Action ReleaseAutomationTriggerKeys,
    Func<Task> EnsureTravelToHideoutAsync,
    Func<Task<bool>> EnsureMerchantPanelOpenAsync,
    Func<Task<bool>> EnsureShopInventorySelectedAsync,
    Func<string> ResolveConfiguredShopTabName,
    Func<string, Task> SelectOfflineMerchantTabAsync,
    Action EnsureWholeRunTabCapacity,
    Func<bool> IsMerchantPanelVisible,
    Func<string, bool, Task> EnsureListingContextReadyAsync,
    Func<bool> IsCurrentTabFull,
    Func<MerchantListingCandidate> GetNextSellableCapturedMonster,
    Action<string, bool> UpdateAutomationStatus,
    Func<ExileCore.PoEMemory.Elements.InventoryElements.NormalInventoryItem, int, Task<MerchantListingAttemptResult>> TryListCapturedMonsterAndConfirmAsync,
    Func<int> GetVisibleCapturedMonsterInventoryItemCount,
    Func<int> GetClickDelayMs,
    Func<int, Task> DelayAutomationAsync,
    Action<string> LogDebug);

internal sealed class MerchantAutomationWorkflow
{
    private readonly MerchantAutomationWorkflowCallbacks _callbacks;

    public MerchantAutomationWorkflow(MerchantAutomationWorkflowCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task RunListingBodyAsync(CancellationToken cancellationToken = default)
    {
        var listingStartupStopwatch = Stopwatch.StartNew();
        _callbacks.ReleaseAutomationTriggerKeys();
        await MeasureStepAsync("Faustus listing prep: travel to hideout", async () =>
        {
            await _callbacks.EnsureTravelToHideoutAsync();
            return true;
        });

        var listedCount = 0;
        var skippedNoPriceCount = 0;
        var consecutiveFailures = 0;
        var loggedFirstListingPrep = false;
        var loggedFirstListingAttempt = false;

        if (!await MeasureStepAsync("Faustus listing prep: open merchant panel", _callbacks.EnsureMerchantPanelOpenAsync))
        {
            throw new InvalidOperationException("Could not open Faustus merchant panel.");
        }

        if (!await MeasureStepAsync("Faustus listing prep: select Shop inventory", _callbacks.EnsureShopInventorySelectedAsync))
        {
            throw new InvalidOperationException("Could not switch Faustus to the Shop inventory.");
        }

        var configuredTabName = MeasureStep("Faustus listing prep: resolve configured shop tab", _callbacks.ResolveConfiguredShopTabName);
        if (string.IsNullOrWhiteSpace(configuredTabName))
        {
            throw new InvalidOperationException("Select a Faustus shop tab before listing itemized beasts.");
        }

        await MeasureStepAsync($"Faustus listing prep: select configured shop tab '{configuredTabName}'", async () =>
        {
            await _callbacks.SelectOfflineMerchantTabAsync(configuredTabName);
            return true;
        });
        MeasureStep("Faustus listing prep: capacity preflight", () =>
        {
            _callbacks.EnsureWholeRunTabCapacity();
            return true;
        });

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var merchantPanelWasVisible = _callbacks.IsMerchantPanelVisible();
            var ensureListingContextStopwatch = Stopwatch.StartNew();
            await _callbacks.EnsureListingContextReadyAsync(configuredTabName, merchantPanelWasVisible);
            var ensureListingContextElapsedMs = ensureListingContextStopwatch.ElapsedMilliseconds;

            var candidateScanStopwatch = Stopwatch.StartNew();
            var candidate = _callbacks.GetNextSellableCapturedMonster();
            var candidateScanElapsedMs = candidateScanStopwatch.ElapsedMilliseconds;
            if (!loggedFirstListingPrep)
            {
                _callbacks.LogDebug(
                    $"Faustus first listing prep timing. merchantPanelWasVisible={merchantPanelWasVisible}, ensureListingContextMs={ensureListingContextElapsedMs}, candidateScanMs={candidateScanElapsedMs}, totalBeforeFirstListingMs={listingStartupStopwatch.ElapsedMilliseconds}");
                loggedFirstListingPrep = true;
            }

            if (candidate == null)
            {
                skippedNoPriceCount = _callbacks.GetVisibleCapturedMonsterInventoryItemCount();
                break;
            }

            if (_callbacks.IsCurrentTabFull())
            {
                throw new InvalidOperationException($"Faustus shop tab '{configuredTabName}' is full.");
            }

            _callbacks.UpdateAutomationStatus($"Listing itemized beast {candidate.BeastName} for {candidate.ListingPriceChaos} chaos...", false);

            var listingAttemptStopwatch = Stopwatch.StartNew();
            var listingAttempt = await _callbacks.TryListCapturedMonsterAndConfirmAsync(candidate.Item, candidate.ListingPriceChaos);
            if (!loggedFirstListingAttempt)
            {
                _callbacks.LogDebug(
                    $"Faustus first listing attempt timing. popupOpened={listingAttempt.PopupOpened}, previousCount={listingAttempt.PreviousCount}, currentCount={listingAttempt.CurrentCount}, clickItemMs={listingAttempt.ClickItemMs}, popupWaitMs={listingAttempt.PopupWaitMs}, enterPriceMs={listingAttempt.EnterPriceMs}, popupReadyMs={listingAttempt.PopupReadyMs}, selectAndClearMs={listingAttempt.SelectAndClearMs}, typeDigitsMs={listingAttempt.TypeDigitsMs}, confirmTextMs={listingAttempt.ConfirmTextMs}, submitCloseMs={listingAttempt.SubmitCloseMs}, countChangeMs={listingAttempt.CountChangeMs}, countChangeFallbackUsed={listingAttempt.CountChangeFallbackUsed}, elapsedMs={listingAttemptStopwatch.ElapsedMilliseconds}");
                loggedFirstListingAttempt = true;
            }
            if (!listingAttempt.PopupOpened)
            {
                consecutiveFailures = IncrementFailureCount(consecutiveFailures, "Listing itemized beasts stalled while opening the Faustus price popup.");
                await _callbacks.DelayAutomationAsync(15);
                continue;
            }

            if (listingAttempt.CurrentCount >= listingAttempt.PreviousCount)
            {
                consecutiveFailures = IncrementFailureCount(consecutiveFailures, "Listing itemized beasts stalled while moving beasts into the Faustus shop tab.");
                await _callbacks.DelayAutomationAsync(15);
                continue;
            }

            listedCount += listingAttempt.PreviousCount - listingAttempt.CurrentCount;
            consecutiveFailures = 0;
            await _callbacks.DelayAutomationAsync(_callbacks.GetClickDelayMs());
        }

        _callbacks.UpdateAutomationStatus(listedCount > 0
            ? skippedNoPriceCount > 0
                ? $"Listed {listedCount} itemized {BeastLabel(listedCount)}. Skipped {skippedNoPriceCount} without price data."
                : $"Listed {listedCount} itemized {BeastLabel(listedCount)}."
            : skippedNoPriceCount > 0
                ? $"No sellable itemized beasts found. {skippedNoPriceCount} {BeastLabel(skippedNoPriceCount)} missing price data."
                : "No itemized beasts were found in player inventory.", true);
    }

    private static int IncrementFailureCount(int consecutiveFailures, string stallMessage)
    {
        consecutiveFailures++;
        if (consecutiveFailures >= 3)
        {
            throw new InvalidOperationException(stallMessage);
        }

        return consecutiveFailures;
    }

    private T MeasureStep<T>(string label, Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            _callbacks.LogDebug($"{label} took {stopwatch.ElapsedMilliseconds}ms.");
        }
    }

    private async Task<T> MeasureStepAsync<T>(string label, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        finally
        {
            _callbacks.LogDebug($"{label} took {stopwatch.ElapsedMilliseconds}ms.");
        }
    }

    private static string BeastLabel(int count) => $"beast{BeastsV2Helpers.PluralSuffix(count)}";
}