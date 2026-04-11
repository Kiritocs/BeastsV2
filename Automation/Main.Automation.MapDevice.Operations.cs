using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements.InventoryElements;

namespace BeastsV2;

public partial class Main
{
    private sealed record MapDeviceMapLoadPlan(
        Func<Task> TransferAsync,
        Func<Task<bool>> WaitForTransferAsync,
        Action ThrowOnFailure,
        string SuccessLogMessage);

    private Task<bool> WaitForMapDeviceTransferAsync(Func<bool> isSatisfied)
    {
        return WaitForBestiaryConditionAsync(
            isSatisfied,
            MapDeviceTransferTimeoutMs,
            Math.Max(10, AutomationTiming.FastPollDelayMs));
    }

    private Task<bool> WaitForLoadedMapDeviceQuantityAtLeastAsync(string metadata, int expectedQuantity)
    {
        return WaitForMapDeviceTransferAsync(
            () => GetVisibleLoadedMapDeviceMatchingQuantity(metadata) >= expectedQuantity);
    }

    private Task<bool> WaitForMapDeviceQuantityOrInventoryDropAsync(
        string metadata,
        int previousMapDeviceQuantity,
        int previousInventoryQuantity)
    {
        return WaitForMapDeviceTransferAsync(
            () =>
            {
                var currentInventoryQuantity = GetReadablePlayerInventoryMatchingQuantity(metadata);
                return GetVisibleMapDeviceMatchingQuantity(metadata) > previousMapDeviceQuantity ||
                       currentInventoryQuantity < previousInventoryQuantity;
            });
    }

    private Task<bool> WaitForLoadedMapOrInventoryDropAsync(
        string metadata,
        int previousInventoryQuantity,
        int slotIndex = 0)
    {
        return WaitForMapDeviceTransferAsync(
            () =>
            {
                var loadedMap = GetVisibleMapDeviceItemInSlot(slotIndex);
                var currentInventoryQuantity = GetReadablePlayerInventoryMatchingQuantity(metadata);
                return loadedMap?.Item?.Metadata.EqualsIgnoreCase(metadata) == true ||
                       currentInventoryQuantity < previousInventoryQuantity;
            });
    }

    private Task<bool> WaitForMapDeviceStorageTransferAsync(
        string metadata,
        int previousLoadedQuantity,
        int previousStorageQuantity)
    {
        return WaitForMapDeviceTransferAsync(
            () =>
                GetVisibleLoadedMapDeviceMatchingQuantity(metadata) > previousLoadedQuantity ||
                GetVisibleMapDeviceStorageMatchingQuantity(metadata) < previousStorageQuantity);
    }

    private Task<bool> WaitForSpecificMapDeviceSlotQuantityAsync(string metadata, int slotIndex, int expectedQuantity)
    {
        return WaitForMapDeviceTransferAsync(
            () =>
            {
                var slotItem = GetVisibleMapDeviceItemInSlot(slotIndex);
                return slotItem?.Item?.Metadata.EqualsIgnoreCase(metadata) == true &&
                       GetVisibleInventoryItemStackQuantity(slotItem) == expectedQuantity;
            });
    }

    private void ThrowMapDeviceNewSlotTransferFailure(
        (string Label, string Metadata, bool IsMap) requestedItem,
        int transferQuantity,
        string slotStateBefore,
        string failureMessage)
    {
        LogMapDeviceNewSlotInsertTimeout(requestedItem, transferQuantity, slotStateBefore);
        throw new InvalidOperationException(failureMessage);
    }

    private static void ThrowMapDeviceClickTransferFailure(string label, string sourceDescription, string clickTargetDescription = null)
    {
        var clickTargetSuffix = string.IsNullOrWhiteSpace(clickTargetDescription)
            ? string.Empty
            : $" clickTarget={clickTargetDescription}";
        throw new InvalidOperationException($"Failed to move {label} from {sourceDescription}.{clickTargetSuffix}");
    }

    private void LogMapDeviceStorageLookupFailure(string label, string metadata, int lookupAttempts, int storageQuantity)
    {
        LogDebug(
            $"Map device storage lookup failed for {label} ({metadata}). " +
            $"lookupAttempts={lookupAttempts}/{MapDeviceInventoryLookupRetryCount}, " +
            $"storageQty={storageQuantity}");
    }

    private async Task TransferItemIntoMapDeviceAsync(
        string label,
        Func<Task> transferAsync,
        Func<Task<bool>> waitForTransferAsync,
        Action throwOnFailure)
    {
        UpdateMapDeviceLoadingStatus(label);
        await transferAsync();

        if (!await waitForTransferAsync())
        {
            throwOnFailure();
        }
    }

    private void LogMapDeviceInventoryLookupRecovered(
        (string Label, string Metadata, bool IsMap) requestedItem,
        int lookupAttempts,
        int expectedMapDeviceQuantity,
        string clickTargetDescription)
    {
        LogDebug(
            $"Map device inventory lookup recovered for {requestedItem.Label} ({requestedItem.Metadata}) " +
            $"after {lookupAttempts} attempts. " +
            $"expectedMapDeviceQty={expectedMapDeviceQuantity}, " +
            $"currentMapDeviceQty={GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata)}, " +
            $"clickTarget={clickTargetDescription}");
    }

