using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;

namespace BeastsV2;

public partial class Main
{
    #region Constants and state

    private const string MenagerieAreaName = "The Menagerie";
    private const string MenagerieEinharMetadata = "Metadata/NPC/League/Bestiary/EinharMenagerie";
    private const string HideoutMapDeviceMetadata = "Metadata/Terrain/Missions/Hideouts/Objects/StrDexIntMappingDevice";
    private const string HideoutMapDeviceName = "Map Device";
    private const string CapturedMonsterItemPathFragment = "CapturedMonster";
    private const string SettingsFileName = "BeastsV2_settings.json";
    private const int MenagerieTravelTimeoutMs = 15000;
    private const int BestiaryReleaseTimeoutMs = 250;
    private const int MapDeviceOpenTimeoutMs = 4000;
    private const int MapDeviceTransferTimeoutMs = 3000;
    private const int MapDeviceInventoryLookupRetryCount = 4;
    private const int MapDeviceInventoryLookupRetryDelayMs = 60;
    private const int MapDeviceCloseUiMaxAttempts = 3;
    private const int MapTransferExtraConfirmationDelayMs = 10;
    private const int QuantitySettleStableWindowMs = 100;
    private static readonly AutomationTimingValues AutomationTiming = new();

    private sealed class AutomationTimingValues
    {
        public int KeyTapDelayMs { get; } = 1;
        public int CtrlClickPreDelayMs { get; } = 10;
        public int CtrlClickPostDelayMs { get; } = 10;
        public int BestiaryItemizeClickPreDelayMs { get; } = 5;
        public int BestiaryItemizeClickPostDelayMs { get; } = 0;
        public int BestiaryReleasePollDelayMs { get; } = 10;
        public int UiClickPreDelayMs { get; } = 15;
        public int UiCheckInitialSettleDelayMs { get; } = 90;
        public int MinTabClickPostDelayMs { get; } = 15;
        public int FastPollDelayMs { get; } = 15;
        public int StashOpenPollDelayMs { get; } = 30;
        public int StashInteractionDistance { get; } = 100;
        public int TabRetryDelayMs { get; } = 20;
        public int TabChangeTimeoutMs { get; } = 50;
        public int QuantityChangeBaseDelayMs { get; } = 100;
        public int OpenStashPostClickDelayMs { get; } = 250;
        public int FragmentTabBaseTimeoutMs { get; } = 50;
        public int VisibleTabTimeoutMs { get; } = 100;
    }

    #endregion
    #region Automation entry points

    private void BeginAutomationRun(bool isBestiaryClearRunning = false)
    {
        AutomationRuns.BeginRun(isBestiaryClearRunning);
    }

    private void EndAutomationRun(bool clearBestiaryDeleteModeOverride = false)
    {
        AutomationRuns.EndRun(clearBestiaryDeleteModeOverride);
    }

    private bool TryQueueAutomationRun()
    {
        return AutomationRuns.TryQueueRun();
    }

    private async Task ExecuteAutomationRunAsync(
        Func<CancellationToken, Task> action,
        string failureLabel,
        string cancelledStatus = null,
        bool isBestiaryClearRunning = false,
        bool clearBestiaryDeleteModeOverride = false)
    {
        await AutomationRuns.ExecuteRunAsync(
            action,
            failureLabel,
            cancelledStatus,
            isBestiaryClearRunning,
            clearBestiaryDeleteModeOverride);
    }

    private async Task RunQueuedAutomationAsync(
        Func<CancellationToken, Task> action,
        string failureLabel,
        string cancelledStatus = null,
        bool isBestiaryClearRunning = false,
        bool clearBestiaryDeleteModeOverride = false,
        AutomationUiCleanupOptions uiCleanupOptions = null)
    {
        await AutomationRuns.RunQueuedAsync(
            action,
            failureLabel,
            cancelledStatus,
            isBestiaryClearRunning,
            clearBestiaryDeleteModeOverride,
            uiCleanupOptions);
    }

