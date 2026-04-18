using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Nodes;

namespace BeastsV2;

public partial class Main
{
    #region Map device automation

    private const int MapDeviceFragmentSlotCount = 5;
    private const float AtlasTargetScale = 0.45f;
    private const float AtlasTargetScaleTolerance = 0.015f;
    private const uint MouseEventWheel = 0x0800;
    private const int MouseWheelDelta = 120;
    private const float AtlasCenteredMinY = -1200f;
    private const float AtlasCenteredMaxY = -1100f;
    private const string OpenMapSelectionValue = "open Map";
    private const float AtlasHudTopLeftMinX = 0.00f;
    private const float AtlasHudTopLeftMaxX = 0.21f;
    private const float AtlasHudTopLeftMinY = 0.00f;
    private const float AtlasHudTopLeftMaxY = 0.080f;
    private const float AtlasHudTopCenterMinX = 0.33f;
    private const float AtlasHudTopCenterMaxX = 0.67f;
    private const float AtlasHudTopCenterMinY = 0.00f;
    private const float AtlasHudTopCenterMaxY = 0.115f;
    private const float AtlasHudBottomLeftGlobeMinX = 0.00f;
    private const float AtlasHudBottomLeftGlobeMaxX = 0.115f;
    private const float AtlasHudBottomLeftGlobeMinY = 0.80f;
    private const float AtlasHudBottomLeftGlobeMaxY = 1.00f;
    private const float AtlasHudBottomRightGlobeMinX = 0.885f;
    private const float AtlasHudBottomRightGlobeMaxX = 1.00f;
    private const float AtlasHudBottomRightGlobeMinY = 0.80f;
    private const float AtlasHudBottomRightGlobeMaxY = 1.00f;
    private const float AtlasHudBottomLeftBarMinX = 0.00f;
    private const float AtlasHudBottomLeftBarMaxX = 0.29f;
    private const float AtlasHudBottomLeftBarMinY = 0.87f;
    private const float AtlasHudBottomLeftBarMaxY = 1.00f;
    private const float AtlasHudBottomRightBarMinX = 0.71f;
    private const float AtlasHudBottomRightBarMaxX = 1.00f;
    private const float AtlasHudBottomRightBarMinY = 0.87f;
    private const float AtlasHudBottomRightBarMaxY = 1.00f;

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, UIntPtr dwExtraInfo);

    private void TryCapturePreparedMapCostBreakdownFromMapDeviceWindow()
    {
        var now = DateTime.UtcNow;
        if (_lastPreparedMapCostCapturePollUtc != DateTime.MinValue &&
            now - _lastPreparedMapCostCapturePollUtc < PreparedMapCostCapturePollInterval)
        {
            return;
        }

        _lastPreparedMapCostCapturePollUtc = now;

        var mapDeviceWindow = GameController?.IngameState?.IngameUi?.MapDeviceWindow;
        if (mapDeviceWindow?.IsVisible != true)
        {
            return;
        }

        CapturePreparedMapCostBreakdownFromMapDeviceWindow();
    }

    private void CapturePreparedMapCostBreakdownFromMapDeviceWindow()
    {
        var itemNames = GetVisibleMapDeviceItemNames();
        if (itemNames.Count <= 0)
        {
            return;
        }

        var breakdown = BuildMapDeviceCostBreakdownFromItemNames(itemNames);
        var usesDuplicatingScarab = itemNames.Any(IsDuplicatingScarabItemName);
        SetPreparedMapCostBreakdown(breakdown, usesDuplicatingScarab);
    }

    private List<MapCostItem> BuildMapDeviceCostBreakdownFromItemNames(IReadOnlyList<string> itemNames)
    {
        var items = new List<MapCostItem>();
        if (itemNames == null)
        {
            return items;
        }

        foreach (var itemName in itemNames)
        {
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            if (!TryGetConfiguredItemPriceChaos(itemName, out var price) || price <= 0)
            {
                continue;
            }

            items.Add(new MapCostItem
            {
                ItemName = itemName,
                UnitPriceChaos = price,
            });
        }

        return items;
    }

    private List<string> GetVisibleMapDeviceItemNames()
    {
        if (!CanReadMapDeviceWindowState())
        {
            return [];
        }

        return GetVisibleMapDeviceItems()
            .Select(item => TryGetMapDeviceItemName(item?.Item))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    private static string TryGetMapDeviceItemName(Entity entity)
    {
        if (entity == null)
        {
            return null;
        }

        var itemName = entity.GetComponent<Base>()?.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            return itemName;
        }

        var mapTier = entity.GetComponent<MapKey>()?.Tier;
        if (mapTier > 0)
        {
            return $"Map (Tier {mapTier})";
        }

        return null;
    }

    private IList<NormalInventoryItem> GetVisiblePlayerInventoryItemsOrThrow(string errorMessage)
    {
        var inventoryItems = GetVisiblePlayerInventoryItems();
        if (inventoryItems == null)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return inventoryItems;
    }

    private async Task<bool> TryRestockMissingMapDeviceItemsAsync(StashAutomationSettings automation, CancellationToken cancellationToken)
    {
        if (!TryBuildMapDeviceAutoRestockRequest(automation, out var effectiveAutomation, out var deficitSummary))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        UpdateAutomationStatus(
            $"Map Device targets are missing required items ({deficitSummary}). Running restock before continuing map-device load.",
            true);

        await CloseMapDeviceBlockingUiAsync();

        if (!await EnsureStashOpenForAutomationAsync())
        {
            const string failureMessage = "Auto-restock failed because the stash could not be opened after closing the Map Device UI.";
            UpdateAutomationStatus(failureMessage, true);
            throw new InvalidOperationException(failureMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RunStashAutomationBodyAsync(effectiveAutomation, cancellationToken);
        return true;
    }

    private bool TryBuildMapDeviceAutoRestockRequest(
        StashAutomationSettings automation,
        out StashAutomationSettings effectiveAutomation,
        out string deficitSummary)
    {
        effectiveAutomation = null;
        deficitSummary = string.Empty;

        if (automation?.AutoRestockMissingMapDeviceItems?.Value != true || !CanReadMapDeviceWindowState())
        {
            return false;
        }

        effectiveAutomation = BuildMapDeviceAutoRestockAutomation(automation);
        var missingTargets = GetAutomationTargets(effectiveAutomation)
            .Where(x => IsTargetEnabledForAutomation(x.Target))
            .Select(x => $"{x.Label}={GetConfiguredTargetQuantity(x.Target)}")
            .ToArray();
        if (missingTargets.Length <= 0)
        {
            effectiveAutomation = null;
            return false;
        }

        deficitSummary = string.Join(", ", missingTargets);
        return true;
    }

    private StashAutomationSettings BuildMapDeviceAutoRestockAutomation(StashAutomationSettings automation)
    {
        var sourceTargets = GetAutomationTargets(automation);
        var availablePreparedQuantitiesByIdentity = BuildVisibleMapDevicePreparedQuantitiesByIdentity(automation, GetVisibleMapDeviceItems(), GetVisibleMapDeviceStorageItems());
        var preparedDeficitTargets = BuildAdjustedTargetsForMapDeviceAutoRestock(sourceTargets, availablePreparedQuantitiesByIdentity);
        var availableInventoryQuantitiesByIdentity = BuildReadablePlayerInventoryQuantitiesByIdentity(sourceTargets);
        var effectiveTargets = BuildAdjustedTargetsForMapDeviceAutoRestock(
            GetAutomationTargets(CloneAutomationForMapDeviceAutoRestock(automation, preparedDeficitTargets)),
            availableInventoryQuantitiesByIdentity);

        LogDebug($"Map Device auto-restock deficits: {string.Join(" | ", sourceTargets.Select((entry, index) => $"{entry.Label} [configured={GetConfiguredTargetQuantity(entry.Target)}, prepared={GetAvailableIdentityQuantity(entry.Target, availablePreparedQuantitiesByIdentity)}, inventory={GetAvailableIdentityQuantity(entry.Target, availableInventoryQuantitiesByIdentity)}, restock={GetConfiguredTargetQuantity(effectiveTargets[index])}]"))}");

        return CloneAutomationForMapDeviceAutoRestock(automation, effectiveTargets);
    }

    private Dictionary<string, int> BuildReadablePlayerInventoryQuantitiesByIdentity(
        IEnumerable<(string Label, string IdSuffix, StashAutomationTargetSettings Target)> automationTargets)
    {
        var quantitiesByIdentity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, target) in automationTargets ?? [])
        {
            if (!IsTargetEnabledForAutomation(target))
            {
                continue;
            }

            var identityKey = GetAutomationTargetIdentityKey(target);
            if (string.IsNullOrWhiteSpace(identityKey) || quantitiesByIdentity.ContainsKey(identityKey))
            {
                continue;
            }

            quantitiesByIdentity[identityKey] = GetReadablePlayerInventoryMatchingQuantity(target);
        }

        return quantitiesByIdentity;
    }

    private static StashAutomationTargetSettings[] BuildAdjustedTargetsForMapDeviceAutoRestock(
        (string Label, string IdSuffix, StashAutomationTargetSettings Target)[] sourceTargets,
        IReadOnlyDictionary<string, int> availableQuantitiesByIdentity)
    {
        var consumedAvailableQuantitiesByIdentity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var effectiveTargets = new StashAutomationTargetSettings[sourceTargets.Length];

        for (var index = 0; index < sourceTargets.Length; index++)
        {
            var (_, _, sourceTarget) = sourceTargets[index];
            effectiveTargets[index] = CloneTargetForMapDeviceAutoRestock(
                sourceTarget,
                GetMapDeviceAutoRestockRequiredQuantity(
                    sourceTarget,
                    availableQuantitiesByIdentity,
                    consumedAvailableQuantitiesByIdentity));
        }

        return effectiveTargets;
    }

    private static StashAutomationSettings CloneAutomationForMapDeviceAutoRestock(
        StashAutomationSettings automation,
        StashAutomationTargetSettings[] effectiveTargets)
    {
        return new StashAutomationSettings
        {
            AutoRestockMissingMapDeviceItems = new ToggleNode(automation?.AutoRestockMissingMapDeviceItems?.Value == true),
            Target1 = effectiveTargets[0],
            Target2 = effectiveTargets[1],
            Target3 = effectiveTargets[2],
            Target4 = effectiveTargets[3],
            Target5 = effectiveTargets[4],
            Target6 = effectiveTargets[5],
        };
    }

    private static Dictionary<string, int> BuildVisibleMapDevicePreparedQuantitiesByIdentity(
        StashAutomationSettings automation,
        IList<NormalInventoryItem> visibleMapDeviceItems,
        IList<NormalInventoryItem> visibleStorageItems)
    {
        var quantitiesByIdentity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, _, target) in GetAutomationTargets(automation))
        {
            if (!IsTargetEnabledForAutomation(target))
            {
                continue;
            }

            var identityKey = GetAutomationTargetIdentityKey(target);
            if (string.IsNullOrWhiteSpace(identityKey) || quantitiesByIdentity.ContainsKey(identityKey))
            {
                continue;
            }

            quantitiesByIdentity[identityKey] = CountMatchingTargetQuantity(visibleMapDeviceItems, target) + CountMatchingTargetQuantity(visibleStorageItems, target);
        }

        return quantitiesByIdentity;
    }

    private static int GetMapDeviceAutoRestockRequiredQuantity(
        StashAutomationTargetSettings target,
        IReadOnlyDictionary<string, int> availablePreparedQuantitiesByIdentity,
        IDictionary<string, int> consumedPreparedQuantitiesByIdentity)
    {
        var configuredQuantity = GetConfiguredTargetQuantity(target);
        if (!IsTargetEnabledForAutomation(target) || configuredQuantity <= 0)
        {
            return 0;
        }

        var identityKey = GetAutomationTargetIdentityKey(target);
        if (string.IsNullOrWhiteSpace(identityKey))
        {
            return configuredQuantity;
        }

        var availablePreparedQuantity = 0;
        var consumedPreparedQuantity = 0;
        availablePreparedQuantitiesByIdentity?.TryGetValue(identityKey, out availablePreparedQuantity);
        consumedPreparedQuantitiesByIdentity?.TryGetValue(identityKey, out consumedPreparedQuantity);
        var remainingPreparedQuantity = Math.Max(0, availablePreparedQuantity - consumedPreparedQuantity);
        var preparedQuantityAppliedToTarget = Math.Min(configuredQuantity, remainingPreparedQuantity);
        if (consumedPreparedQuantitiesByIdentity != null)
        {
            consumedPreparedQuantitiesByIdentity[identityKey] = consumedPreparedQuantity + preparedQuantityAppliedToTarget;
        }

        return Math.Max(0, configuredQuantity - preparedQuantityAppliedToTarget);
    }

    private static int GetAvailableIdentityQuantity(
        StashAutomationTargetSettings target,
        IReadOnlyDictionary<string, int> quantitiesByIdentity)
    {
        var identityKey = GetAutomationTargetIdentityKey(target);
        return !string.IsNullOrWhiteSpace(identityKey) && quantitiesByIdentity != null && quantitiesByIdentity.TryGetValue(identityKey, out var quantity)
            ? Math.Max(0, quantity)
            : 0;
    }

    private static StashAutomationTargetSettings CloneTargetForMapDeviceAutoRestock(StashAutomationTargetSettings sourceTarget, int quantity)
    {
        var clone = new StashAutomationTargetSettings
        {
            Enabled = new ToggleNode(sourceTarget?.Enabled?.Value == true),
            ItemName = new TextNode(sourceTarget?.ItemName?.Value ?? string.Empty),
            Quantity = new RangeNode<int>(Math.Clamp(quantity, 0, StashAutomationTargetSettings.MaxQuantity), 0, StashAutomationTargetSettings.MaxQuantity),
        };

        clone.SelectedTabName.Value = sourceTarget?.SelectedTabName?.Value ?? string.Empty;
        return clone;
    }

    private async Task LoadConfiguredMapDevicePlanAsync(StashAutomationSettings automation, CancellationToken cancellationToken)
    {
        var plan = BuildMapDeviceLoadPlan(automation);
        EnsureMapDeviceContainsOnlyRequestedItems(plan);
        await LoadMapDevicePlanAsync(plan, automation, cancellationToken);

        if (!await WaitForRequestedMapDeviceItemsAsync(plan.RequestedItems, plan.ConfiguredInventoryTotals))
        {
            throw new InvalidOperationException("Timed out verifying the Map Device contents.");
        }

        ValidateConfiguredMapDeviceInventoryTotalsAfterLoad(plan.ConfiguredInventoryTotals, plan.RequestedItems);
    }

    private async Task RunMapDeviceBodyAsync(StashAutomationSettings automation, CancellationToken cancellationToken = default)
    {
        await MapDeviceWorkflow.RunBodyAsync(automation, cancellationToken);
    }

    private static (string Label, string IdSuffix, StashAutomationTargetSettings Target) GetConfiguredMapSlotTarget(StashAutomationSettings automation)
    {
        return GetAutomationTargets(automation).First();
    }

    private static IEnumerable<(string Label, string IdSuffix, StashAutomationTargetSettings Target)> GetConfiguredFragmentSlotTargets(StashAutomationSettings automation)
    {
        return GetAutomationTargets(automation).Skip(1).Take(MapDeviceFragmentSlotCount);
    }

    private static void ValidateConfiguredMapDeviceSlotAssignment(string label, StashAutomationTargetSettings target, bool requiresMap)
    {
        var isMapTarget = IsMapDeviceMapTarget(target);
        if (requiresMap && !isMapTarget)
        {
            throw new InvalidOperationException($"{label} must be configured with a map target like 'Map (Tier 16)'.");
        }

        if (!requiresMap && isMapTarget)
        {
            throw new InvalidOperationException($"{label} must be configured with a fragment or scarab item, not a map.");
        }
    }

    private async Task RunMapDeviceAutomationFromHotkeyAsync()
    {
        if (!TryGetStashAutomation(out var automation))
        {
            return;
        }

        await RunQueuedAutomationAsync(
            ct => RunMapDeviceBodyAsync(automation, ct),
            "Map device load",
            cancelledStatus: "Map device load cancelled.",
            uiCleanupOptions: new AutomationUiCleanupOptions(KeepInventory: true, KeepAtlas: true, KeepMapDeviceWindow: true));
    }

    private void MoveCursorToMapDeviceActivateButton()
    {
        var activateButton = GameController?.IngameState?.IngameUi?.MapDeviceWindow?.ActivateButton;
        if (activateButton?.IsVisible != true)
        {
            throw new InvalidOperationException("Map Device Activate button is not visible.");
        }

        SetAutomationCursorPosition(activateButton.GetClientRect().Center);
    }

    #endregion
}