    private void LogMapDeviceInventoryLookupStoppedEarly((string Label, string Metadata, bool IsMap) requestedItem, int lookupAttempts)
    {
        LogDebug(
            $"Map device inventory lookup became unnecessary for {requestedItem.Label} ({requestedItem.Metadata}) " +
            $"after {lookupAttempts} attempts because the visible Map Device quantity reached the expected value.");
    }

    private void ThrowMapDeviceInventoryLookupFailure(
        (string Label, string Metadata, bool IsMap) requestedItem,
        int attemptNumberForMetadata,
        int totalRequestedCountForMetadata,
        int expectedMapDeviceQuantity,
        IList<NormalInventoryItem> inventoryItems,
        int lookupAttempts)
    {
        var visibleInventoryQuantity = TryGetVisiblePlayerInventoryMatchingQuantity(requestedItem.Metadata) ?? 0;
        var readableInventoryQuantity = GetReadablePlayerInventoryMatchingQuantity(requestedItem.Metadata);
        LogDebug(
            $"Map device inventory lookup failed for {requestedItem.Label} ({requestedItem.Metadata}). " +
            $"attempt={attemptNumberForMetadata}/{Math.Max(1, totalRequestedCountForMetadata)}, " +
            $"lookupAttempts={lookupAttempts}/{MapDeviceInventoryLookupRetryCount}, " +
            $"expectedMapDeviceQty={expectedMapDeviceQuantity}, " +
            $"currentMapDeviceQty={GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata)}, " +
            $"visibleQty={visibleInventoryQuantity}, " +
            $"readableQty={readableInventoryQuantity}, " +
            $"visibleMatches=[{DescribeVisibleInventoryMatches(inventoryItems, requestedItem.Metadata)}]");
        throw new InvalidOperationException($"Could not find {requestedItem.Label} in player inventory.");
    }

    private NormalInventoryItem FindVisibleInventoryItemForMapDeviceTransferOrThrow(
        MapDeviceRequestedSlot requestedItem,
        int requiredQuantity,
        bool allowPartialQuantity,
        string failureMessage)
    {
        var inventoryItems = GetVisiblePlayerInventoryItemsOrThrow("Player inventory is not visible.");
        var sourceItem = FindVisibleInventoryItemForMapDeviceTransfer(
            inventoryItems,
            requestedItem.Metadata,
            requiredQuantity,
            allowPartialQuantity);
        if (sourceItem?.Item != null)
        {
            return sourceItem;
        }

        throw new InvalidOperationException(failureMessage);
    }

    private static void ThrowMapDeviceFragmentSlotOverfilled(MapDeviceRequestedSlot requestedItem, int currentSlotQuantity)
    {
        throw new InvalidOperationException(
            $"{requestedItem.Label} already contains too many items. Expected x{requestedItem.ExpectedQuantity}, found x{currentSlotQuantity}.");
    }

    private static void ThrowMapDeviceUnexpectedNextFreeFragmentSlot(MapDeviceRequestedSlot requestedItem, int nextFreeSlotIndex)
    {
        var nextFreeSlotLabel = nextFreeSlotIndex >= 0 ? $"Slot {nextFreeSlotIndex + 1}" : "no free fragment slot";
        throw new InvalidOperationException(
            $"{requestedItem.Label} cannot be loaded because the next free fragment slot is {nextFreeSlotLabel}, not Slot {requestedItem.SlotIndex + 1}.");
    }

    private void EnsureMapDeviceFragmentSlotLanded(MapDeviceRequestedSlot requestedItem, int insertedQuantity)
    {
        var insertedSlotItem = GetVisibleMapDeviceItemInSlot(requestedItem.SlotIndex);
        var detectedQuantity = insertedSlotItem?.Item == null
            ? 0
            : GetVisibleInventoryItemStackQuantity(insertedSlotItem);
        if (insertedSlotItem?.Item?.Metadata.EqualsIgnoreCase(requestedItem.Metadata) != true || detectedQuantity != insertedQuantity)
        {
            throw new InvalidOperationException(
                $"{requestedItem.Label} did not land in the expected Map Device slot after transferring quantity {insertedQuantity}.");
        }
    }

    private async Task<(int Attempts, bool Resolved, bool StoppedEarly)> RetryMapDeviceClickTargetLookupAsync(
        Func<bool> tryResolveClickTarget,
        Func<int, bool> shouldStopEarly = null)
    {
        var result = await RetryAutomationAsync(
            attempt => Task.FromResult(
                tryResolveClickTarget()
                    ? (Attempts: attempt, Resolved: true, StoppedEarly: false)
                    : shouldStopEarly?.Invoke(attempt) == true
                        ? (Attempts: attempt, Resolved: false, StoppedEarly: true)
                        : (Attempts: attempt, Resolved: false, StoppedEarly: false)),
            lookup => lookup.Resolved || lookup.StoppedEarly,
            MapDeviceInventoryLookupRetryCount,
            MapDeviceInventoryLookupRetryDelayMs,
            firstAttemptNumber: 1);

        return result.Attempts > 0
            ? result
            : (MapDeviceInventoryLookupRetryCount, false, false);
    }

