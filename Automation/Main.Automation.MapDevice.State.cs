using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;

namespace BeastsV2;

public partial class Main
{
    private bool TryGetVisibleMapDeviceScarabSlots(out List<object> scarabSlots)
    {
        scarabSlots = [];

        var mapDeviceWindow = GameController?.IngameState?.IngameUi?.MapDeviceWindow;
        if (!CanReadMapDeviceWindowState() || mapDeviceWindow == null)
        {
            return false;
        }

        if (mapDeviceWindow.GetType().GetProperty("ScarabSlots")?.GetValue(mapDeviceWindow) is not IEnumerable rawScarabSlots)
        {
            return false;
        }

        foreach (var slot in rawScarabSlots)
        {
            scarabSlots.Add(slot);
        }

        return true;
    }

    private string DescribeMapDeviceLoadedSlotState()
    {
        if (!CanReadMapDeviceWindowState())
        {
            return "unavailable";
        }

        var loadedItems = GetVisibleMapDeviceItems();
        var occupiedSlots = loadedItems.Count(item => item?.Item != null);
        var slotCapacity = GetVisibleMapDeviceSlotCapacity();
        var freeSlotsText = slotCapacity > 0
            ? $", freeSlots={Math.Max(0, slotCapacity - occupiedSlots)}/{slotCapacity}"
            : string.Empty;
        var loadedCounts = loadedItems
            .Where(item => item?.Item != null && !string.IsNullOrWhiteSpace(item.Item.Metadata))
            .GroupBy(item => item.Item.Metadata, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(GetVisibleInventoryItemStackQuantity),
                StringComparer.OrdinalIgnoreCase);

        return $"occupiedSlots={occupiedSlots}{freeSlotsText}, loaded={DescribeMetadataCounts(loadedCounts)}";
    }

    private int GetVisibleMapDeviceSlotCapacity()
    {
        return TryGetVisibleMapDeviceScarabSlots(out var scarabSlots) ? scarabSlots.Count : 0;
    }

    private List<string> GetVisibleMapDeviceItemMetadata()
    {
        if (!CanReadMapDeviceWindowState())
        {
            return [];
        }

        var metadata = GetVisibleMapDeviceItems()
            .Where(item => item?.Item != null && !string.IsNullOrWhiteSpace(item.Item.Metadata))
            .Select(item => item.Item.Metadata)
            .ToList();

        metadata.AddRange(GetVisibleMapDeviceStorageItems()
            .Where(IsMapDeviceStoredMapItem)
            .Select(item => item.Item.Metadata)
            .ToList());

        return metadata;
    }

    private Dictionary<string, int> GetVisibleMapDeviceQuantities()
    {
        if (!CanReadMapDeviceWindowState())
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var quantities = GetVisibleMapDeviceItems()
            .Where(item => item?.Item != null && !string.IsNullOrWhiteSpace(item.Item.Metadata))
            .GroupBy(item => item.Item.Metadata, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(GetVisibleInventoryItemStackQuantity),
                StringComparer.OrdinalIgnoreCase);

        foreach (var storedMap in GetVisibleMapDeviceStorageItems().Where(IsMapDeviceStoredMapItem))
        {
            var metadata = storedMap.Item.Metadata;
            var quantity = GetVisibleInventoryItemStackQuantity(storedMap);
            quantities[metadata] = quantities.TryGetValue(metadata, out var existingQuantity)
                ? existingQuantity + quantity
                : quantity;
        }

        return quantities;
    }

    private static bool IsMapDeviceStoredMapItem(NormalInventoryItem item)
    {
        return item?.Item != null &&
               !string.IsNullOrWhiteSpace(item.Item.Metadata) &&
               item.Item.GetComponent<MapKey>() != null;
    }

    private List<NormalInventoryItem> GetVisibleMapDeviceItems()
    {
        return TryGetVisibleMapDeviceScarabSlots(out var scarabSlots)
            ? GetVisibleMapDeviceSlotItemsFromScarabSlots(scarabSlots).Where(item => item?.Item != null).ToList()
            : [];
    }