    private bool TryGetStashAutomation(out StashAutomationSettings automation)
    {
        automation = Settings?.StashAutomation;
        if (automation != null)
        {
            return true;
        }

        UpdateAutomationStatus("Stash automation settings unavailable.");
        return false;
    }

    private bool TryGetVisibleStashForAutomation(out StashElement stash)
    {
        stash = GameController?.IngameState?.IngameUi?.StashElement;
        if (stash?.IsVisible == true)
        {
            return true;
        }

        UpdateAutomationStatus("Open the stash before running restock.");
        return false;
    }

    private bool TryGetConfiguredBestiaryRegexForAutomation(string emptyStatus, out string regex)
    {
        regex = GetConfiguredBestiaryRegex();
        if (!string.IsNullOrWhiteSpace(regex))
        {
            return true;
        }

        UpdateAutomationStatus(emptyStatus, forceLog: true);
        return false;
    }

    private void LogConfiguredAutomationTargets((string Label, string IdSuffix, StashAutomationTargetSettings Target)[] automationTargets)
    {
        LogDebug($"Configured targets: {string.Join(" | ", automationTargets.Select(x => $"{x.Label} [{DescribeTarget(x.Target)}]"))}");
    }

    private static string BeastLabel(int count) => $"beast{BeastsV2Helpers.PluralSuffix(count)}";

    private static string SlotLabel(int count) => $"slot{BeastsV2Helpers.PluralSuffix(count)}";

    private sealed record RestockCapacityCheckTarget(
        string Label,
        string IdSuffix,
        string IdentityKey,
        StashAutomationTargetSettings Target,
        int RequestedQuantity);

    private async Task<bool> EnsureBestiaryItemizingCapacityAsync(bool isFullSequence = false)
    {
        var availableInventorySlots = GetPlayerInventoryFreeCellCount();
        LogDebug($"Bestiary clear starting. Can itemize up to {availableInventorySlots} {BeastLabel(availableInventorySlots)} based on free inventory slots.");

        if (availableInventorySlots > 0)
        {
            return true;
        }

        if (isFullSequence)
        {
            LogDebug("Bestiary clear during full sequence: inventory is full but continuing with sequence.");
            return true;
        }

        if (!ShouldAutoStashBestiaryItemizedBeasts())
        {
            _bestiaryInventoryFullStop = true;
            UpdateAutomationStatus("Bestiary clear stopped. Inventory is full and regex itemize auto-stash is disabled.", forceLog: true);
            return false;
        }

        await StashCapturedMonstersAndReturnToBestiaryAsync();
        if (GetPlayerInventoryFreeCellCount() > 0)
        {
            return true;
        }

        UpdateAutomationStatus("Bestiary clear stopped. Inventory is full.", forceLog: true);
        return false;
    }

    private void EnsureFullSequenceCanStartItemizing(int matchingBeastCount)
    {
        if (matchingBeastCount <= 0)
        {
            return;
        }

        var configuredFaustusTabName = Settings?.MerchantAutomation?.SelectedFaustusShopTabName.Value?.Trim();
        if (string.IsNullOrWhiteSpace(configuredFaustusTabName))
        {
            throw new InvalidOperationException("Select a Faustus shop tab before running full sequence itemize.");
        }

        var availableInventorySlots = GetPlayerInventoryFreeCellCount();
        if (availableInventorySlots > 0)
        {
            return;
        }

        var fullMessage = "Full sequence preflight failed. Inventory is full before Bestiary itemizing starts.";
        UpdateAutomationStatus(fullMessage, forceLog: true);
        throw new InvalidOperationException(fullMessage);
    }

    private async Task RunStashAutomationBodyAsync(StashAutomationSettings automation, CancellationToken cancellationToken = default)
    {
        var automationTargets = GetAutomationTargets(automation);
        LogConfiguredAutomationTargets(automationTargets);
        EnsureStashAutomationTargetsFitInInventory(automation, automationTargets, cancellationToken);
        UpdateAutomationStatus("Restocking inventory...");

        var totalTransferred = 0;
        foreach (var (label, idSuffix, target) in automationTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalTransferred += await RestockConfiguredTargetAsync(automation, label, idSuffix, target);
        }

        UpdateAutomationStatus($"Restock complete. Transferred {totalTransferred} total items.");
    }