    private async Task EnsureRequestedMapLoadedAsync(MapDeviceRequestedSlot requestedMap)
    {
        if (requestedMap == null || !requestedMap.IsMap || string.IsNullOrWhiteSpace(requestedMap.Metadata))
        {
            return;
        }

        var loadedMapItem = GetVisibleMapDeviceItemInSlot(0);
        if (loadedMapItem?.Item?.Metadata.EqualsIgnoreCase(requestedMap.Metadata) == true)
        {
            return;
        }

        await EnsureRequestedMapLoadedFromStorageAsync(requestedMap);

        loadedMapItem = GetVisibleMapDeviceItemInSlot(0);
        if (loadedMapItem?.Item?.Metadata.EqualsIgnoreCase(requestedMap.Metadata) == true)
        {
            return;
        }

        await EnsureRequestedMapLoadedFromInventoryAsync(requestedMap);
    }

    private async Task EnsureRequestedMapLoadedFromStorageAsync(MapDeviceRequestedSlot requestedMap)
    {
        await EnsureRequestedMapLoadedFromSourceAsync(requestedMap, TryBuildRequestedMapStorageLoadPlanAsync);
    }

    private async Task EnsureRequestedMapLoadedFromInventoryAsync(MapDeviceRequestedSlot requestedMap)
    {
        await EnsureRequestedMapLoadedFromSourceAsync(requestedMap, TryBuildRequestedMapInventoryLoadPlanAsync);
    }

    private async Task EnsureRequestedMapLoadedFromSourceAsync(
        MapDeviceRequestedSlot requestedMap,
        Func<MapDeviceRequestedSlot, Task<MapDeviceMapLoadPlan>> buildLoadPlanAsync)
    {
        if (requestedMap == null || string.IsNullOrWhiteSpace(requestedMap.Metadata) || buildLoadPlanAsync == null)
        {
            return;
        }

        var loadPlan = await buildLoadPlanAsync(requestedMap);
        if (loadPlan == null)
        {
            return;
        }

        await TransferItemIntoMapDeviceAsync(
            requestedMap.Label,
            loadPlan.TransferAsync,
            loadPlan.WaitForTransferAsync,
            loadPlan.ThrowOnFailure);

        if (!string.IsNullOrWhiteSpace(loadPlan.SuccessLogMessage))
        {
            LogDebug(loadPlan.SuccessLogMessage);
        }
    }

    private async Task<MapDeviceMapLoadPlan> TryBuildRequestedMapStorageLoadPlanAsync(MapDeviceRequestedSlot requestedMap)
    {
        if (requestedMap == null || string.IsNullOrWhiteSpace(requestedMap.Metadata) || GetVisibleLoadedMapDeviceMatchingQuantity(requestedMap.Metadata) > 0)
        {
            return null;
        }

        var storageQuantityBefore = GetVisibleMapDeviceStorageMatchingQuantity(requestedMap.Metadata);
        if (storageQuantityBefore <= 0)
        {
            return null;
        }

        SharpDX.Vector2 storageClickTarget = default;
        string storageClickDescription = null;
        var lookup = await RetryMapDeviceClickTargetLookupAsync(
            () => TryGetMapDeviceStorageItemClickTarget(requestedMap.Metadata, out storageClickTarget, out storageClickDescription));

        if (storageClickDescription == null)
        {
            LogMapDeviceStorageLookupFailure(requestedMap.Label, requestedMap.Metadata, lookup.Attempts, storageQuantityBefore);
            return null;
        }

        var loadedQuantityBefore = GetVisibleLoadedMapDeviceMatchingQuantity(requestedMap.Metadata);
        return new(
            () => ClickAtAsync(
                storageClickTarget,
                holdCtrl: true,
                preClickDelayMs: AutomationTiming.CtrlClickPreDelayMs,
                postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs),
            () => WaitForMapDeviceStorageTransferAsync(
                requestedMap.Metadata,
                loadedQuantityBefore,
                storageQuantityBefore),
            () => ThrowMapDeviceClickTransferFailure(requestedMap.Label, "Map Device storage into the active Map Device slot", storageClickDescription),
            $"Moved {requestedMap.Label} ({requestedMap.Metadata}) from Map Device storage into the active Map Device slot. storageQtyBefore={storageQuantityBefore}, storageQtyAfter={GetVisibleMapDeviceStorageMatchingQuantity(requestedMap.Metadata)}, loadedQtyAfter={GetVisibleLoadedMapDeviceMatchingQuantity(requestedMap.Metadata)}, clickTarget={storageClickDescription}");
    }

    private Task<MapDeviceMapLoadPlan> TryBuildRequestedMapInventoryLoadPlanAsync(MapDeviceRequestedSlot requestedMap)
    {
        if (requestedMap == null || string.IsNullOrWhiteSpace(requestedMap.Metadata))
        {
            return Task.FromResult<MapDeviceMapLoadPlan>(null);
        }

        var inventoryItems = GetVisiblePlayerInventoryItemsOrThrow("Player inventory is not visible.");
        var visibleItem = FindNextMatchingStashItem(inventoryItems, requestedMap.Metadata);
        if (visibleItem?.Item == null)
        {
            return Task.FromResult<MapDeviceMapLoadPlan>(null);
        }

        var inventoryQuantityBefore = GetReadablePlayerInventoryMatchingQuantity(requestedMap.Metadata);
        return Task.FromResult(new MapDeviceMapLoadPlan(
            () => CtrlClickInventoryItemAsync(visibleItem),
            () => WaitForLoadedMapOrInventoryDropAsync(requestedMap.Metadata, inventoryQuantityBefore),
            () => ThrowMapDeviceClickTransferFailure(requestedMap.Label, "inventory into the active Map Device map slot"),
            null));
    }

