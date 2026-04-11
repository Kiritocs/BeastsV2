using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeastsV2.Runtime.Automation;

internal sealed record MapDeviceVerificationCallbacks(
    Func<bool> HasAnyVisibleMapDeviceItems,
    Func<int, MapDeviceVisibleSlotState> GetVisibleSlotState,
    Func<IReadOnlyList<MapDeviceVisibleSlotState>> GetVisibleSlotStates,
    Func<string, int?> TryGetVisiblePlayerInventoryMatchingQuantity,
    Func<string, int> GetVisibleMapDeviceMatchingQuantity,
    Func<string, int> GetVisibleLoadedMapDeviceMatchingQuantity,
    Func<string, int> GetVisibleMapDeviceStorageMatchingQuantity,
    Func<IReadOnlyDictionary<string, int>> GetVisibleMapDeviceQuantities,
    Func<IReadOnlyList<string>> GetVisibleMapDeviceItemMetadata,
    Func<Func<bool>, int, int, Task<bool>> WaitForConditionAsync,
    Func<int> GetFastPollDelayMs,
    int FragmentSlotCount,
    int TransferTimeoutMs,
    Action<string> LogDebug);

internal sealed class MapDeviceVerificationService
{
    private readonly MapDeviceVerificationCallbacks _callbacks;

    public MapDeviceVerificationService(MapDeviceVerificationCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void EnsureContainsOnlyRequestedItems(MapDeviceLoadPlan plan)
    {
        if (plan == null || !_callbacks.HasAnyVisibleMapDeviceItems())
        {
            return;
        }

        if (!DoesContainOnlyRequestedItems(plan.RequestedItems, plan.ConfiguredInventoryTotals))
        {
            throw new InvalidOperationException("Map Device already contains unexpected items. Clear it before running this hotkey.");
        }
    }

    public void ValidateConfiguredInventoryTotalsBeforeLoad(
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        if (configuredInventoryTotals == null)
        {
            return;
        }

        var requestedMapMetadata = requestedItems?
            .Where(item => item.IsMap && !string.IsNullOrWhiteSpace(item.Metadata))
            .Select(item => item.Metadata)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (metadata, configured) in configuredInventoryTotals)
        {
            var actualInventoryQuantity = _callbacks.TryGetVisiblePlayerInventoryMatchingQuantity(metadata) ?? 0;
            var actualLoadedQuantity = _callbacks.GetVisibleLoadedMapDeviceMatchingQuantity(metadata);
            var actualStorageQuantity = requestedMapMetadata.Contains(metadata)
                ? _callbacks.GetVisibleMapDeviceStorageMatchingQuantity(metadata)
                : 0;
            var actualCombinedQuantity = actualInventoryQuantity + actualLoadedQuantity + actualStorageQuantity;
            if (actualCombinedQuantity < configured.ExpectedQuantity)
            {
                throw new InvalidOperationException(
                    $"Inventory quantity mismatch for {configured.Label}. Expected at least total {configured.ExpectedQuantity} of {metadata}, found combined visible quantity {actualCombinedQuantity} (inventory={actualInventoryQuantity}, map device={actualLoadedQuantity}, storage={actualStorageQuantity}).");
            }
        }
    }

