using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BeastsV2;

public partial class Main
{
    #region Shared item queries

    private static NormalInventoryItem FindStashItemByName(IList<NormalInventoryItem> items, string itemName)
    {
        if (items == null || string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        return items.FirstOrDefault(item =>
            item?.Item != null &&
            item.Item.GetComponent<Base>()?.Name.EqualsIgnoreCase(itemName) == true);
    }

    private static NormalInventoryItem FindNextMatchingStashItem(IList<NormalInventoryItem> items, string metadata)
    {
        if (items == null || string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        return items
            .Where(item => item?.Item != null &&
                           item.Item.Metadata.EqualsIgnoreCase(metadata))
            .OrderByScreenPosition(item => item.GetClientRect())
            .FirstOrDefault();
    }

    private static int CountMatchingItemQuantity(IList<NormalInventoryItem> items, string metadata)
    {
        if (items == null || string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        return items
            .Where(item => item?.Item != null &&
                           item.Item.Metadata.EqualsIgnoreCase(metadata))
            .Sum(item => Math.Max(1, item.Item.GetComponent<Stack>()?.Size ?? 1));
    }

    private static int CountMatchingItemStacks(IList<NormalInventoryItem> items, string metadata)
    {
        if (items == null || string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        return items.Count(item => item?.Item != null &&
                                  item.Item.Metadata.EqualsIgnoreCase(metadata));
    }

    private int GetVisibleMatchingQuantity<T>(
        Func<IList<T>> getVisibleItems,
        string metadata,
        Func<IList<T>, string, int> countMatchingItems)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        return countMatchingItems(getVisibleItems?.Invoke(), metadata);
    }

    private int? TryGetVisibleMatchingQuantity<T>(
        Func<IList<T>> getVisibleItems,
        string metadata,
        Func<IList<T>, string, int> countMatchingItems)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        var visibleItems = getVisibleItems?.Invoke();
        return visibleItems == null ? (int?)null : countMatchingItems(visibleItems, metadata);
    }

    private static NormalInventoryItem FindInventoryItemForMapDeviceTarget(IList<NormalInventoryItem> inventoryItems, StashAutomationTargetSettings target)
    {
        if (inventoryItems == null || target == null)
        {
            return null;
        }

        var configuredMapTier = TryGetConfiguredMapTier(target);
        if (configuredMapTier.HasValue)
        {
            return inventoryItems
                .Where(item => item?.Item != null && item.Item.GetComponent<MapKey>()?.Tier == configuredMapTier.Value)
                .OrderByScreenPosition(item => item.GetClientRect())
                .FirstOrDefault();
        }

        return FindStashItemByName(inventoryItems, target.ItemName.Value?.Trim());
    }

    private static bool DoesInventoryEntityMatchTarget(Entity entity, StashAutomationTargetSettings target)
    {
        if (entity == null || target == null)
        {
            return false;
        }

        var configuredMapTier = TryGetConfiguredMapTier(target);
        if (configuredMapTier.HasValue)
        {
            return entity.GetComponent<MapKey>()?.Tier == configuredMapTier.Value;
        }

        var configuredName = target.ItemName.Value?.Trim();
        return !string.IsNullOrWhiteSpace(configuredName) &&
               entity.GetComponent<Base>()?.Name.EqualsIgnoreCase(configuredName) == true;
    }

    private static int CountMatchingTargetQuantity(IList<NormalInventoryItem> items, StashAutomationTargetSettings target)
    {
        if (items == null || target == null)
        {
            return 0;
        }

        return items
            .Where(item => DoesInventoryEntityMatchTarget(item?.Item, target))
            .Sum(item => Math.Max(1, item.Item.GetComponent<Stack>()?.Size ?? 1));
    }

    private static int CountMatchingTargetStacks(IList<NormalInventoryItem> items, StashAutomationTargetSettings target)
    {
        if (items == null || target == null)
        {
            return 0;
        }

        return items.Count(item => DoesInventoryEntityMatchTarget(item?.Item, target));
    }

    private static (int Width, int Height) GetVisibleInventoryItemFootprint(NormalInventoryItem item)
    {
        var itemInfo = TryGetPropertyValue<object>(item?.Item, "ItemInfo");
        var width = Math.Max(1, TryGetGridIntPropertyValue(item, "ItemWidth") ?? TryGetGridIntPropertyValue(itemInfo, "Width") ?? TryGetGridIntPropertyValue(item, "SizeX") ?? 1);
        var height = Math.Max(1, TryGetGridIntPropertyValue(item, "ItemHeight") ?? TryGetGridIntPropertyValue(itemInfo, "Height") ?? TryGetGridIntPropertyValue(item, "SizeY") ?? 1);
        return (width, height);
    }

    private static int CountRequiredGridCells(IEnumerable<(int Width, int Height)> footprints)
    {
        return footprints?.Sum(footprint => Math.Max(1, footprint.Width) * Math.Max(1, footprint.Height)) ?? 0;
    }

    private static int CountFreeGridCells(bool[,] occupiedCells, int columns, int rows)
    {
        if (occupiedCells == null || columns <= 0 || rows <= 0)
        {
            return 0;
        }

        var freeCellCount = 0;
        for (var x = 0; x < columns; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                if (!occupiedCells[x, y])
                {
                    freeCellCount++;
                }
            }
        }

        return freeCellCount;
    }

    private static bool[,] BuildVisibleItemOccupiedCells(IEnumerable<NormalInventoryItem> items, int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            return null;
        }

        var occupiedCells = new bool[columns, rows];
        if (items == null)
        {
            return occupiedCells;
        }

        foreach (var item in items)
        {
            if (item == null)
            {
                continue;
            }

            var startX = Math.Max(0, TryGetGridIntPropertyValue(item, "InventPosX") ?? TryGetGridIntPropertyValue(item, "PosX") ?? 0);
            var startY = Math.Max(0, TryGetGridIntPropertyValue(item, "InventPosY") ?? TryGetGridIntPropertyValue(item, "PosY") ?? 0);
            var (width, height) = GetVisibleInventoryItemFootprint(item);
            var endX = Math.Min(columns, startX + width);
            var endY = Math.Min(rows, startY + height);

            for (var x = startX; x < endX; x++)
            {
                for (var y = startY; y < endY; y++)
                {
                    occupiedCells[x, y] = true;
                }
            }
        }

        return occupiedCells;
    }

