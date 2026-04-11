using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeastsV2;

public partial class Main
{
    private void ValidateConfiguredMapDeviceInventoryTotalsAfterLoad(
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems) =>
        MapDeviceVerification.ValidateConfiguredInventoryTotalsAfterLoad(configuredInventoryTotals, requestedItems);

    private async Task<bool> WaitForRequestedMapDeviceItemsAsync(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals) =>
        await MapDeviceVerification.WaitForRequestedItemsAsync(requestedItems, configuredInventoryTotals);

    private bool DoesMapDeviceMatchRequestedItems(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        bool logMismatch = true) =>
        MapDeviceVerification.DoesMatchRequestedItems(requestedItems, configuredInventoryTotals, logMismatch);

    private bool DoesMapDeviceContainOnlyRequestedItems(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals) =>
        MapDeviceVerification.DoesContainOnlyRequestedItems(requestedItems, configuredInventoryTotals);

    private static string DescribeMetadataCounts(
        IReadOnlyDictionary<string, int> metadataCounts,
        IReadOnlyDictionary<string, string> metadataLabels = null,
        IReadOnlyDictionary<string, int> inventoryQuantities = null)
    {
        if (metadataCounts == null || metadataCounts.Count <= 0)
        {
            return "none";
        }

        return string.Join(", ", metadataCounts.Select(x =>
        {
            var labelText = metadataLabels != null && metadataLabels.TryGetValue(x.Key, out var label) && !string.IsNullOrWhiteSpace(label)
                ? $"{label} ({x.Key})"
                : x.Key;
            var quantityText = inventoryQuantities != null && inventoryQuantities.TryGetValue(x.Key, out var quantity)
                ? $", qty={quantity}"
                : string.Empty;
            return $"{labelText} x{x.Value}{quantityText}";
        }));
    }
}