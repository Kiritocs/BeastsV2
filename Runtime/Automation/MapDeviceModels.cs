using System.Collections.Generic;

namespace BeastsV2;

internal sealed record MapDeviceRequestedSlot(int SlotIndex, string Label, string Metadata, bool IsMap, int ExpectedQuantity);

internal sealed record MapDeviceLoadPlan(
    IReadOnlyList<MapDeviceRequestedSlot> RequestedItems,
    IReadOnlyDictionary<string, (string Label, int ExpectedQuantity)> ConfiguredInventoryTotals,
    MapDeviceRequestedSlot MapSlot);

internal sealed record MapDeviceCurrentStateObservation(
    IReadOnlyDictionary<int, int> SlotQuantities,
    IReadOnlyDictionary<string, int> CombinedQuantities);

internal sealed record MapDeviceVisibleSlotState(int SlotIndex, string Metadata, int Quantity);