    private static bool CanFitItemFootprintsInGrid(bool[,] occupiedCells, int columns, int rows, IEnumerable<(int Width, int Height)> footprints)
    {
        if (occupiedCells == null || columns <= 0 || rows <= 0)
        {
            return false;
        }

        var normalizedFootprints = footprints?
            .Select(footprint => (Width: Math.Max(1, footprint.Width), Height: Math.Max(1, footprint.Height)))
            .ToList() ?? [];
        if (normalizedFootprints.Count <= 0)
        {
            return true;
        }

        if (normalizedFootprints.All(footprint => footprint.Width == 1 && footprint.Height == 1))
        {
            return CountFreeGridCells(occupiedCells, columns, rows) >= normalizedFootprints.Count;
        }

        var simulatedOccupiedCells = (bool[,])occupiedCells.Clone();
        foreach (var footprint in normalizedFootprints
                     .OrderByDescending(footprint => footprint.Width * footprint.Height)
                     .ThenByDescending(footprint => Math.Max(footprint.Width, footprint.Height))
                     .ThenByDescending(footprint => Math.Min(footprint.Width, footprint.Height)))
        {
            if (!TryPlaceItemFootprint(simulatedOccupiedCells, columns, rows, footprint.Width, footprint.Height))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryPlaceItemFootprint(bool[,] occupiedCells, int columns, int rows, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        for (var x = 0; x <= columns - width; x++)
        {
            for (var y = 0; y <= rows - height; y++)
            {
                var canFit = true;
                for (var checkX = x; checkX < x + width && canFit; checkX++)
                {
                    for (var checkY = y; checkY < y + height; checkY++)
                    {
                        if (occupiedCells[checkX, checkY])
                        {
                            canFit = false;
                            break;
                        }
                    }
                }

                if (!canFit)
                {
                    continue;
                }

                for (var fillX = x; fillX < x + width; fillX++)
                {
                    for (var fillY = y; fillY < y + height; fillY++)
                    {
                        occupiedCells[fillX, fillY] = true;
                    }
                }

                return true;
            }
        }

        return false;
    }

    private static int? TryGetGridIntPropertyValue(object instance, string propertyName)
    {
        var valueText = TryGetPropertyValueAsString(instance, propertyName);
        return int.TryParse(valueText, out var value) ? value : null;
    }

    #endregion
    #region Player inventory cells

    private int GetPlayerInventoryFreeCellCount()
    {
        var occupiedSlots = GetPlayerInventoryOccupiedCells(out var columns, out var rows);
        if (occupiedSlots == null || columns <= 0 || rows <= 0)
        {
            return 0;
        }

        var freeCellCount = 0;
        for (var x = 0; x < columns; x++)
        {
            for (var y = 0; y < rows; y++)
            {
                if (!occupiedSlots[x, y])
                {
                    freeCellCount++;
                }
            }
        }

        return freeCellCount;
    }

    private bool[,] GetPlayerInventoryOccupiedCells(out int columns, out int rows)
    {
        columns = 0;
        rows = 0;

        var playerInventory = GameController?.Game?.IngameState?.ServerData?.PlayerInventories[(int)InventorySlotE.MainInventory1]?.Inventory;
        if (playerInventory == null || playerInventory.Columns <= 0 || playerInventory.Rows <= 0)
        {
            return null;
        }

        columns = playerInventory.Columns;
        rows = playerInventory.Rows;
        var occupiedSlots = new bool[columns, rows];
        foreach (var inventoryItem in playerInventory.InventorySlotItems)
        {
            var startX = Math.Max(0, inventoryItem.PosX);
            var startY = Math.Max(0, inventoryItem.PosY);
            var endX = Math.Min(columns, inventoryItem.PosX + inventoryItem.SizeX);
            var endY = Math.Min(rows, inventoryItem.PosY + inventoryItem.SizeY);

            for (var x = startX; x < endX; x++)
            {
                for (var y = startY; y < endY; y++)
                {
                    occupiedSlots[x, y] = true;
                }
            }
        }

        return occupiedSlots;
    }

    private bool TryGetVisibleStashOccupiedCells(out bool[,] occupiedCells, out int columns, out int rows)
    {
        occupiedCells = null;
        columns = 0;
        rows = 0;

        var visibleStash = GameController?.IngameState?.IngameUi?.StashElement?.VisibleStash;
        if (visibleStash == null)
        {
            return false;
        }

        var serverInventory = TryGetPropertyValue<object>(visibleStash, "ServerInventory");
        columns = Math.Max(0, TryGetGridIntPropertyValue(serverInventory, "Columns") ?? 0);
        rows = Math.Max(0, TryGetGridIntPropertyValue(serverInventory, "Rows") ?? 0);
        if (columns <= 0)
        {
            columns = Math.Max(0, TryGetGridIntPropertyValue(visibleStash, "TotalBoxesInInventoryRow") ?? 0);
        }

        if (rows <= 0 && columns > 0)
        {
            rows = InferVisibleInventoryRows(visibleStash.VisibleInventoryItems, columns);
        }

        if (columns <= 0 || rows <= 0)
        {
            return false;
        }

        occupiedCells = BuildServerInventoryOccupiedCells(serverInventory, columns, rows)
                        ?? BuildVisibleItemOccupiedCells(visibleStash.VisibleInventoryItems, columns, rows);
        return occupiedCells != null;
    }

    private static bool[,] BuildServerInventoryOccupiedCells(object serverInventory, int columns, int rows)
    {
        if (serverInventory == null || columns <= 0 || rows <= 0)
        {
            return null;
        }

        if (TryGetPropertyValue<object>(serverInventory, "InventorySlotItems") is not System.Collections.IEnumerable slotItems)
        {
            return null;
        }

        var occupiedCells = new bool[columns, rows];
        foreach (var slotItem in slotItems)
        {
            if (slotItem == null)
            {
                continue;
            }

            var startX = Math.Max(0, TryGetGridIntPropertyValue(slotItem, "PosX") ?? 0);
            var startY = Math.Max(0, TryGetGridIntPropertyValue(slotItem, "PosY") ?? 0);
            var width = Math.Max(1, TryGetGridIntPropertyValue(slotItem, "SizeX") ?? 1);
            var height = Math.Max(1, TryGetGridIntPropertyValue(slotItem, "SizeY") ?? 1);
            var endX = Math.Min(columns, startX + width);
            var endY = Math.Min(rows, startY + height);

            for (var x = startX; x < endX; x++)
            {
                for (var y = startY; y < endY; y++)
                {
                    occupiedCells[x, y] = true;
                }
            }
        }

        return occupiedCells;
    }

    private static int InferVisibleInventoryRows(IEnumerable<NormalInventoryItem> items, int columns)
    {
        if (columns <= 0)
        {
            return 0;
        }

        var maxRow = 0;
        foreach (var item in items ?? [])
        {
            if (item == null)
            {
                continue;
            }

            var startY = Math.Max(0, TryGetGridIntPropertyValue(item, "InventPosY") ?? TryGetGridIntPropertyValue(item, "PosY") ?? 0);
            var (_, height) = GetVisibleInventoryItemFootprint(item);
            maxRow = Math.Max(maxRow, startY + Math.Max(1, height));
        }

        if (maxRow > 0)
        {
            return maxRow;
        }

        return columns >= 24 ? 24 : 12;
    }

    private List<(int X, int Y)> GetPlayerInventoryNextFreeCells(int maxCount)
    {
        var requestedCount = Math.Max(0, maxCount);
        if (requestedCount <= 0)
        {
            return [];
        }

        var occupiedSlots = GetPlayerInventoryOccupiedCells(out var columns, out var rows);
        if (occupiedSlots == null || columns <= 0 || rows <= 0)
        {
            return [];
        }

        var result = new List<(int X, int Y)>(requestedCount);
        for (var x = 0; x < columns && result.Count < requestedCount; x++)
        {
            for (var y = 0; y < rows && result.Count < requestedCount; y++)
            {
                if (!occupiedSlots[x, y])
                {
                    result.Add((x, y));
                }
            }
        }

        return result;
    }

    private int CountOccupiedPlayerInventoryCells(IReadOnlyList<(int X, int Y)> cells)
    {
        if (cells == null || cells.Count <= 0)
        {
            return 0;
        }

        var occupiedSlots = GetPlayerInventoryOccupiedCells(out var columns, out var rows);
        if (occupiedSlots == null || columns <= 0 || rows <= 0)
        {
            return 0;
        }

        var occupiedCount = 0;
        foreach (var (x, y) in cells)
        {
            if (x >= 0 && x < columns && y >= 0 && y < rows && occupiedSlots[x, y])
            {
                occupiedCount++;
            }
        }

        return occupiedCount;
    }

    private string DescribePlayerInventoryCells(IReadOnlyList<(int X, int Y)> cells)
    {
        if (cells == null || cells.Count <= 0)
        {
            return "none";
        }

        var occupiedSlots = GetPlayerInventoryOccupiedCells(out var columns, out var rows);
        return string.Join(" | ", cells.Select((cell, index) =>
        {
            var state = occupiedSlots != null && cell.X >= 0 && cell.X < columns && cell.Y >= 0 && cell.Y < rows
                ? occupiedSlots[cell.X, cell.Y] ? "filled" : "empty"
                : "unknown";
            return $"{index + 1}=({cell.X},{cell.Y}):{state}";
        }));
    }

    private IList<NormalInventoryItem> GetVisiblePlayerInventoryItems()
    {
        return GameController?.IngameState?.IngameUi?.InventoryPanel[InventoryIndex.PlayerInventory]?.VisibleInventoryItems;
    }

    private IEnumerable<object> GetPlayerInventoryServerItems()
    {
        return GameController?.Game?.IngameState?.ServerData?.PlayerInventories[(int)InventorySlotE.MainInventory1]?.Inventory?.InventorySlotItems
               ?.Cast<object>()
               ?? [];
    }

    private int GetServerPlayerInventoryMatchingQuantity(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        var quantity = 0;
        foreach (var inventoryItem in GetPlayerInventoryServerItems())
        {
            var entity = TryGetPropertyValue<Entity>(inventoryItem, "Item");
            if (entity == null || !entity.Metadata.EqualsIgnoreCase(metadata))
            {
                continue;
            }

            quantity += Math.Max(1, entity.GetComponent<Stack>()?.Size ?? 1);
        }

        return quantity;
    }

    private bool TryGetPlayerInventoryItemClickTarget(string metadata, out Vector2 center, out string description)
    {
        center = default;
        description = "none";

        if (string.IsNullOrWhiteSpace(metadata))
        {
            return false;
        }

        var visibleItem = FindNextMatchingStashItem(GetVisiblePlayerInventoryItems(), metadata);
        if (visibleItem?.Item != null)
        {
            center = visibleItem.GetClientRect().Center;
            description = $"visible-rect=({visibleItem.GetClientRect().Left},{visibleItem.GetClientRect().Top},{visibleItem.GetClientRect().Right},{visibleItem.GetClientRect().Bottom})";
            return true;
        }

        foreach (var inventoryItem in GetPlayerInventoryServerItems())
        {
            var entity = TryGetPropertyValue<Entity>(inventoryItem, "Item");
            if (entity == null || !entity.Metadata.EqualsIgnoreCase(metadata))
            {
                continue;
            }

            var posX = TryGetGridIntPropertyValue(inventoryItem, "PosX") ?? 0;
            var posY = TryGetGridIntPropertyValue(inventoryItem, "PosY") ?? 0;
            var cellCenter = TryGetPlayerInventoryCellCenter(posX, posY);
            if (!cellCenter.HasValue)
            {
                continue;
            }

            center = cellCenter.Value;
            var stackSize = Math.Max(1, entity.GetComponent<Stack>()?.Size ?? 1);
            description = $"server-cell=({posX},{posY}), stack={stackSize}";
            return true;
        }

        return false;
    }

    private int GetVisiblePlayerInventoryMatchingQuantity(string metadata)
    {
        return CountMatchingItemQuantity(GetVisiblePlayerInventoryItems(), metadata);
    }

    private int GetVisiblePlayerInventoryMatchingQuantity(StashAutomationTargetSettings target)
    {
        return CountMatchingTargetQuantity(GetVisiblePlayerInventoryItems(), target);
    }

    private int GetVisiblePlayerInventoryMatchingStackCount(string metadata)
    {
        return CountMatchingItemStacks(GetVisiblePlayerInventoryItems(), metadata);
    }

    private int GetVisiblePlayerInventoryMatchingStackCount(StashAutomationTargetSettings target)
    {
        return CountMatchingTargetStacks(GetVisiblePlayerInventoryItems(), target);
    }

    private int? TryGetVisiblePlayerInventoryMatchingQuantity(string metadata)
    {
        var visibleItems = GetVisiblePlayerInventoryItems();
        return visibleItems == null ? (int?)null : CountMatchingItemQuantity(visibleItems, metadata);
    }

    private int GetReadablePlayerInventoryMatchingQuantity(string metadata)
    {
        var visibleQuantity = GetVisiblePlayerInventoryMatchingQuantity(metadata);
        return visibleQuantity > 0 ? visibleQuantity : GetServerPlayerInventoryMatchingQuantity(metadata);
    }

    private int GetServerPlayerInventoryMatchingQuantity(StashAutomationTargetSettings target)
    {
        if (target == null)
        {
            return 0;
        }

        var quantity = 0;
        foreach (var inventoryItem in GetPlayerInventoryServerItems())
        {
            var entity = TryGetPropertyValue<Entity>(inventoryItem, "Item");
            if (!DoesInventoryEntityMatchTarget(entity, target))
            {
                continue;
            }

            quantity += Math.Max(1, entity.GetComponent<Stack>()?.Size ?? 1);
        }

        return quantity;
    }

    private int GetReadablePlayerInventoryMatchingQuantity(StashAutomationTargetSettings target)
    {
        var visibleQuantity = GetVisiblePlayerInventoryMatchingQuantity(target);
        return visibleQuantity > 0 ? visibleQuantity : GetServerPlayerInventoryMatchingQuantity(target);
    }

    private int GetServerPlayerInventoryMatchingStackCount(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return 0;
        }

        var stackCount = 0;
        foreach (var inventoryItem in GetPlayerInventoryServerItems())
        {
            var entity = TryGetPropertyValue<Entity>(inventoryItem, "Item");
            if (entity != null && entity.Metadata.EqualsIgnoreCase(metadata))
            {
                stackCount++;
            }
        }

        return stackCount;
    }

    private int GetServerPlayerInventoryMatchingStackCount(StashAutomationTargetSettings target)
    {
        if (target == null)
        {
            return 0;
        }

        var stackCount = 0;
        foreach (var inventoryItem in GetPlayerInventoryServerItems())
        {
            var entity = TryGetPropertyValue<Entity>(inventoryItem, "Item");
            if (DoesInventoryEntityMatchTarget(entity, target))
            {
                stackCount++;
            }
        }

        return stackCount;
    }

    private int GetReadablePlayerInventoryMatchingStackCount(string metadata)
    {
        var visibleStackCount = GetVisiblePlayerInventoryMatchingStackCount(metadata);
        return visibleStackCount > 0 ? visibleStackCount : GetServerPlayerInventoryMatchingStackCount(metadata);
    }

    private int GetReadablePlayerInventoryMatchingStackCount(StashAutomationTargetSettings target)
    {
        var visibleStackCount = GetVisiblePlayerInventoryMatchingStackCount(target);
        return visibleStackCount > 0 ? visibleStackCount : GetServerPlayerInventoryMatchingStackCount(target);
    }

    private int GetConfiguredRestockTargetFullStackSize(StashAutomationTargetSettings target)
    {
        if (TryGetConfiguredMapTier(target).HasValue)
        {
            return 1;
        }

        var visibleInventoryItem = FindInventoryItemForMapDeviceTarget(GetVisiblePlayerInventoryItems(), target);
        if (visibleInventoryItem?.Item != null)
        {
            return GetKnownFullStackSize(visibleInventoryItem.Item, visibleInventoryItem.Item.Metadata)
                   ?? GetConfiguredRestockTargetHeuristicFullStackSize(target);
        }

        foreach (var inventoryItem in GetPlayerInventoryServerItems())
        {
            var entity = TryGetPropertyValue<Entity>(inventoryItem, "Item");
            if (!DoesInventoryEntityMatchTarget(entity, target))
            {
                continue;
            }

            return GetKnownFullStackSize(entity, entity.Metadata)
                   ?? GetConfiguredRestockTargetHeuristicFullStackSize(target);
        }

        return GetConfiguredRestockTargetHeuristicFullStackSize(target);
    }

    private static int GetConfiguredRestockTargetHeuristicFullStackSize(StashAutomationTargetSettings target)
    {
        if (TryGetConfiguredMapTier(target).HasValue)
        {
            return 1;
        }

        var configuredItemName = target?.ItemName.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredItemName) &&
            configuredItemName.IndexOf("Scarab", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 20;
        }

        var selectedTabName = target?.SelectedTabName.Value?.Trim();
        return string.Equals(selectedTabName, "Fragments", StringComparison.OrdinalIgnoreCase)
            ? 10
            : 1;
    }