    private async Task EnsureRequestedFragmentSlotLoadedAsync(MapDeviceRequestedSlot requestedItem)
    {
        if (requestedItem == null || requestedItem.IsMap || string.IsNullOrWhiteSpace(requestedItem.Metadata))
        {
            return;
        }

        while (true)
        {
            var slotItem = GetVisibleMapDeviceItemInSlot(requestedItem.SlotIndex);
            if (slotItem?.Item != null)
            {
                if (!slotItem.Item.Metadata.EqualsIgnoreCase(requestedItem.Metadata))
                {
                    throw new InvalidOperationException($"{requestedItem.Label} is already occupied by a different item. Clear the Map Device before loading it again.");
                }

                var currentSlotQuantity = GetVisibleInventoryItemStackQuantity(slotItem);
                if (currentSlotQuantity == requestedItem.ExpectedQuantity)
                {
                    return;
                }

                if (currentSlotQuantity > requestedItem.ExpectedQuantity)
                {
                    ThrowMapDeviceFragmentSlotOverfilled(requestedItem, currentSlotQuantity);
                }

                var topUpQuantity = requestedItem.ExpectedQuantity - currentSlotQuantity;
                var sourceItem = FindVisibleInventoryItemForMapDeviceTransferOrThrow(
                    requestedItem,
                    topUpQuantity,
                    allowPartialQuantity: true,
                    $"Could not find enough {requestedItem.Label} in player inventory to top up the configured slot quantity.");

                var topUpTransferQuantity = Math.Min(topUpQuantity, GetVisibleInventoryItemStackQuantity(sourceItem));
                await PlaceExactInventoryQuantityIntoLoadedMapDeviceStackAsync((requestedItem.Label, requestedItem.Metadata, false), sourceItem, topUpTransferQuantity, slotItem);
                continue;
            }

            var nextFreeSlotIndex = GetNextFreeMapDeviceFragmentSlotIndex();
            if (nextFreeSlotIndex != requestedItem.SlotIndex)
            {
                ThrowMapDeviceUnexpectedNextFreeFragmentSlot(requestedItem, nextFreeSlotIndex);
            }

            var sourceStack = FindVisibleInventoryItemForMapDeviceTransferOrThrow(
                requestedItem,
                requestedItem.ExpectedQuantity,
                allowPartialQuantity: true,
                $"Could not find enough {requestedItem.Label} in player inventory to fill the configured slot quantity of {requestedItem.ExpectedQuantity}.");

            var insertTransferQuantity = Math.Min(requestedItem.ExpectedQuantity, GetVisibleInventoryItemStackQuantity(sourceStack));
            await InsertExactInventoryQuantityIntoNewMapDeviceSlotAsync((requestedItem.Label, requestedItem.Metadata, false), sourceStack, insertTransferQuantity, requestedItem.SlotIndex);
            EnsureMapDeviceFragmentSlotLanded(requestedItem, insertTransferQuantity);
        }
    }

    private bool TryGetMapDeviceStorageItemClickTarget(string metadata, out SharpDX.Vector2 center, out string description)
    {
        center = default;
        description = null;

        if (string.IsNullOrWhiteSpace(metadata))
        {
            return false;
        }

        var visibleItem = FindNextMatchingStashItem(GetVisibleMapDeviceStorageItems(), metadata);
        if (visibleItem?.Item == null)
        {
            return false;
        }

        var rect = visibleItem.GetClientRect();
        center = rect.Center;
        description = $"map-device-storage-rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})";
        return true;
    }

    private async Task CtrlClickInventoryItemIntoMapDeviceAsync((string Label, string Metadata, bool IsMap) requestedItem, int attemptNumberForMetadata, int totalRequestedCountForMetadata, int expectedMapDeviceQuantity)
    {
        var normalizedExpectedMapDeviceQuantity = Math.Max(1, expectedMapDeviceQuantity);
        while (GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata) < normalizedExpectedMapDeviceQuantity)
        {
            if (await TryInsertExactStackableInventoryQuantityIntoMapDeviceAsync(
                    requestedItem,
                    normalizedExpectedMapDeviceQuantity,
                    GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata)))
            {
                continue;
            }

