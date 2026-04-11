using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BeastsV2;

public partial class Main
{
    private MapDeviceLoadPlan BuildMapDeviceLoadPlan(StashAutomationSettings automation) => MapDeviceLoadPlans.BuildLoadPlan(automation);

    private bool IsRequestedItemCurrentlyLoadedInExpectedSlot(MapDeviceRequestedSlot requestedItem)
    {
        if (requestedItem == null || string.IsNullOrWhiteSpace(requestedItem.Metadata))
        {
            return false;
        }

        var slotItem = GetVisibleMapDeviceItemInSlot(requestedItem.SlotIndex);
        return slotItem?.Item?.Metadata.EqualsIgnoreCase(requestedItem.Metadata) == true;
    }

    private int? GetCurrentMapDeviceRequestedItemQuantity(MapDeviceRequestedSlot requestedItem)
    {
        if (requestedItem == null || string.IsNullOrWhiteSpace(requestedItem.Metadata))
        {
            return null;
        }

        if (requestedItem.IsMap)
        {
            return GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata);
        }

        var slotItem = GetVisibleMapDeviceItemInSlot(requestedItem.SlotIndex);
        if (slotItem?.Item == null)
        {
            return 0;
        }

        return slotItem.Item.Metadata.EqualsIgnoreCase(requestedItem.Metadata)
            ? GetVisibleInventoryItemStackQuantity(slotItem)
            : null;
    }

    private int GetVisibleCombinedRequestedItemQuantity(string metadata, bool includeStorage)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        var inventoryQuantity = TryGetVisiblePlayerInventoryMatchingQuantity(metadata) ?? 0;
        var loadedQuantity = GetVisibleLoadedMapDeviceMatchingQuantity(metadata);
        var storageQuantity = includeStorage ? GetVisibleMapDeviceStorageMatchingQuantity(metadata) : 0;
        return inventoryQuantity + loadedQuantity + storageQuantity;
    }

    private void EnsureMapDeviceContainsOnlyRequestedItems(MapDeviceLoadPlan plan) => MapDeviceVerification.EnsureContainsOnlyRequestedItems(plan);

    private async Task LoadMapDevicePlanAsync(
        MapDeviceLoadPlan plan,
        StashAutomationSettings automation,
        CancellationToken cancellationToken)
    {
        await EnsureRequestedMapLoadedAsync(plan.MapSlot);

        if (DoesMapDeviceMatchRequestedItems(plan.RequestedItems, plan.ConfiguredInventoryTotals, logMismatch: false))
        {
            return;
        }

        ValidateConfiguredMapDeviceInventoryTotalsBeforeLoad(plan.ConfiguredInventoryTotals, plan.RequestedItems);

        foreach (var requestedItem in plan.RequestedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadRequestedMapDeviceItemAsync(requestedItem, plan.ConfiguredInventoryTotals);
        }
    }

    private async Task LoadRequestedMapDeviceItemAsync(
        MapDeviceRequestedSlot requestedItem,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals)
    {
        if (requestedItem.IsMap)
        {
            await EnsureRequestedMapLoadedAsync(requestedItem);
            await EnsureConfiguredMapReserveStoredAsync(requestedItem, configuredInventoryTotals);
            return;
        }

        await EnsureRequestedFragmentSlotLoadedAsync(requestedItem);
    }

    private async Task EnsureConfiguredMapReserveStoredAsync(
        MapDeviceRequestedSlot requestedMap,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals)
    {
        var expectedQuantity = GetExpectedMapDeviceQuantity(requestedMap.Metadata, configuredInventoryTotals, fallbackQuantity: 1);
        if (GetVisibleMapDeviceMatchingQuantity(requestedMap.Metadata) >= expectedQuantity)
        {
            return;
        }

        await CtrlClickInventoryItemIntoMapDeviceAsync(
            (requestedMap.Label, requestedMap.Metadata, true),
            1,
            1,
            expectedQuantity);
    }

    private static int GetExpectedMapDeviceQuantity(
        string metadata,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        int fallbackQuantity)
    {
        if (!string.IsNullOrWhiteSpace(metadata) &&
            configuredInventoryTotals != null &&
            configuredInventoryTotals.TryGetValue(metadata, out var configured))
        {
            return Math.Max(1, configured.ExpectedQuantity);
        }

        return Math.Max(1, fallbackQuantity);
    }
}