    private static int? GetKnownFullStackSize(Entity item, string metadata = null)
    {
        var itemInfo = TryGetPropertyValue<object>(item, "ItemInfo");
        var currencyInfo = TryGetPropertyValue<object>(itemInfo, "CurrencyInfo");
        var maxStackSize = TryGetGridIntPropertyValue(currencyInfo, "MaxStackSize");
        if (maxStackSize.HasValue && maxStackSize.Value > 1)
        {
            return maxStackSize.Value;
        }

        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        return metadata.IndexOf("Metadata/Items/Scarabs/", StringComparison.OrdinalIgnoreCase) >= 0 ? 20 : null;
    }

    private static int? GetKnownFullStackSize(IEnumerable<NormalInventoryItem> items, string metadata)
    {
        if (items != null && !string.IsNullOrWhiteSpace(metadata))
        {
            var matchingItem = items.FirstOrDefault(item =>
                item?.Item != null &&
                item.Item.Metadata.EqualsIgnoreCase(metadata));
            var itemStackSize = GetKnownFullStackSize(matchingItem?.Item, metadata);
            if (itemStackSize.HasValue)
            {
                return itemStackSize;
            }
        }

        return GetKnownFullStackSize((Entity)null, metadata);
    }

    private Vector2? TryGetPlayerInventoryCellCenter(int x, int y)
    {
        var inventoryPanel = GameController?.IngameState?.IngameUi?.InventoryPanel[InventoryIndex.PlayerInventory];
        var playerInventory = GameController?.Game?.IngameState?.ServerData?.PlayerInventories[(int)InventorySlotE.MainInventory1]?.Inventory;
        if (inventoryPanel?.IsVisible != true || playerInventory == null || playerInventory.Columns <= 0 || playerInventory.Rows <= 0)
        {
            return null;
        }

        var rect = inventoryPanel.GetClientRect();
        if (rect.Width <= 0 || rect.Height <= 0 || x < 0 || x >= playerInventory.Columns || y < 0 || y >= playerInventory.Rows)
        {
            return null;
        }

        var cellWidth = rect.Width / playerInventory.Columns;
        var cellHeight = rect.Height / playerInventory.Rows;
        return new Vector2(
            rect.Left + (x + 0.5f) * cellWidth,
            rect.Top + (y + 0.5f) * cellHeight);
    }

