using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2.Runtime.Automation;

internal sealed record MapDeviceLoadPlanCallbacks(
    Func<StashAutomationSettings, List<MapDeviceRequestedSlot>> ResolveConfiguredRequestedSlots,
    Func<StashAutomationSettings, IReadOnlyList<MapDeviceRequestedSlot>, Dictionary<string, (string Label, int ExpectedQuantity)>> ResolveConfiguredInventoryTotals,
    Func<MapDeviceRequestedSlot, int?> GetCurrentRequestedItemQuantity,
    Func<string, bool, int> GetVisibleCombinedRequestedItemQuantity,
    Func<MapDeviceRequestedSlot, bool> IsRequestedItemCurrentlyLoadedInExpectedSlot,
    Action<string> LogDebug);

internal sealed class MapDeviceLoadPlanService
{
    private readonly MapDeviceLoadPlanCallbacks _callbacks;

    public MapDeviceLoadPlanService(MapDeviceLoadPlanCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public MapDeviceLoadPlan BuildLoadPlan(StashAutomationSettings automation)
    {
        var requestedItems = _callbacks.ResolveConfiguredRequestedSlots(automation);
        ValidateRequestedMapCount(requestedItems);

        var basePlan = new MapDeviceLoadPlan(
            requestedItems,
            _callbacks.ResolveConfiguredInventoryTotals(automation, requestedItems),
            requestedItems.First(item => item.IsMap));

        return ApplySharedConsumedRunDeficit(basePlan);
    }

    private MapDeviceLoadPlan ApplySharedConsumedRunDeficit(MapDeviceLoadPlan plan)
    {
        var observation = TryCaptureCurrentStateObservation(plan);
        var sharedDeficit = TryResolveSharedConsumedRunDeficit(plan, observation);
        if (!sharedDeficit.HasValue || sharedDeficit.Value <= 0)
        {
            ThrowIfStateIsPartiallyLoadedButNotUniform(plan, observation);
            return plan;
        }

        var adjustedMapTotal = GetExpectedMapDeviceQuantity(plan.MapSlot?.Metadata, plan.ConfiguredInventoryTotals, fallbackQuantity: 1) - sharedDeficit.Value;
        if (adjustedMapTotal <= 0)
        {
            return plan;
        }

        var adjustedRequestedItems = new List<MapDeviceRequestedSlot>(plan.RequestedItems.Count);
        foreach (var requestedItem in plan.RequestedItems)
        {
            if (requestedItem.IsMap)
            {
                adjustedRequestedItems.Add(requestedItem);
                continue;
            }

            var adjustedSlotQuantity = requestedItem.ExpectedQuantity - sharedDeficit.Value;
            if (adjustedSlotQuantity <= 0)
            {
                return plan;
            }

            adjustedRequestedItems.Add(requestedItem with { ExpectedQuantity = adjustedSlotQuantity });
        }

        _callbacks.LogDebug(
            $"Applying shared Map Device consumed-run deficit. deficit={sharedDeficit.Value}, " +
            $"mapTotal={adjustedMapTotal}, " +
            $"slots={string.Join(", ", adjustedRequestedItems.Where(item => !item.IsMap).OrderBy(item => item.SlotIndex).Select(item => $"Slot {item.SlotIndex + 1}=x{item.ExpectedQuantity}"))}, " +
            $"observedSlots={DescribeObservedSlotQuantities(plan.RequestedItems, observation)}, " +
            $"observedCombined={DescribeObservedCombinedQuantities(plan.ConfiguredInventoryTotals, observation)}");

        return new MapDeviceLoadPlan(
            adjustedRequestedItems,
            BuildConfiguredInventoryTotalsForSharedDeficit(adjustedRequestedItems, adjustedMapTotal),
            adjustedRequestedItems.First(item => item.IsMap));
    }

    private MapDeviceCurrentStateObservation TryCaptureCurrentStateObservation(MapDeviceLoadPlan plan)
    {
        if (plan?.RequestedItems == null || plan.RequestedItems.Count <= 0)
        {
            return null;
        }

        var slotQuantities = new Dictionary<int, int>();
        foreach (var requestedItem in plan.RequestedItems.OrderBy(item => item.SlotIndex))
        {
            var currentQuantity = _callbacks.GetCurrentRequestedItemQuantity(requestedItem);
            if (!currentQuantity.HasValue)
            {
                return null;
            }

            slotQuantities[requestedItem.SlotIndex] = currentQuantity.Value;
        }

        var combinedQuantities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedGroup in plan.RequestedItems
                     .Where(item => !string.IsNullOrWhiteSpace(item?.Metadata))
                     .GroupBy(item => item.Metadata, StringComparer.OrdinalIgnoreCase))
        {
            var includeStorage = requestedGroup.Any(item => item.IsMap);
            combinedQuantities[requestedGroup.Key] = _callbacks.GetVisibleCombinedRequestedItemQuantity(requestedGroup.Key, includeStorage);
        }

        return new MapDeviceCurrentStateObservation(slotQuantities, combinedQuantities);
    }

