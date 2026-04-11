using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;

namespace BeastsV2;

public partial class Main
{
    private MapDeviceRequestedSlot ResolveConfiguredMapDeviceRequestedSlot(
        IList<NormalInventoryItem> inventoryItems,
        IList<NormalInventoryItem> mapDeviceItems,
        IList<NormalInventoryItem> mapDeviceStorageItems,
        string label,
        int slotIndex,
        StashAutomationTargetSettings target,
        bool requiresMap)
    {
        var visibleItem = FindConfiguredVisibleItemForMapDeviceTarget(inventoryItems, mapDeviceItems, mapDeviceStorageItems, target);
        if (visibleItem?.Item != null)
        {
            return CreateConfiguredMapDeviceRequestedSlot(
                slotIndex,
                label,
                target,
                visibleItem.Item.Metadata,
                visibleItem.Item.GetComponent<MapKey>() != null,
                requiresMap);
        }

        if (!TryResolveConfiguredMapDeviceTargetFromState(target, out var metadata, out var isMap))
        {
            return null;
        }

        return CreateConfiguredMapDeviceRequestedSlot(slotIndex, label, target, metadata, isMap, requiresMap);
    }

    private static MapDeviceRequestedSlot CreateConfiguredMapDeviceRequestedSlot(
        int slotIndex,
        string label,
        StashAutomationTargetSettings target,
        string metadata,
        bool isMap,
        bool requiresMap)
    {
        if (requiresMap && !isMap)
        {
            throw new InvalidOperationException($"{label} resolved to a non-map item. Configure Slot 1 with a map target.");
        }

        if (!requiresMap && isMap)
        {
            throw new InvalidOperationException($"{label} resolved to a map item. Fragment slots must use fragments or scarabs only.");
        }

        return string.IsNullOrWhiteSpace(metadata)
            ? null
            : new MapDeviceRequestedSlot(
                slotIndex,
                label,
                metadata,
                isMap,
                isMap ? 1 : Math.Max(1, GetConfiguredTargetQuantity(target)));
    }

    private List<MapDeviceRequestedSlot> ResolveConfiguredMapDeviceRequestedSlots(StashAutomationSettings automation)
    {
        var inventoryItems = GetVisiblePlayerInventoryItemsOrThrow("Player inventory is not visible.");
        var mapDeviceItems = GetVisibleMapDeviceItems();
        var mapDeviceStorageItems = GetVisibleMapDeviceStorageItems();

        var requestedItems = new List<MapDeviceRequestedSlot>();
        var missingTargets = new List<string>();

        var mapSlotTarget = GetConfiguredMapSlotTarget(automation);
        if (!IsTargetEnabledForAutomation(mapSlotTarget.Target))
        {
            throw new InvalidOperationException("Slot 1 - Map Slot must be enabled and have a quantity greater than 0.");
        }

        ValidateConfiguredMapDeviceSlotAssignment(mapSlotTarget.Label, mapSlotTarget.Target, requiresMap: true);

        var mapRequestedSlot = ResolveConfiguredMapDeviceRequestedSlot(
            inventoryItems,
            mapDeviceItems,
            mapDeviceStorageItems,
            mapSlotTarget.Label,
            0,
            mapSlotTarget.Target,
            requiresMap: true);
        if (mapRequestedSlot == null)
        {
            missingTargets.Add(mapSlotTarget.Label);
        }
        else
        {
            requestedItems.Add(mapRequestedSlot);
        }

        var encounteredEmptyFragmentSlot = false;
        var fragmentSlotIndex = 1;
        foreach (var (label, _, target) in GetConfiguredFragmentSlotTargets(automation))
        {
            if (!IsTargetEnabledForAutomation(target))
            {
                encounteredEmptyFragmentSlot = true;
                fragmentSlotIndex++;
                continue;
            }

            if (encounteredEmptyFragmentSlot)
            {
                throw new InvalidOperationException("Configured fragment slots must be filled from Slot 2 onward with no gaps. Leave only trailing fragment slots disabled or set to quantity 0.");
            }

            ValidateConfiguredMapDeviceSlotAssignment(label, target, requiresMap: false);

            var requestedSlot = ResolveConfiguredMapDeviceRequestedSlot(
                inventoryItems,
                mapDeviceItems,
                mapDeviceStorageItems,
                label,
                fragmentSlotIndex,
                target,
                requiresMap: false);
            if (requestedSlot == null)
            {
                missingTargets.Add(label);
            }
            else
            {
                requestedItems.Add(requestedSlot);
            }

            fragmentSlotIndex++;
        }

        if (missingTargets.Count > 0)
        {
            throw new InvalidOperationException($"Missing required inventory item(s): {string.Join(", ", missingTargets)}.");
        }

        if (requestedItems.Count <= 0)
        {
            throw new InvalidOperationException("No enabled map-device targets were found in player inventory or the Map Device.");
        }

        return requestedItems;
    }