    private async Task PlaceItemIntoPlayerInventoryCellAsync(int x, int y)
    {
        var cellCenter = TryGetPlayerInventoryCellCenter(x, y);
        if (!cellCenter.HasValue)
        {
            throw new InvalidOperationException($"Could not resolve player inventory cell center for ({x},{y}).");
        }

        var inventoryPlacementPreClickDelayMs = Math.Max(AutomationTiming.UiClickPreDelayMs, 100);
        await ClickAtAsync(
            cellCenter.Value,
            holdCtrl: false,
            preClickDelayMs: inventoryPlacementPreClickDelayMs,
            postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs);
    }

    #endregion
    #region Captured monster detection

    private List<NormalInventoryItem> GetVisibleCapturedMonsterInventoryItems()
    {
        var inventoryItems = GetVisiblePlayerInventoryItems();
        if (inventoryItems == null)
        {
            return [];
        }

        return inventoryItems
            .Where(IsCapturedMonsterInventoryItem)
            .OrderByScreenPosition(item => item.GetClientRect())
            .ToList();
    }

    private static bool IsCapturedMonsterInventoryItem(NormalInventoryItem item)
    {
        var path = item?.Item?.Path;
        if (!string.IsNullOrWhiteSpace(path) && path.IndexOf(CapturedMonsterItemPathFragment, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        var metadata = item?.Item?.Metadata;
        return !string.IsNullOrWhiteSpace(metadata) && metadata.IndexOf(CapturedMonsterItemPathFragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRedCapturedMonsterInventoryItem(NormalInventoryItem item)
    {
        if (item?.Item == null)
        {
            return false;
        }

        var capturedMonster = item.Item.GetComponent<CapturedMonster>();
        var monsterVariety = capturedMonster?.MonsterVariety;
        if (IsKnownRedCapturedMonsterIdentity(TryGetPropertyValueAsString(monsterVariety, "VarietyId")) ||
            IsKnownRedCapturedMonsterIdentity(TryGetPropertyValueAsString(monsterVariety, "BaseMonsterTypeIndex")) ||
            IsKnownRedCapturedMonsterIdentity(TryGetPropertyValueAsString(monsterVariety, "Name")) ||
            IsKnownRedCapturedMonsterIdentity(TryGetPropertyValueAsString(monsterVariety, "MonsterName")))
        {
            return true;
        }

        return IsKnownRedCapturedMonsterIdentity(item.Item.GetComponent<Base>()?.Name);
    }

    private static bool IsKnownRedCapturedMonsterIdentity(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        return BeastsV2BeastData.AllRedBeasts.Any(beast =>
            beast.Name.EqualsIgnoreCase(identity) ||
            beast.MetadataPatterns.Any(pattern => identity.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private async Task<int> WaitForCapturedMonsterInventoryItemCountToChangeAsync(int previousCount)
    {
        var timeoutMs = Math.Max(
            AutomationTiming.QuantityChangeBaseDelayMs,
            GetConfiguredClickDelayMs() + AutomationTiming.QuantityChangeBaseDelayMs);

        await WaitForBestiaryConditionAsync(
            () => GetVisibleCapturedMonsterInventoryItems().Count < previousCount,
            timeoutMs);

        return GetVisibleCapturedMonsterInventoryItems().Count;
    }

    #endregion
    #region Currency shift-click

    private Element TryGetCurrencyShiftClickMenu()
    {
        var ingameUi = GameController?.IngameState?.IngameUi;
        return TryGetPropertyValue<Element>(ingameUi, "CurrencyShiftClickMenu")
               ?? TryGetChildFromIndicesQuietly(ingameUi, CurrencyShiftClickMenuPath);
    }

    private Element TryGetCurrencyShiftClickMenuConfirmButton()
    {
        return TryGetChildFromIndicesQuietly(TryGetCurrencyShiftClickMenu(), CurrencyShiftClickMenuConfirmButtonPath);
    }

    private Element TryGetCurrencyShiftClickMenuQuantityTextElement()
    {
        return TryGetChildFromIndicesQuietly(TryGetCurrencyShiftClickMenu(), CurrencyShiftClickMenuQuantityTextPath);
    }

    private async Task<bool> WaitForCurrencyShiftClickMenuVisibleAsync()
    {
        return await WaitForBestiaryConditionAsync(
            () => TryGetCurrencyShiftClickMenu()?.IsVisible == true,
            1000,
            Math.Max(AutomationTiming.FastPollDelayMs, 10));
    }

    private async Task<bool> WaitForCurrencyShiftClickMenuHiddenAsync()
    {
        return await WaitForBestiaryConditionAsync(
            () => TryGetCurrencyShiftClickMenu()?.IsVisible != true,
            1000,
            Math.Max(AutomationTiming.FastPollDelayMs, 10));
    }

    private async Task InputCurrencyShiftClickQuantityAsync(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("Partial transfer quantity must be greater than zero.");
        }

        if (!await WaitForCurrencyShiftClickMenuVisibleAsync())
        {
            throw new InvalidOperationException("Timed out waiting for partial transfer menu before entering quantity.");
        }

        LogDebug($"Partial transfer menu became visible. {DescribeCurrencyShiftClickMenuQuantityState()}");

        var quantityText = quantity.ToString();
        var observedQuantityText = await RetryAutomationAsync(
            async attempt =>
            {
                await TypeDigitTextAsync(quantityText);
                var observedText = await WaitForCurrencyShiftClickMenuQuantityTextAsync(quantityText, 250) ?? GetCurrencyShiftClickMenuQuantityText();
                if (!observedText.EqualsIgnoreCase(quantityText))
                {
                    LogDebug($"Partial transfer quantity text mismatch after typing attempt {attempt + 1}. expected='{quantityText}', observed='{observedText ?? "<null>"}'. {DescribeCurrencyShiftClickMenuQuantityState()}");
                }

                return observedText;
            },
            observedText => observedText.EqualsIgnoreCase(quantityText),
            maxAttempts: 2);

        if (!observedQuantityText.EqualsIgnoreCase(quantityText))
        {
            LogDebug($"Partial transfer quantity final mismatch state. expected='{quantityText}', observed='{observedQuantityText ?? "<null>"}'. {DescribeCurrencyShiftClickMenuQuantityState()}");
            throw new InvalidOperationException($"Partial transfer quantity text mismatch. Expected '{quantityText}', observed '{observedQuantityText ?? "<null>"}'.");
        }

        await TapKeyAsync(Keys.Enter, AutomationTiming.KeyTapDelayMs, 0);
        if (!await WaitForCurrencyShiftClickMenuHiddenAsync())
        {
            throw new InvalidOperationException("Timed out closing partial transfer menu after confirming quantity.");
        }

        LogDebug($"Partial transfer quantity entered with Enter confirmation. quantity={quantityText}, observedText='{observedQuantityText}', menu={DescribeElement(TryGetCurrencyShiftClickMenu())}");
    }

    private string GetCurrencyShiftClickMenuQuantityText()
    {
        var quantityTextElement = TryGetCurrencyShiftClickMenuQuantityTextElement();
        if (quantityTextElement == null)
        {
            return null;
        }

        return TryGetPropertyValueAsString(quantityTextElement, "TextNoTags")?.Trim()
               ?? TryGetPropertyValueAsString(quantityTextElement, "Text")?.Trim()
               ?? TryGetElementText(quantityTextElement)
               ?? GetElementTextRecursive(quantityTextElement, 1)?.Trim();
    }

    private async Task<string> WaitForCurrencyShiftClickMenuQuantityTextAsync(string expectedText, int timeoutMs)
    {
        return await PollAutomationValueAsync(
            GetCurrencyShiftClickMenuQuantityText,
            observedText => !string.IsNullOrWhiteSpace(observedText) &&
                            (string.IsNullOrWhiteSpace(expectedText) || observedText.EqualsIgnoreCase(expectedText)),
            timeoutMs,
            AutomationTiming.FastPollDelayMs);
    }

    private async Task TypeDigitTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var character in text)
        {
            var key = character switch
            {
                '0' => Keys.D0,
                '1' => Keys.D1,
                '2' => Keys.D2,
                '3' => Keys.D3,
                '4' => Keys.D4,
                '5' => Keys.D5,
                '6' => Keys.D6,
                '7' => Keys.D7,
                '8' => Keys.D8,
                '9' => Keys.D9,
                _ => Keys.None
            };

            if (key == Keys.None)
            {
                throw new InvalidOperationException($"Unsupported partial transfer quantity character '{character}'.");
            }

            await TapKeyAsync(key, AutomationTiming.KeyTapDelayMs, AutomationTiming.FastPollDelayMs);
        }
    }

    private string DescribeCurrencyShiftClickMenuQuantityState()
    {
        var menu = TryGetCurrencyShiftClickMenu();
        var quantityTextElement = TryGetCurrencyShiftClickMenuQuantityTextElement();
        var textNoTags = TryGetPropertyValueAsString(quantityTextElement, "TextNoTags")?.Trim();
        var text = TryGetPropertyValueAsString(quantityTextElement, "Text")?.Trim();
        var directGetText = TryGetElementText(quantityTextElement);
        var recursiveText = GetElementTextRecursive(quantityTextElement, 1)?.Trim();

        return $"menu={DescribeElement(menu)}, quantityPath={DescribePath(CurrencyShiftClickMenuQuantityTextPath)}, quantityPathTrace={DescribePathLookup(menu, CurrencyShiftClickMenuQuantityTextPath)}, quantityElement={DescribeElement(quantityTextElement)}, textNoTags='{textNoTags ?? "<null>"}', text='{text ?? "<null>"}', getText='{directGetText ?? "<null>"}', recursiveText='{recursiveText ?? "<null>"}'";
    }

    #endregion
}