    private void EnsureStashAutomationTargetsFitInInventory(
        StashAutomationSettings automation,
        (string Label, string IdSuffix, StashAutomationTargetSettings Target)[] automationTargets,
        CancellationToken cancellationToken)
    {
        var capacityTargets = BuildRestockCapacityCheckTargets(automation, automationTargets);
        if (capacityTargets.Count <= 0)
        {
            return;
        }

        UpdateAutomationStatus("Checking inventory capacity...");
        var availableInventorySlots = GetPlayerInventoryFreeCellCount();
        if (availableInventorySlots <= 0)
        {
            var fullMessage = "Restock preflight failed. Inventory is full.";
            UpdateAutomationStatus(fullMessage, forceLog: true);
            throw new InvalidOperationException(fullMessage);
        }

        var requiredInventorySlots = 0;
        foreach (var capacityTarget in capacityTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inventoryQuantityBeforeTransfer = GetReadablePlayerInventoryMatchingQuantity(capacityTarget.Target);
            var inventoryStackCountBeforeTransfer = GetReadablePlayerInventoryMatchingStackCount(capacityTarget.Target);
            var remainingRequestedQuantity = Math.Max(0, capacityTarget.RequestedQuantity - inventoryQuantityBeforeTransfer);
            if (remainingRequestedQuantity <= 0)
            {
                LogDebug(
                    $"Restock preflight target '{capacityTarget.Label}' skipped because inventory already satisfies the cumulative request. cumulativeRequested={capacityTarget.RequestedQuantity}, identity='{capacityTarget.IdentityKey}', inventoryQuantityBefore={inventoryQuantityBeforeTransfer}, inventoryStackCountBefore={inventoryStackCountBeforeTransfer}, availableSlots={availableInventorySlots}");
                continue;
            }

            var knownFullStackSize = GetConfiguredRestockTargetFullStackSize(capacityTarget.Target);
            var expectedFinalStackCount = Math.Max(
                0,
                (int)Math.Ceiling((inventoryQuantityBeforeTransfer + remainingRequestedQuantity) / (double)Math.Max(1, knownFullStackSize)));
            var requiredSlotsForTarget = Math.Max(0, expectedFinalStackCount - inventoryStackCountBeforeTransfer);
            requiredInventorySlots += requiredSlotsForTarget;
            LogDebug(
                $"Restock preflight target '{capacityTarget.Label}' cumulativeRequested={capacityTarget.RequestedQuantity}, identity='{capacityTarget.IdentityKey}', remainingRequested={remainingRequestedQuantity}, inventoryQuantityBefore={inventoryQuantityBeforeTransfer}, inventoryStackCountBefore={inventoryStackCountBeforeTransfer}, knownFullStackSize={knownFullStackSize}, expectedFinalStackCount={expectedFinalStackCount}, requiredSlots={requiredSlotsForTarget}, cumulativeRequiredSlots={requiredInventorySlots}, availableSlots={availableInventorySlots}");

            if (requiredInventorySlots > availableInventorySlots)
            {
                var insufficientMessage = BuildRestockPreflightInsufficientSlotsMessage(requiredInventorySlots, availableInventorySlots);
                UpdateAutomationStatus(insufficientMessage, forceLog: true);
                throw new InvalidOperationException(insufficientMessage);
            }
        }

        if (requiredInventorySlots > availableInventorySlots)
        {
            var insufficientMessage = BuildRestockPreflightInsufficientSlotsMessage(requiredInventorySlots, availableInventorySlots);
            UpdateAutomationStatus(insufficientMessage, forceLog: true);
            throw new InvalidOperationException(insufficientMessage);
        }

        LogDebug($"Restock preflight passed. requiredSlots={requiredInventorySlots}, availableSlots={availableInventorySlots}, uniqueTargets={capacityTargets.Count}");
    }