    public void ValidateConfiguredInventoryTotalsAfterLoad(
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        if (configuredInventoryTotals == null || requestedItems == null)
        {
            return;
        }

        foreach (var requestedItem in requestedItems)
        {
            if (requestedItem == null || string.IsNullOrWhiteSpace(requestedItem.Metadata))
            {
                continue;
            }

            var actualRemainingInventory = _callbacks.TryGetVisiblePlayerInventoryMatchingQuantity(requestedItem.Metadata) ?? 0;

            if (requestedItem.IsMap)
            {
                var loadedMap = _callbacks.GetVisibleSlotState(requestedItem.SlotIndex);
                var configuredTotalQuantity = GetExpectedMapDeviceQuantity(requestedItem.Metadata, configuredInventoryTotals, fallbackQuantity: 1);
                var actualMapQuantity = _callbacks.GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata);
                if (!string.Equals(loadedMap?.Metadata, requestedItem.Metadata, StringComparison.OrdinalIgnoreCase) ||
                    actualMapQuantity != configuredTotalQuantity)
                {
                    throw new InvalidOperationException(
                        $"Post-load quantity mismatch for {requestedItem.Label} ({requestedItem.Metadata}). " +
                        $"Expected active map slot plus storage total x{configuredTotalQuantity}, found x{actualMapQuantity}. " +
                        $"Active slot contains '{loadedMap?.Metadata ?? "empty"}'. " +
                        $"Visible inventory qty={actualRemainingInventory}.");
                }

                continue;
            }

            var slotItem = _callbacks.GetVisibleSlotState(requestedItem.SlotIndex);
            var slotQuantity = slotItem == null || string.IsNullOrWhiteSpace(slotItem.Metadata) ? 0 : slotItem.Quantity;
            if (!string.Equals(slotItem?.Metadata, requestedItem.Metadata, StringComparison.OrdinalIgnoreCase) ||
                slotQuantity != requestedItem.ExpectedQuantity)
            {
                throw new InvalidOperationException(
                    $"Post-load slot mismatch for {requestedItem.Label} ({requestedItem.Metadata}). " +
                    $"Expected slot x{requestedItem.ExpectedQuantity}, found x{slotQuantity}. Visible inventory qty={actualRemainingInventory}.");
            }
        }
    }

    public async Task<bool> WaitForRequestedItemsAsync(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals)
    {
        if (DoesMatchRequestedItems(requestedItems, configuredInventoryTotals, logMismatch: false))
        {
            return true;
        }

        var matched = await _callbacks.WaitForConditionAsync(
            () => DoesMatchRequestedItems(requestedItems, configuredInventoryTotals, logMismatch: false),
            _callbacks.TransferTimeoutMs,
            Math.Max(10, _callbacks.GetFastPollDelayMs()));

        if (!matched)
        {
            DoesMatchRequestedItems(requestedItems, configuredInventoryTotals, logMismatch: true);
        }

        return matched;
    }

    public bool DoesMatchRequestedItems(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        bool logMismatch = true)
    {
        if (requestedItems == null)
        {
            return false;
        }

        var requestedSlots = requestedItems.ToDictionary(item => item.SlotIndex);
        for (var slotIndex = 0; slotIndex <= _callbacks.FragmentSlotCount; slotIndex++)
        {
            var mismatchReason = GetSlotMismatchReason(
                requestedSlots,
                slotIndex,
                string.Empty,
                allowMissingRequestedItem: false,
                allowUnderfilledRequestedQuantity: false);
            if (mismatchReason != null)
            {
                if (logMismatch)
                {
                    LogVerificationMismatch(requestedItems, _callbacks.GetVisibleMapDeviceQuantities(), configuredInventoryTotals, mismatchReason);
                }

                return false;
            }
        }

        foreach (var requestedItem in requestedItems.Where(item => item.IsMap))
        {
            var expectedMapQuantity = GetExpectedMapDeviceQuantity(requestedItem.Metadata, configuredInventoryTotals, fallbackQuantity: 1);
            var detectedQuantity = _callbacks.GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata);
            if (detectedQuantity != expectedMapQuantity)
            {
                if (logMismatch)
                {
                    LogVerificationMismatch(requestedItems, _callbacks.GetVisibleMapDeviceQuantities(), configuredInventoryTotals, $"map-total:{requestedItem.Metadata}");
                }

                return false;
            }
        }

        return true;
    }

    public bool DoesContainOnlyRequestedItems(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals)
    {
        if (requestedItems == null)
        {
            return false;
        }

        var requestedSlots = requestedItems.ToDictionary(item => item.SlotIndex);
        for (var slotIndex = 0; slotIndex <= _callbacks.FragmentSlotCount; slotIndex++)
        {
            var mismatchReason = GetSlotMismatchReason(
                requestedSlots,
                slotIndex,
                "subset-",
                allowMissingRequestedItem: true,
                allowUnderfilledRequestedQuantity: true);
            if (mismatchReason != null)
            {
                LogVerificationMismatch(requestedItems, _callbacks.GetVisibleMapDeviceQuantities(), configuredInventoryTotals, mismatchReason);
                return false;
            }
        }

        return true;
    }

    private string GetSlotMismatchReason(
        IReadOnlyDictionary<int, MapDeviceRequestedSlot> requestedSlots,
        int slotIndex,
        string reasonPrefix,
        bool allowMissingRequestedItem,
        bool allowUnderfilledRequestedQuantity)
    {
        var actualItem = _callbacks.GetVisibleSlotState(slotIndex);
        if (!requestedSlots.TryGetValue(slotIndex, out var requestedItem))
        {
            return !string.IsNullOrWhiteSpace(actualItem?.Metadata) ? $"{reasonPrefix}unexpected-slot:{slotIndex}" : null;
        }

        if (string.IsNullOrWhiteSpace(actualItem?.Metadata))
        {
            return allowMissingRequestedItem ? null : $"{reasonPrefix}missing-slot:{slotIndex}";
        }

        if (!string.Equals(actualItem.Metadata, requestedItem.Metadata, StringComparison.OrdinalIgnoreCase))
        {
            return $"{reasonPrefix}slot-metadata:{slotIndex}";
        }

        if (requestedItem.IsMap)
        {
            return null;
        }

        var quantityMatches = allowUnderfilledRequestedQuantity
            ? actualItem.Quantity <= requestedItem.ExpectedQuantity
            : actualItem.Quantity == requestedItem.ExpectedQuantity;

        return quantityMatches ? null : $"{reasonPrefix}slot-quantity:{slotIndex}";
    }

    private void LogVerificationMismatch(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        IReadOnlyDictionary<string, int> detectedQuantities,
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        string reason)
    {
        var requestedLabels = BuildRequestedLabels(requestedItems);
        var expectedQuantities = BuildExpectedQuantities(configuredInventoryTotals, requestedItems);
        var safeDetectedQuantities = detectedQuantities != null
            ? new Dictionary<string, int>(detectedQuantities, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var missingCounts = BuildMissingMetadataCounts(expectedQuantities, safeDetectedQuantities);
        var extraCounts = BuildExtraMetadataCounts(expectedQuantities, safeDetectedQuantities);
        var inventoryQuantities = BuildVisibleInventoryQuantities(expectedQuantities.Keys.Concat(safeDetectedQuantities.Keys));

        _callbacks.LogDebug(
            "Map device verification mismatch." + Environment.NewLine +
            $"Reason: {reason}" + Environment.NewLine +
            $"Missing item(s): {DescribeMetadataCounts(missingCounts, requestedLabels, inventoryQuantities)}" + Environment.NewLine +
            $"Extra item(s): {DescribeMetadataCounts(extraCounts, requestedLabels, inventoryQuantities)}" + Environment.NewLine +
            $"Expected Map Device quantities: {DescribeMetadataCounts(expectedQuantities, requestedLabels, inventoryQuantities)}" + Environment.NewLine +
            $"Detected Map Device quantities: {DescribeMetadataCounts(safeDetectedQuantities, requestedLabels, inventoryQuantities)}" + Environment.NewLine +
            $"Requested slots: {DescribeRequestedSlots(requestedItems)}" + Environment.NewLine +
            $"Detected slots: {DescribeDetectedSlots()}" + Environment.NewLine +
            $"Requested items: {DescribeRequestedItems(requestedItems)}" + Environment.NewLine +
            $"Detected items: {DescribeMetadataList(_callbacks.GetVisibleMapDeviceItemMetadata())}");
    }

    private Dictionary<string, int> BuildVisibleInventoryQuantities(IEnumerable<string> metadataKeys)
    {
        var quantities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (metadataKeys == null)
        {
            return quantities;
        }

        foreach (var metadata in metadataKeys)
        {
            if (string.IsNullOrWhiteSpace(metadata) || quantities.ContainsKey(metadata))
            {
                continue;
            }

            quantities[metadata] = _callbacks.TryGetVisiblePlayerInventoryMatchingQuantity(metadata) ?? 0;
        }

        return quantities;
    }

    private string DescribeDetectedSlots()
    {
        var slotItems = _callbacks.GetVisibleSlotStates();
        if (slotItems == null || slotItems.Count <= 0)
        {
            return "none";
        }

        return string.Join(", ", slotItems.Select(item => string.IsNullOrWhiteSpace(item?.Metadata)
            ? $"Slot {item.SlotIndex + 1}:empty"
            : $"Slot {item.SlotIndex + 1}:{item.Metadata}:x{item.Quantity}"));
    }

    private static Dictionary<string, string> BuildRequestedLabels(IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (requestedItems == null)
        {
            return labels;
        }

        foreach (var item in requestedItems)
        {
            if (string.IsNullOrWhiteSpace(item.Metadata) || string.IsNullOrWhiteSpace(item.Label) || labels.ContainsKey(item.Metadata))
            {
                continue;
            }

            labels[item.Metadata] = item.Label;
        }

        return labels;
    }

    private static string DescribeRequestedSlots(IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        if (requestedItems == null || requestedItems.Count <= 0)
        {
            return "none";
        }

        return string.Join(", ", requestedItems
            .OrderBy(item => item.SlotIndex)
            .Select(item => $"Slot {item.SlotIndex + 1}:{item.Label}:{item.Metadata}:x{item.ExpectedQuantity}"));
    }

    private static Dictionary<string, int> BuildExpectedQuantities(
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        var quantities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (requestedItems == null)
        {
            return quantities;
        }

        foreach (var requestedItem in requestedItems)
        {
            if (string.IsNullOrWhiteSpace(requestedItem.Metadata))
            {
                continue;
            }

            if (requestedItem.IsMap)
            {
                if (configuredInventoryTotals != null && configuredInventoryTotals.TryGetValue(requestedItem.Metadata, out var configuredMapQuantity))
                {
                    quantities[requestedItem.Metadata] = Math.Max(1, configuredMapQuantity.ExpectedQuantity);
                }
                else
                {
                    quantities[requestedItem.Metadata] = 1;
                }

                continue;
            }

            if (configuredInventoryTotals != null && configuredInventoryTotals.TryGetValue(requestedItem.Metadata, out var configured))
            {
                quantities[requestedItem.Metadata] = configured.ExpectedQuantity;
                continue;
            }

            quantities[requestedItem.Metadata] = 1;
        }

        return quantities;
    }

    private static Dictionary<string, int> BuildMissingMetadataCounts(IReadOnlyDictionary<string, int> requestedCounts, IReadOnlyDictionary<string, int> detectedCounts)
    {
        var missingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (requestedCounts == null)
        {
            return missingCounts;
        }

        foreach (var (metadata, requestedCount) in requestedCounts)
        {
            var detectedCount = detectedCounts != null && detectedCounts.TryGetValue(metadata, out var foundCount) ? foundCount : 0;
            var missingCount = requestedCount - detectedCount;
            if (missingCount > 0)
            {
                missingCounts[metadata] = missingCount;
            }
        }

        return missingCounts;
    }

    private static Dictionary<string, int> BuildExtraMetadataCounts(IReadOnlyDictionary<string, int> requestedCounts, IReadOnlyDictionary<string, int> detectedCounts)
    {
        var extraCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (detectedCounts == null)
        {
            return extraCounts;
        }

        foreach (var (metadata, detectedCount) in detectedCounts)
        {
            var requestedCount = requestedCounts != null && requestedCounts.TryGetValue(metadata, out var expectedCount) ? expectedCount : 0;
            var extraCount = detectedCount - requestedCount;
            if (extraCount > 0)
            {
                extraCounts[metadata] = extraCount;
            }
        }

        return extraCounts;
    }

    private static string DescribeRequestedItems(IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        if (requestedItems == null || requestedItems.Count <= 0)
        {
            return string.Empty;
        }

        return string.Join(", ", requestedItems.Select(item => $"Slot {item.SlotIndex + 1}:{item.Label}:{item.Metadata}:x{item.ExpectedQuantity}"));
    }

    private static string DescribeMetadataList(IReadOnlyList<string> metadataItems)
    {
        if (metadataItems == null || metadataItems.Count <= 0)
        {
            return string.Empty;
        }

        return string.Join(", ", metadataItems);
    }

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