    private List<NormalInventoryItem> GetVisibleMapDeviceSlotItems()
    {
        return TryGetVisibleMapDeviceScarabSlots(out var scarabSlots)
            ? GetVisibleMapDeviceSlotItemsFromScarabSlots(scarabSlots)
            : [];
    }

    private NormalInventoryItem GetVisibleMapDeviceItemInSlot(int slotIndex)
    {
        var slotItems = GetVisibleMapDeviceSlotItems();
        return slotIndex >= 0 && slotIndex < slotItems.Count ? slotItems[slotIndex] : null;
    }

    private SharpDX.Vector2? GetVisibleMapDeviceSlotCenter(int slotIndex)
    {
        var slotElements = GetVisibleMapDeviceSlotElements();
        if (slotIndex < 0 || slotIndex >= slotElements.Count)
        {
            return null;
        }

        var slotElement = slotElements[slotIndex];
        return TryGetElementIsVisible(slotElement) ? TryGetElementCenter(slotElement) : null;
    }

    private int GetNextFreeMapDeviceFragmentSlotIndex()
    {
        var slotItems = GetVisibleMapDeviceSlotItems();
        for (var slotIndex = 1; slotIndex <= MapDeviceFragmentSlotCount; slotIndex++)
        {
            if (slotIndex >= slotItems.Count || slotItems[slotIndex]?.Item == null)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private static List<NormalInventoryItem> GetVisibleMapDeviceSlotItemsFromScarabSlots(IEnumerable scarabSlots)
    {
        var items = new List<NormalInventoryItem>();
        if (scarabSlots == null)
        {
            return items;
        }

        foreach (var slot in scarabSlots)
        {
            NormalInventoryItem slotItem = null;
            if (slot == null)
            {
                items.Add(slotItem);
                continue;
            }

            if (slot.GetType().GetProperty("VisibleInventoryItems")?.GetValue(slot) is not IEnumerable visibleInventoryItems)
            {
                items.Add(slotItem);
                continue;
            }

            foreach (var visibleItem in visibleInventoryItems)
            {
                if (visibleItem is NormalInventoryItem inventoryItem && HasReadableMapDeviceItemMetadata(inventoryItem))
                {
                    slotItem = inventoryItem;
                    break;
                }
            }

            items.Add(slotItem);
        }

        return items;
    }

    private List<object> GetVisibleMapDeviceSlotElements()
    {
        return TryGetVisibleMapDeviceScarabSlots(out var scarabSlots)
            ? scarabSlots
            : [];
    }

    private static bool HasReadableMapDeviceItemMetadata(NormalInventoryItem item)
    {
        return !string.IsNullOrWhiteSpace(item?.Item?.Metadata);
    }

    private bool CanReadMapDeviceWindowState()
    {
        var ui = GameController?.IngameState?.IngameUi;
        return ui?.MapDeviceWindow != null && (ui.MapDeviceWindow.IsVisible == true || ui.Atlas?.IsVisible == true);
    }

    private List<NormalInventoryItem> GetVisibleMapDeviceStorageItems()
    {
        var storageInventory = GameController?.IngameState?.IngameUi?.Atlas?.MapDeviceStorage;
        return storageInventory?.VisibleInventoryItems?.Where(item => item?.Item != null).ToList() ?? [];
    }

    private int GetVisibleLoadedMapDeviceMatchingQuantity(string metadata) =>
        GetVisibleMatchingQuantity(GetVisibleMapDeviceItems, metadata, CountMatchingItemQuantity);

    private int GetVisibleMapDeviceStorageMatchingQuantity(string metadata) =>
        GetVisibleMatchingQuantity(GetVisibleMapDeviceStorageItems, metadata, CountMatchingItemQuantity);

    private int GetVisibleMapDeviceMatchingQuantity(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        return GetVisibleMapDeviceQuantities().TryGetValue(metadata, out var quantity) ? quantity : 0;
    }
}