    private Dictionary<string, (string Label, int ExpectedQuantity)> ResolveConfiguredMapDeviceInventoryTotals(
        StashAutomationSettings automation,
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        var configuredTotals = new Dictionary<string, (string Label, int ExpectedQuantity)>(StringComparer.OrdinalIgnoreCase);

        if (requestedItems == null)
        {
            return configuredTotals;
        }

        var configuredMapReserveQuantity = GetConfiguredTargetQuantity(GetConfiguredMapSlotTarget(automation).Target);

        foreach (var requestedItem in requestedItems)
        {
            if (string.IsNullOrWhiteSpace(requestedItem?.Metadata))
            {
                continue;
            }

            var expectedQuantity = requestedItem.IsMap
                ? Math.Max(1, configuredMapReserveQuantity)
                : requestedItem.ExpectedQuantity;

            configuredTotals[requestedItem.Metadata] = configuredTotals.TryGetValue(requestedItem.Metadata, out var existing)
                ? ($"{existing.Label} / {requestedItem.Label}", existing.ExpectedQuantity + expectedQuantity)
                : (requestedItem.Label, expectedQuantity);
        }

        return configuredTotals;
    }

    private void ValidateConfiguredMapDeviceInventoryTotalsBeforeLoad(
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems) =>
        MapDeviceVerification.ValidateConfiguredInventoryTotalsBeforeLoad(configuredInventoryTotals, requestedItems);

    private NormalInventoryItem FindConfiguredVisibleItemForMapDeviceTarget(
        IList<NormalInventoryItem> inventoryItems,
        IList<NormalInventoryItem> mapDeviceItems,
        IList<NormalInventoryItem> mapDeviceStorageItems,
        StashAutomationTargetSettings target)
    {
        return FindInventoryItemForMapDeviceTarget(inventoryItems, target)
               ?? FindInventoryItemForMapDeviceTarget(mapDeviceItems, target)
               ?? (IsMapDeviceMapTarget(target) ? FindInventoryItemForMapDeviceTarget(mapDeviceStorageItems, target) : null);
    }

    private static bool IsMapDeviceMapTarget(StashAutomationTargetSettings target)
    {
        return TryGetConfiguredMapTier(target).HasValue;
    }

    private bool TryResolveConfiguredMapDeviceTargetFromState(StashAutomationTargetSettings target, out string metadata, out bool isMap)
    {
        metadata = null;
        isMap = false;

        if (target == null || !CanReadMapDeviceWindowState())
        {
            return false;
        }

        foreach (var item in GetVisibleMapDeviceItems())
        {
            if (TryResolveConfiguredMapDeviceTargetFromEntity(item?.Item, target, out metadata, out isMap))
            {
                return true;
            }
        }

        if (!IsMapDeviceMapTarget(target))
        {
            return false;
        }

        foreach (var item in GetVisibleMapDeviceStorageItems())
        {
            if (TryResolveConfiguredMapDeviceTargetFromEntity(item?.Item, target, out metadata, out isMap))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryResolveConfiguredMapDeviceTargetFromEntity(
        Entity entity,
        StashAutomationTargetSettings target,
        out string metadata,
        out bool isMap)
    {
        metadata = null;
        isMap = false;

        if (entity == null || target == null)
        {
            return false;
        }

        var configuredMapTier = TryGetConfiguredMapTier(target);

        if (configuredMapTier.HasValue)
        {
            var mapTier = entity.GetComponent<MapKey>()?.Tier;
            if (mapTier == configuredMapTier.Value)
            {
                metadata = entity.Metadata;
                isMap = true;
                return !string.IsNullOrWhiteSpace(metadata);
            }

            return false;
        }

        var configuredName = target.ItemName.Value?.Trim();
        var entityName = TryGetMapDeviceItemName(entity);
        if (entityName.EqualsIgnoreCase(configuredName))
        {
            metadata = entity.Metadata;
            return !string.IsNullOrWhiteSpace(metadata);
        }

        return false;
    }

}