    private int? TryResolveSharedConsumedRunDeficit(MapDeviceLoadPlan plan, MapDeviceCurrentStateObservation observation)
    {
        if (plan?.RequestedItems == null || plan.RequestedItems.Count <= 0 || observation?.SlotQuantities == null)
        {
            return null;
        }

        int? sharedDeficit = null;
        foreach (var requestedItem in plan.RequestedItems.OrderBy(item => item.SlotIndex))
        {
            var expectedQuantity = requestedItem.IsMap
                ? GetExpectedMapDeviceQuantity(requestedItem.Metadata, plan.ConfiguredInventoryTotals, fallbackQuantity: 1)
                : requestedItem.ExpectedQuantity;
            if (!observation.SlotQuantities.TryGetValue(requestedItem.SlotIndex, out var currentQuantity) || currentQuantity > expectedQuantity)
            {
                return null;
            }

            var currentDeficit = expectedQuantity - currentQuantity;
            if (currentDeficit <= 0)
            {
                return null;
            }

            if (!sharedDeficit.HasValue)
            {
                sharedDeficit = currentDeficit;
                continue;
            }

            if (sharedDeficit.Value != currentDeficit)
            {
                return null;
            }
        }

        if (!sharedDeficit.HasValue || !DoesSharedConsumedRunObservationMatchCombinedTotals(plan, observation, sharedDeficit.Value))
        {
            return null;
        }

        return sharedDeficit;
    }

    private void ThrowIfStateIsPartiallyLoadedButNotUniform(MapDeviceLoadPlan plan, MapDeviceCurrentStateObservation observation)
    {
        if (plan?.RequestedItems == null || plan.ConfiguredInventoryTotals == null || observation?.SlotQuantities == null)
        {
            return;
        }

        var hasLoadedRequestedSlot = false;
        var hasPositiveDeficit = false;
        int? referenceDeficit = null;
        var hasNonUniformDeficit = false;

        foreach (var requestedItem in plan.RequestedItems.OrderBy(item => item.SlotIndex))
        {
            if (!observation.SlotQuantities.TryGetValue(requestedItem.SlotIndex, out var currentQuantity))
            {
                return;
            }

            var expectedQuantity = requestedItem.IsMap
                ? GetExpectedMapDeviceQuantity(requestedItem.Metadata, plan.ConfiguredInventoryTotals, fallbackQuantity: 1)
                : requestedItem.ExpectedQuantity;
            if (currentQuantity > expectedQuantity)
            {
                return;
            }

            if (_callbacks.IsRequestedItemCurrentlyLoadedInExpectedSlot(requestedItem))
            {
                hasLoadedRequestedSlot = true;
            }

            var deficit = expectedQuantity - currentQuantity;
            if (deficit <= 0)
            {
                continue;
            }

            hasPositiveDeficit = true;
            if (!referenceDeficit.HasValue)
            {
                referenceDeficit = deficit;
                continue;
            }

            if (referenceDeficit.Value != deficit)
            {
                hasNonUniformDeficit = true;
            }
        }

        if (!hasLoadedRequestedSlot || !hasPositiveDeficit || !hasNonUniformDeficit)
        {
            return;
        }

        if (DoObservedCombinedTotalsSatisfyConfiguredTotals(plan, observation))
        {
            _callbacks.LogDebug(
                "Allowing partial Map Device reload because combined visible totals already satisfy the configured request. " +
                $"observedSlots={DescribeObservedSlotQuantities(plan.RequestedItems, observation)}, " +
                $"observedCombined={DescribeObservedCombinedQuantities(plan.ConfiguredInventoryTotals, observation)}");
            return;
        }

        throw new InvalidOperationException(
            "Map Device is partially loaded but does not match a uniform consumed-run deficit. " +
            $"observedSlots={DescribeObservedSlotQuantities(plan.RequestedItems, observation)}, " +
            $"observedCombined={DescribeObservedCombinedQuantities(plan.ConfiguredInventoryTotals, observation)}. " +
            "Clear the Map Device or restore the missing item(s) before loading again.");
    }

    private static bool DoObservedCombinedTotalsSatisfyConfiguredTotals(
        MapDeviceLoadPlan plan,
        MapDeviceCurrentStateObservation observation)
    {
        if (plan?.ConfiguredInventoryTotals == null || observation?.CombinedQuantities == null)
        {
            return false;
        }

        foreach (var (metadata, configured) in plan.ConfiguredInventoryTotals)
        {
            if (!observation.CombinedQuantities.TryGetValue(metadata, out var observedCombinedQuantity) ||
                observedCombinedQuantity < configured.ExpectedQuantity)
            {
                return false;
            }
        }

        return true;
    }