    private static string BuildRestockPreflightInsufficientSlotsMessage(int requiredInventorySlots, int availableInventorySlots)
    {
        return $"Restock preflight failed. Configured targets need {requiredInventorySlots} inventory {SlotLabel(requiredInventorySlots)}, but only {availableInventorySlots} {SlotLabel(availableInventorySlots)} are free.";
    }

    private static List<RestockCapacityCheckTarget> BuildRestockCapacityCheckTargets(
        StashAutomationSettings automation,
        IEnumerable<(string Label, string IdSuffix, StashAutomationTargetSettings Target)> automationTargets)
    {
        var uniqueTargets = new Dictionary<string, RestockCapacityCheckTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, idSuffix, target) in automationTargets ?? [])
        {
            if (!IsTargetEnabledForAutomation(target))
            {
                continue;
            }

            var identityKey = GetAutomationTargetIdentityKey(target);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                continue;
            }

            uniqueTargets[identityKey] = new RestockCapacityCheckTarget(
                label,
                idSuffix,
                identityKey,
                target,
                Math.Max(GetConfiguredTargetQuantity(target), GetCumulativeConfiguredTargetQuantity(automation, idSuffix, target)));
        }

        return uniqueTargets.Values.ToList();
    }

    private async Task RunStashAutomationFromHotkeyAsync()
    {
        if (!TryGetStashAutomation(out _))
        {
            return;
        }

        await RunQueuedAutomationAsync(
            async ct =>
            {
                ct.ThrowIfCancellationRequested();

                if (!await EnsureStashOpenForAutomationAsync())
                {
                    return;
                }

                ct.ThrowIfCancellationRequested();

                if (!TryGetStashAutomation(out var automation))
                {
                    return;
                }

                if (!TryGetVisibleStashForAutomation(out var stash))
                {
                    return;
                }

                LogDebug($"Run started. {DescribeStash(stash)}");
                await RunStashAutomationBodyAsync(automation, ct);
            },
            "Restock",
            cancelledStatus: "Restock cancelled.",
            uiCleanupOptions: new AutomationUiCleanupOptions(KeepInventory: true, KeepStash: true));
    }

    private async Task LaunchBestiaryClearAutomationAsync()
    {
        await RunQueuedAutomationAsync(
            ct => BestiaryWorkflow.RunClearAsync(ct),
            "Bestiary clear",
            isBestiaryClearRunning: true,
            clearBestiaryDeleteModeOverride: true,
            uiCleanupOptions: new AutomationUiCleanupOptions(KeepBestiary: true));
    }

    private async Task RunBestiaryDeleteAutomationFromHotkeyAsync()
    {
        if (_isAutomationRunning)
        {
            RequestAutomationStop();
            return;
        }

        _bestiaryDeleteModeOverride = true;
        await RunQueuedAutomationAsync(
            ct => BestiaryWorkflow.RunClearAsync(ct),
            "Bestiary delete",
            isBestiaryClearRunning: true,
            clearBestiaryDeleteModeOverride: true,
            uiCleanupOptions: new AutomationUiCleanupOptions(KeepBestiary: true));
    }

    private async Task<int> RunBestiaryRegexItemizeBodyAsync(string regex, bool isFullSequence = false, CancellationToken cancellationToken = default)
    {
        return await BestiaryWorkflow.RunRegexItemizeBodyAsync(regex, isFullSequence, cancellationToken);
    }

    private async Task<int> RunBestiaryRegexItemizeForFullSequenceAsync(string regex, CancellationToken cancellationToken)
    {
        _isBestiaryClearRunning = true;

        try
        {
            return await RunBestiaryRegexItemizeBodyAsync(regex, isFullSequence: true, cancellationToken: cancellationToken);
        }
        finally
        {
            _isBestiaryClearRunning = false;
            _bestiaryDeleteModeOverride = null;
        }
    }

    private async Task RunBestiaryRegexItemizeAutomationFromHotkeyAsync()
    {
        if (!TryGetConfiguredBestiaryRegexForAutomation("Bestiary regex itemize stopped. Bestiary Regex is empty.", out var regex))
        {
            return;
        }

        await RunQueuedAutomationAsync(
            async ct =>
            {
                await RunBestiaryRegexItemizeBodyAsync(regex, cancellationToken: ct);
            },
            "Bestiary regex itemize",
            isBestiaryClearRunning: true,
            clearBestiaryDeleteModeOverride: true,
            uiCleanupOptions: new AutomationUiCleanupOptions(KeepBestiary: true, KeepInventory: true));
    }

    private async Task RunFullSequenceAutomationAsync()
    {
        if (!TryGetConfiguredBestiaryRegexForAutomation("Full sequence stopped. Bestiary Regex is empty.", out var regex))
        {
            return;
        }

        await RunQueuedAutomationAsync(
            ct => FullSequenceWorkflow.RunAsync(regex, ct),
            "Full sequence",
            cancelledStatus: "Full sequence cancelled.",
            clearBestiaryDeleteModeOverride: true,
            uiCleanupOptions: new AutomationUiCleanupOptions(KeepBestiary: true));
    }

    #endregion
    #region Shared automation state

    private async Task EnsureTravelToMenagerieAsync()
    {
        if (IsInMenagerie())
        {
            return;
        }

        if (await TryTravelViaChatCommandAsync(
                "/menagerie",
                "The Menagerie",
                IsInMenagerie,
                MenagerieTravelTimeoutMs,
                maxAttempts: 2))
        {
            return;
        }

        throw new InvalidOperationException($"Timed out travelling to The Menagerie. Current area: '{GameController?.Area?.CurrentArea?.Name ?? "<null>"}'.");
    }

    private async Task<bool> TryTravelViaChatCommandAsync(
        string command,
        string destinationLabel,
        Func<bool> hasArrived,
        int timeoutMs,
        int maxAttempts = 1)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(destinationLabel) || hasArrived == null)
        {
            return false;
        }

        if (hasArrived())
        {
            return true;
        }

        for (var attempt = 1; attempt <= Math.Max(1, maxAttempts); attempt++)
        {
            ThrowIfAutomationStopRequested();

            UpdateAutomationStatus($"Travelling to {destinationLabel}...");
            LogDebug($"Travelling to {destinationLabel}. attempt={attempt}, currentArea='{GameController?.Area?.CurrentArea?.Name ?? "<null>"}'");

            await SendChatCommandAsync(command);
            if (await WaitForAutomationConditionAsync(
                    hasArrived,
                    timeoutMs,
                    Math.Max(AutomationTiming.FastPollDelayMs, 25)) || hasArrived())
            {
                return true;
            }

            LogDebug($"{destinationLabel} travel attempt {attempt} timed out. currentArea='{GameController?.Area?.CurrentArea?.Name ?? "<null>"}'");
            if (attempt < maxAttempts)
            {
                await DelayForUiCheckAsync(250);
            }
        }

        return hasArrived();
    }

    private void ResetAutomationState()
    {
        _bestiaryAutoStashOverride = null;
        _bestiaryInventoryFullStop = false;
        _activeBestiarySearchRegex = null;
        _lastAutomationFragmentScarabTabIndex = -1;
        _lastAutomationMapStashTierSelection = -1;
        _lastAutomationMapStashPageNumber = -1;
    }

    private bool ShouldAutoStashBestiaryItemizedBeasts()
    {
        return BestiaryWorkflow.ShouldAutoStashItemizedBeasts();
    }

    private void RequestAutomationStop()
    {
        AutomationRuns.RequestStop();
    }

    private void ThrowIfAutomationStopRequested()
    {
        AutomationRuns.ThrowIfStopRequested();
    }

    private void ReleaseAutomationModifierKeys()
    {
        ReleaseAutomationKeys(Keys.ControlKey, Keys.LControlKey);
    }

    private bool IsMapStashTarget(StashAutomationTargetSettings target)
    {
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        return stash?.VisibleStash?.InvType == InventoryType.MapStash && TryGetConfiguredMapTier(target).HasValue;
    }

    #endregion
}