            var inventoryItems = GetVisiblePlayerInventoryItems();
            SharpDX.Vector2 inventoryClickTarget = default;
            string inventoryClickDescription = null;
            var lookup = await RetryMapDeviceClickTargetLookupAsync(
                () =>
                {
                    inventoryItems = GetVisiblePlayerInventoryItems();
                    return TryGetPlayerInventoryItemClickTarget(requestedItem.Metadata, out inventoryClickTarget, out inventoryClickDescription);
                },
                _ => GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata) >= normalizedExpectedMapDeviceQuantity);

            if (lookup.Resolved && lookup.Attempts > 1)
            {
                LogMapDeviceInventoryLookupRecovered(
                    requestedItem,
                    lookup.Attempts,
                    normalizedExpectedMapDeviceQuantity,
                    inventoryClickDescription);
            }

            if (lookup.StoppedEarly)
            {
                LogMapDeviceInventoryLookupStoppedEarly(requestedItem, lookup.Attempts);
            }

            if (GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata) >= normalizedExpectedMapDeviceQuantity)
            {
                break;
            }

            if (!lookup.Resolved)
            {
                ThrowMapDeviceInventoryLookupFailure(
                    requestedItem,
                    attemptNumberForMetadata,
                    totalRequestedCountForMetadata,
                    normalizedExpectedMapDeviceQuantity,
                    inventoryItems,
                    lookup.Attempts);
            }

            var deviceQuantityBefore = GetVisibleMapDeviceMatchingQuantity(requestedItem.Metadata);
            var inventoryQuantityBefore = GetReadablePlayerInventoryMatchingQuantity(requestedItem.Metadata);
            await TransferItemIntoMapDeviceAsync(
                requestedItem.Label,
                () => ClickAtAsync(
                    inventoryClickTarget,
                    holdCtrl: true,
                    preClickDelayMs: AutomationTiming.CtrlClickPreDelayMs,
                    postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs),
                () => WaitForMapDeviceQuantityOrInventoryDropAsync(
                    requestedItem.Metadata,
                    deviceQuantityBefore,
                    inventoryQuantityBefore),
                () => ThrowMapDeviceClickTransferFailure(requestedItem.Label, "player inventory into the Map Device", inventoryClickDescription));
        }
    }

    private async Task<bool> TryInsertExactStackableInventoryQuantityIntoMapDeviceAsync(
        (string Label, string Metadata, bool IsMap) requestedItem,
        int expectedMapDeviceQuantity,
        int currentMapDeviceQuantity)
    {
        if (requestedItem.IsMap || string.IsNullOrWhiteSpace(requestedItem.Metadata))
        {
            return false;
        }

        var inventoryItems = GetVisiblePlayerInventoryItems();
        var knownFullStackSize = GetKnownFullStackSize(inventoryItems, requestedItem.Metadata);
        if (!knownFullStackSize.HasValue)
        {
            return false;
        }

        var remainingQuantity = expectedMapDeviceQuantity - currentMapDeviceQuantity;
        if (remainingQuantity <= 0 || remainingQuantity >= knownFullStackSize.Value)
        {
            return false;
        }

        var sourceItem = FindVisibleInventoryItemForExactMapDeviceTransfer(inventoryItems, requestedItem.Metadata, remainingQuantity);
        if (sourceItem?.Item == null)
        {
            return false;
        }

        if (TryGetVisibleLoadedPartialMapDeviceItem(requestedItem.Metadata, knownFullStackSize.Value, out var partialLoadedItem))
        {
            var partialLoadedQuantity = GetVisibleInventoryItemStackQuantity(partialLoadedItem);
            var fillQuantity = Math.Min(remainingQuantity, knownFullStackSize.Value - partialLoadedQuantity);
            if (fillQuantity > 0)
            {
                await PlaceExactInventoryQuantityIntoLoadedMapDeviceStackAsync(requestedItem, sourceItem, fillQuantity, partialLoadedItem);
                return true;
            }
        }

        await InsertExactInventoryQuantityIntoNewMapDeviceSlotAsync(requestedItem, sourceItem, remainingQuantity);
        return true;
    }

    private static NormalInventoryItem FindVisibleInventoryItemForExactMapDeviceTransfer(IList<NormalInventoryItem> inventoryItems, string metadata, int requiredQuantity)
    {
        return FindVisibleInventoryItemForMapDeviceTransfer(inventoryItems, metadata, requiredQuantity, allowPartialQuantity: false);
    }

    private static NormalInventoryItem FindVisibleInventoryItemForMapDeviceTransfer(
        IList<NormalInventoryItem> inventoryItems,
        string metadata,
        int requiredQuantity,
        bool allowPartialQuantity)
    {
        if (inventoryItems == null || string.IsNullOrWhiteSpace(metadata) || requiredQuantity <= 0)
        {
            return null;
        }

        var matchingItems = inventoryItems
            .Where(item => item?.Item != null && item.Item.Metadata.EqualsIgnoreCase(metadata))
            .Select(item => new
            {
                Item = item,
                Quantity = GetVisibleInventoryItemStackQuantity(item),
            })
            .ToList();

        var exactOrLargerMatch = matchingItems
            .Where(x => x.Quantity >= requiredQuantity)
            .OrderBy(x => x.Quantity == requiredQuantity ? 0 : 1)
            .ThenBy(x => x.Quantity)
            .ThenBy(x => x.Item.GetClientRect().Top)
            .ThenBy(x => x.Item.GetClientRect().Left)
            .Select(x => x.Item)
            .FirstOrDefault();
        if (exactOrLargerMatch != null || !allowPartialQuantity)
        {
            return exactOrLargerMatch;
        }

        return matchingItems
            .Where(x => x.Quantity > 0)
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.Item.GetClientRect().Top)
            .ThenBy(x => x.Item.GetClientRect().Left)
            .Select(x => x.Item)
            .FirstOrDefault();
    }

    private static int GetVisibleInventoryItemStackQuantity(NormalInventoryItem item)
    {
        return Math.Max(1, item?.Item?.GetComponent<ExileCore.PoEMemory.Components.Stack>()?.Size ?? 1);
    }

    private bool CanUseDirectCtrlClickForMapDeviceSlotInsert(string metadata, int targetSlotIndex, int? knownFullStackSize)
    {
        if (targetSlotIndex < 0 || string.IsNullOrWhiteSpace(metadata))
        {
            return true;
        }

        for (var slotIndex = 1; slotIndex < targetSlotIndex; slotIndex++)
        {
            var slotItem = GetVisibleMapDeviceItemInSlot(slotIndex);
            if (slotItem?.Item?.Metadata.EqualsIgnoreCase(metadata) != true)
            {
                continue;
            }

            if (!knownFullStackSize.HasValue)
            {
                return false;
            }

            if (GetVisibleInventoryItemStackQuantity(slotItem) < knownFullStackSize.Value)
            {
                return false;
            }
        }

        return true;
    }

    private int GetVisibleMapDeviceSlotIndex(NormalInventoryItem targetItem)
    {
        if (targetItem?.Item == null)
        {
            return -1;
        }

        var targetRect = targetItem.GetClientRect();
        var slotItems = GetVisibleMapDeviceSlotItems();
        for (var slotIndex = 0; slotIndex < slotItems.Count; slotIndex++)
        {
            var slotItem = slotItems[slotIndex];
            if (ReferenceEquals(slotItem, targetItem))
            {
                return slotIndex;
            }

            if (slotItem?.Item == null || !slotItem.Item.Metadata.EqualsIgnoreCase(targetItem.Item.Metadata))
            {
                continue;
            }

            var slotRect = slotItem.GetClientRect();
            if (slotRect.Left == targetRect.Left &&
                slotRect.Top == targetRect.Top &&
                slotRect.Right == targetRect.Right &&
                slotRect.Bottom == targetRect.Bottom)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private bool CanUseDirectCtrlClickForLoadedMapDeviceTopUp(string metadata, NormalInventoryItem targetLoadedItem, int? knownFullStackSize)
    {
        if (targetLoadedItem?.Item == null || string.IsNullOrWhiteSpace(metadata) || !knownFullStackSize.HasValue)
        {
            return false;
        }

        var targetSlotIndex = GetVisibleMapDeviceSlotIndex(targetLoadedItem);
        if (targetSlotIndex < 0)
        {
            return false;
        }

        for (var slotIndex = 1; slotIndex <= MapDeviceFragmentSlotCount; slotIndex++)
        {
            var slotItem = GetVisibleMapDeviceItemInSlot(slotIndex);
            if (slotItem?.Item?.Metadata.EqualsIgnoreCase(metadata) != true)
            {
                continue;
            }

            if (GetVisibleInventoryItemStackQuantity(slotItem) < knownFullStackSize.Value)
            {
                return slotIndex == targetSlotIndex;
            }
        }

        return false;
    }

    private bool TryGetVisibleLoadedPartialMapDeviceItem(string metadata, int fullStackSize, out NormalInventoryItem partialLoadedItem)
    {
        partialLoadedItem = null;
        if (string.IsNullOrWhiteSpace(metadata) || fullStackSize <= 1)
        {
            return false;
        }

        partialLoadedItem = GetVisibleMapDeviceItems()
            .Where(item => item?.Item != null && item.Item.Metadata.EqualsIgnoreCase(metadata))
            .Select(item => new
            {
                Item = item,
                Quantity = GetVisibleInventoryItemStackQuantity(item),
            })
            .Where(x => x.Quantity > 0 && x.Quantity < fullStackSize)
            .OrderByScreenPosition(x => x.Item.GetClientRect())
            .Select(x => x.Item)
            .FirstOrDefault();

        return partialLoadedItem?.Item != null;
    }

    private async Task PlaceExactInventoryQuantityIntoLoadedMapDeviceStackAsync(
        (string Label, string Metadata, bool IsMap) requestedItem,
        NormalInventoryItem sourceItem,
        int transferQuantity,
        NormalInventoryItem targetLoadedItem)
    {
        var sourceStackQuantity = GetVisibleInventoryItemStackQuantity(sourceItem);
        var loadedQuantityBefore = GetVisibleLoadedMapDeviceMatchingQuantity(requestedItem.Metadata);
        var targetStackQuantityBefore = GetVisibleInventoryItemStackQuantity(targetLoadedItem);
        var knownFullStackSize = GetKnownFullStackSize(targetLoadedItem?.Item, requestedItem.Metadata)
                                 ?? GetKnownFullStackSize(sourceItem?.Item, requestedItem.Metadata);
        var canUseDirectCtrlClick = sourceStackQuantity == transferQuantity &&
                                    CanUseDirectCtrlClickForLoadedMapDeviceTopUp(requestedItem.Metadata, targetLoadedItem, knownFullStackSize);

        UpdateMapDeviceLoadingStatus(requestedItem.Label);

        if (canUseDirectCtrlClick)
        {
            LogDebug(
                $"Using direct ctrl-click Map Device top-up. metadata='{requestedItem.Metadata}', transferQuantity={transferQuantity}, " +
                $"sourceStack={sourceStackQuantity}, targetStackBefore={targetStackQuantityBefore}");

            await CtrlClickInventoryItemAsync(sourceItem);
        }
        else
        {
            LogDebug(
                $"Using exact stackable Map Device top-up. metadata='{requestedItem.Metadata}', transferQuantity={transferQuantity}, " +
                $"sourceStack={sourceStackQuantity}, targetStackBefore={targetStackQuantityBefore}");

            await PickUpExactMapDeviceTransferStackAsync(sourceItem, sourceStackQuantity, transferQuantity);

            await ClickAtAsync(
                targetLoadedItem.GetClientRect().Center,
                holdCtrl: false,
                preClickDelayMs: Math.Max(AutomationTiming.UiClickPreDelayMs, 100),
                postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs);
        }

        var inserted = await WaitForLoadedMapDeviceQuantityAtLeastAsync(
            requestedItem.Metadata,
            loadedQuantityBefore + transferQuantity);
        if (!inserted)
        {
            throw new InvalidOperationException(
                $"Failed to top up the existing Map Device stack for {requestedItem.Label}. transferQuantity={transferQuantity}");
        }
    }

    private async Task PickUpExactMapDeviceTransferStackAsync(
        NormalInventoryItem sourceItem,
        int sourceStackQuantity,
        int transferQuantity)
    {
        if (sourceStackQuantity == transferQuantity)
        {
            await ClickAtAsync(
                sourceItem.GetClientRect().Center,
                holdCtrl: false,
                preClickDelayMs: AutomationTiming.UiClickPreDelayMs,
                postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs);
            return;
        }

        await ShiftClickInventoryItemAsync(sourceItem);
        await InputCurrencyShiftClickQuantityAsync(transferQuantity);
        await DelayForUiCheckAsync(100);
    }

    private void UpdateMapDeviceLoadingStatus(string label)
    {
        UpdateAutomationStatus($"Loading Map Device: {label}");
    }

    private async Task InsertExactInventoryQuantityIntoNewMapDeviceSlotAsync(
        (string Label, string Metadata, bool IsMap) requestedItem,
        NormalInventoryItem sourceItem,
        int transferQuantity,
        int targetSlotIndex = -1)
    {
        var sourceStackQuantity = GetVisibleInventoryItemStackQuantity(sourceItem);
        var loadedQuantityBefore = GetVisibleLoadedMapDeviceMatchingQuantity(requestedItem.Metadata);
        var slotStateBefore = DescribeMapDeviceLoadedSlotState();
        var knownFullStackSize = GetKnownFullStackSize(sourceItem?.Item, requestedItem.Metadata);
        var canUseDirectCtrlClick = CanUseDirectCtrlClickForMapDeviceSlotInsert(requestedItem.Metadata, targetSlotIndex, knownFullStackSize);

        LogDebug(
            $"Preparing exact new-slot Map Device insert. metadata='{requestedItem.Metadata}', transferQuantity={transferQuantity}, " +
            $"sourceStack={sourceStackQuantity}, slotStateBefore={slotStateBefore}");

        if (canUseDirectCtrlClick && sourceStackQuantity == transferQuantity)
        {
            LogDebug(
                $"Using direct ctrl-click Map Device insert. metadata='{requestedItem.Metadata}', transferQuantity={transferQuantity}, " +
                $"sourceStack={sourceStackQuantity}");

            UpdateMapDeviceLoadingStatus(requestedItem.Label);
            await CtrlClickInventoryItemAsync(sourceItem);

            var insertedExactStack = await WaitForLoadedMapDeviceQuantityAtLeastAsync(
                requestedItem.Metadata,
                loadedQuantityBefore + transferQuantity);
            if (!insertedExactStack)
            {
                ThrowMapDeviceNewSlotTransferFailure(
                    requestedItem,
                    transferQuantity,
                    slotStateBefore,
                    $"Failed to move the exact stack for {requestedItem.Label} into a new Map Device slot. transferQuantity={transferQuantity}");
            }

            return;
        }

        if (!canUseDirectCtrlClick && targetSlotIndex >= 0)
        {
            await PlaceExactInventoryQuantityIntoSpecificMapDeviceSlotAsync(
                requestedItem,
                sourceItem,
                transferQuantity,
                targetSlotIndex,
                slotStateBefore,
                sourceStackQuantity);
            return;
        }

        var expectedLandingSlots = GetPlayerInventoryNextFreeCells(1);
        if (expectedLandingSlots.Count <= 0)
        {
            throw new InvalidOperationException(
                $"No free inventory slot found to split {transferQuantity} of {requestedItem.Label} before loading the Map Device.");
        }

        var targetInventoryCell = expectedLandingSlots[0];
        var expectedSlotFillBeforePlacement = CountOccupiedPlayerInventoryCells(expectedLandingSlots);

        LogDebug(
            $"Using exact stack split for Map Device load. metadata='{requestedItem.Metadata}', transferQuantity={transferQuantity}, " +
            $"sourceStack={sourceStackQuantity}, splitCell=({targetInventoryCell.X},{targetInventoryCell.Y})");

        await PickUpExactMapDeviceTransferStackAsync(sourceItem, sourceStackQuantity, transferQuantity);
        await PlaceItemIntoPlayerInventoryCellAsync(targetInventoryCell.X, targetInventoryCell.Y);

        var placedInInventory = await WaitForBestiaryConditionAsync(
            () => CountOccupiedPlayerInventoryCells(expectedLandingSlots) > expectedSlotFillBeforePlacement,
            MapDeviceTransferTimeoutMs,
            Math.Max(10, AutomationTiming.FastPollDelayMs));
        if (!placedInInventory)
        {
            throw new InvalidOperationException(
                $"Failed to place the split stack for {requestedItem.Label} into inventory cell ({targetInventoryCell.X},{targetInventoryCell.Y}).");
        }

        var targetCellCenter = TryGetPlayerInventoryCellCenter(targetInventoryCell.X, targetInventoryCell.Y);
        if (!targetCellCenter.HasValue)
        {
            throw new InvalidOperationException(
                $"Could not resolve the split inventory cell center for {requestedItem.Label} at ({targetInventoryCell.X},{targetInventoryCell.Y}).");
        }

        var inventoryQuantityBefore = GetReadablePlayerInventoryMatchingQuantity(requestedItem.Metadata);
        await TransferItemIntoMapDeviceAsync(
            requestedItem.Label,
            () => ClickAtAsync(
                targetCellCenter.Value,
                holdCtrl: true,
                preClickDelayMs: AutomationTiming.CtrlClickPreDelayMs,
                postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs),
            () => WaitForMapDeviceQuantityOrInventoryDropAsync(
                requestedItem.Metadata,
                loadedQuantityBefore,
                inventoryQuantityBefore),
            () => ThrowMapDeviceNewSlotTransferFailure(
                requestedItem,
                transferQuantity,
                slotStateBefore,
                $"Failed to move the split stack for {requestedItem.Label} into a new Map Device slot. transferQuantity={transferQuantity}"));
    }

    private async Task PlaceExactInventoryQuantityIntoSpecificMapDeviceSlotAsync(
        (string Label, string Metadata, bool IsMap) requestedItem,
        NormalInventoryItem sourceItem,
        int transferQuantity,
        int targetSlotIndex,
        string slotStateBefore,
        int sourceStackQuantity)
    {
        var slotCenter = GetVisibleMapDeviceSlotCenter(targetSlotIndex);
        if (!slotCenter.HasValue)
        {
            throw new InvalidOperationException(
                $"Could not resolve the target Map Device slot center for {requestedItem.Label} at Slot {targetSlotIndex + 1}.");
        }

        LogDebug(
            $"Using slot-targeted Map Device insert to avoid earlier partial-stack auto-merge. metadata='{requestedItem.Metadata}', transferQuantity={transferQuantity}, " +
            $"sourceStack={sourceStackQuantity}, targetSlot={targetSlotIndex + 1}");

        UpdateMapDeviceLoadingStatus(requestedItem.Label);

        await PickUpExactMapDeviceTransferStackAsync(sourceItem, sourceStackQuantity, transferQuantity);

        await ClickAtAsync(
            slotCenter.Value,
            holdCtrl: false,
            preClickDelayMs: Math.Max(AutomationTiming.UiClickPreDelayMs, 100),
            postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs);

        var inserted = await WaitForSpecificMapDeviceSlotQuantityAsync(
            requestedItem.Metadata,
            targetSlotIndex,
            transferQuantity);
        if (!inserted)
        {
            ThrowMapDeviceNewSlotTransferFailure(
                requestedItem,
                transferQuantity,
                slotStateBefore,
                $"Failed to place the targeted stack for {requestedItem.Label} into Map Device Slot {targetSlotIndex + 1}. transferQuantity={transferQuantity}");
        }
    }

    private void LogMapDeviceNewSlotInsertTimeout(
        (string Label, string Metadata, bool IsMap) requestedItem,
        int transferQuantity,
        string slotStateBefore)
    {
        var slotStateAfter = DescribeMapDeviceLoadedSlotState();
        var visibleInventoryMatches = DescribeVisibleInventoryMatches(GetVisiblePlayerInventoryItems(), requestedItem.Metadata);

        LogDebug(
            $"Map Device new-slot insert timed out. metadata='{requestedItem.Metadata}', transferQuantity={transferQuantity}, " +
            $"slotStateBefore={slotStateBefore}, slotStateAfter={slotStateAfter}, visibleInventoryMatches={visibleInventoryMatches}");
    }

    private static string DescribeVisibleInventoryMatches(IList<NormalInventoryItem> inventoryItems, string metadata)
    {
        if (inventoryItems == null || string.IsNullOrWhiteSpace(metadata))
        {
            return "none";
        }

        var matches = inventoryItems
            .Where(item => item?.Item != null && item.Item.Metadata.EqualsIgnoreCase(metadata))
            .Select(item =>
            {
                var rect = item.GetClientRect();
                var stackSize = GetVisibleInventoryItemStackQuantity(item);
                return $"stack={stackSize}, rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})";
            })
            .ToList();

        return matches.Count > 0 ? string.Join(" | ", matches) : "none";
    }

}