    private bool DoesSharedConsumedRunObservationMatchCombinedTotals(
        MapDeviceLoadPlan plan,
        MapDeviceCurrentStateObservation observation,
        int sharedDeficit)
    {
        if (plan?.RequestedItems == null || plan.ConfiguredInventoryTotals == null || observation?.CombinedQuantities == null)
        {
            return false;
        }

        foreach (var requestedGroup in plan.RequestedItems
                     .Where(item => !string.IsNullOrWhiteSpace(item?.Metadata))
                     .GroupBy(item => item.Metadata, StringComparer.OrdinalIgnoreCase))
        {
            if (!plan.ConfiguredInventoryTotals.TryGetValue(requestedGroup.Key, out var configured) ||
                !observation.CombinedQuantities.TryGetValue(requestedGroup.Key, out var observedCombinedQuantity))
            {
                return false;
            }

            var expectedCombinedQuantity = configured.ExpectedQuantity - (sharedDeficit * requestedGroup.Count());
            if (expectedCombinedQuantity <= 0 || observedCombinedQuantity != expectedCombinedQuantity)
            {
                _callbacks.LogDebug(
                    $"Rejecting shared Map Device consumed-run deficit candidate. deficit={sharedDeficit}, " +
                    $"metadata='{requestedGroup.Key}', expectedCombinedQty={expectedCombinedQuantity}, observedCombinedQty={observedCombinedQuantity}, " +
                    $"observedSlots={DescribeObservedSlotQuantities(plan.RequestedItems, observation)}, " +
                    $"observedCombined={DescribeObservedCombinedQuantities(plan.ConfiguredInventoryTotals, observation)}");
                return false;
            }
        }

        return true;
    }

    private static string DescribeObservedSlotQuantities(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        MapDeviceCurrentStateObservation observation)
    {
        if (requestedItems == null || observation?.SlotQuantities == null)
        {
            return "unavailable";
        }

        return string.Join(
            ", ",
            requestedItems
                .OrderBy(item => item.SlotIndex)
                .Select(item => observation.SlotQuantities.TryGetValue(item.SlotIndex, out var quantity)
                    ? $"Slot {item.SlotIndex + 1}=x{quantity}"
                    : $"Slot {item.SlotIndex + 1}=missing"));
    }

    private static string DescribeObservedCombinedQuantities(
        IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> configuredInventoryTotals,
        MapDeviceCurrentStateObservation observation)
    {
        if (configuredInventoryTotals == null || observation?.CombinedQuantities == null)
        {
            return "unavailable";
        }

        return string.Join(
            ", ",
            configuredInventoryTotals
                .OrderBy(entry => entry.Value.Label, StringComparer.OrdinalIgnoreCase)
                .Select(entry => observation.CombinedQuantities.TryGetValue(entry.Key, out var quantity)
                    ? $"{entry.Value.Label}=x{quantity}/{entry.Value.ExpectedQuantity}"
                    : $"{entry.Value.Label}=missing/{entry.Value.ExpectedQuantity}"));
    }

    private static Dictionary<string, (string Label, int ExpectedQuantity)> BuildConfiguredInventoryTotalsForSharedDeficit(
        IReadOnlyList<MapDeviceRequestedSlot> requestedItems,
        int adjustedMapTotal)
    {
        var configuredTotals = new Dictionary<string, (string Label, int ExpectedQuantity)>(StringComparer.OrdinalIgnoreCase);
        if (requestedItems == null)
        {
            return configuredTotals;
        }

        foreach (var requestedItem in requestedItems)
        {
            if (requestedItem == null || string.IsNullOrWhiteSpace(requestedItem.Metadata))
            {
                continue;
            }

            var expectedQuantity = requestedItem.IsMap
                ? adjustedMapTotal
                : requestedItem.ExpectedQuantity;
            AddConfiguredInventoryTotal(configuredTotals, requestedItem.Label, requestedItem.Metadata, expectedQuantity);
        }

        return configuredTotals;
    }

    private static void AddConfiguredInventoryTotal(
        IDictionary<string, (string Label, int ExpectedQuantity)> configuredTotals,
        string label,
        string metadata,
        int expectedQuantity)
    {
        if (configuredTotals == null || string.IsNullOrWhiteSpace(metadata) || expectedQuantity <= 0)
        {
            return;
        }

        configuredTotals[metadata] = configuredTotals.TryGetValue(metadata, out var existing)
            ? ($"{existing.Label} / {label}", existing.ExpectedQuantity + expectedQuantity)
            : (label, expectedQuantity);
    }

    private static void ValidateRequestedMapCount(IReadOnlyList<MapDeviceRequestedSlot> requestedItems)
    {
        var requestedMapCount = requestedItems?.Count(item => item.IsMap) ?? 0;
        if (requestedMapCount == 1)
        {
            return;
        }

        throw new InvalidOperationException(requestedMapCount <= 0
            ? "Exactly one configured map item must be present in player inventory or the Map Device before loading the Map Device."
            : "More than one configured map item was found across player inventory / Map Device. Only one map type can be loaded in the Map Device.");
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