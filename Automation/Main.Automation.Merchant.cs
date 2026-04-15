using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using SharpDX;

namespace BeastsV2;

public partial class Main
{
    #region Faustus merchant automation

    private const string FaustusHideoutMetadata = "Metadata/NPC/League/Kalguur/VillageFaustusHideout";
    private const int OfflineMerchantShopTabColumns = 12;
    private const int OfflineMerchantShopTabRows = 12;
    private static readonly int[] OfflineMerchantShopTabsPath = [2, 0, 0, 1, 1, 0, 0, 1, 0];
    private static readonly int[] OfflineMerchantShopTabTextPath = [0, 1];
    private static readonly int[] MerchantPopupEnteredPriceTextPath = [2, 0, 0];

    private async Task RunFaustusListBodyAsync(CancellationToken cancellationToken = default) => await MerchantWorkflow.RunListingBodyAsync(cancellationToken);

    private async Task EnsureFaustusListingContextReadyAsync(string configuredTabName, bool merchantPanelWasVisible)
    {
        if (!await EnsureFaustusMerchantPanelOpenAsync())
        {
            throw new InvalidOperationException("Faustus merchant panel closed while listing itemized beasts.");
        }

        if (!merchantPanelWasVisible && !await EnsureOfflineMerchantShopInventorySelectedAsync())
        {
            throw new InvalidOperationException("Could not keep Faustus on the Shop inventory while listing itemized beasts.");
        }

        if (!merchantPanelWasVisible)
        {
            await SelectOfflineMerchantTabAsync(configuredTabName);
        }
    }

    private async Task<MerchantListingAttemptResult> TryListCapturedMonsterAndConfirmAsync(NormalInventoryItem item, int listingPriceChaos)
    {
        var previousCount = GetVisibleCapturedMonsterInventoryItems().Count;

        var clickItemStopwatch = Stopwatch.StartNew();
        await CtrlClickInventoryItemAsync(item);
        var clickItemElapsedMs = clickItemStopwatch.ElapsedMilliseconds;

        var popupWaitStopwatch = Stopwatch.StartNew();
        var popupOpened = await WaitForMerchantPopupVisibilityAsync(expectedVisible: true, 1500);
        var popupWaitElapsedMs = popupWaitStopwatch.ElapsedMilliseconds;
        if (!popupOpened)
        {
            return new(false, previousCount, previousCount, clickItemElapsedMs, popupWaitElapsedMs, 0, 0, 0, 0, 0, 0, 0, false);
        }

        var enterPriceStopwatch = Stopwatch.StartNew();
        var priceEntry = await EnterMerchantListingPriceAsync(listingPriceChaos);
        var enterPriceElapsedMs = enterPriceStopwatch.ElapsedMilliseconds;
        var submitCloseElapsedMs = priceEntry.SubmitCloseMs;

        var countChangeStopwatch = Stopwatch.StartNew();
        var currentCount = await WaitForCapturedMonsterInventoryItemCountToChangeAsync(previousCount);
        var countChangeFallbackUsed = false;
        if (currentCount >= previousCount)
        {
            countChangeFallbackUsed = true;

            if (IsMerchantPopupVisible())
            {
                var submitCloseStopwatch = Stopwatch.StartNew();
                if (!await WaitForMerchantPopupVisibilityAsync(expectedVisible: false, 1000))
                {
                    throw new InvalidOperationException("Timed out closing the Faustus price popup.");
                }

                submitCloseElapsedMs = submitCloseStopwatch.ElapsedMilliseconds;
                currentCount = GetVisibleCapturedMonsterInventoryItems().Count;
            }

            if (currentCount >= previousCount)
            {
                await DelayForUiCheckAsync(250);
                currentCount = GetVisibleCapturedMonsterInventoryItems().Count;
            }
        }
        var countChangeElapsedMs = countChangeStopwatch.ElapsedMilliseconds;

        return new(
            true,
            previousCount,
            currentCount,
            clickItemElapsedMs,
            popupWaitElapsedMs,
            enterPriceElapsedMs,
            priceEntry.PopupReadyMs,
            priceEntry.SelectAndClearMs,
            priceEntry.TypeDigitsMs,
            priceEntry.ConfirmTextMs,
            submitCloseElapsedMs,
            countChangeElapsedMs,
            countChangeFallbackUsed);
    }

    private async Task RunSellCapturedMonstersToFaustusAsync()
    {
        while ((Control.MouseButtons & MouseButtons.Left) != 0)
        {
            await Task.Delay(10);
        }

        await RunQueuedAutomationAsync(
            RunFaustusListBodyAsync,
            "Faustus beast listing",
            cancelledStatus: "Faustus beast listing cancelled.",
            uiCleanupOptions: new AutomationUiCleanupOptions(KeepInventory: true, KeepMerchant: true));
    }

    private void ReleaseAutomationTriggerKeys()
    {
        ReleaseAutomationModifierKeys();
        ReleaseAutomationKeys(Keys.Menu, Keys.LMenu, Keys.RMenu);
    }

    private MerchantListingCandidate GetNextSellableCapturedMonsterInventoryItem()
    {
        return GetSellableCapturedMonsterInventoryItems(logMissingPrice: true).FirstOrDefault();
    }

    private IReadOnlyList<MerchantListingCandidate> GetSellableCapturedMonsterInventoryItems(bool logMissingPrice)
    {
        var candidates = new List<MerchantListingCandidate>();
        foreach (var inventoryItem in GetVisibleCapturedMonsterInventoryItems())
        {
            if (TryBuildMerchantListingCandidate(inventoryItem, logMissingPrice, out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private bool TryBuildMerchantListingCandidate(NormalInventoryItem inventoryItem, bool logMissingPrice, out MerchantListingCandidate candidate)
    {
        candidate = null;

        var beastName = GetCapturedMonsterInventoryItemName(inventoryItem);
        if (string.IsNullOrWhiteSpace(beastName))
        {
            return false;
        }

        if (!_beastPrices.TryGetValue(beastName, out var priceChaos) || priceChaos <= 0)
        {
            if (logMissingPrice)
            {
                LogDebug($"Skipping itemized beast '{beastName}' because no beast price data is available.");
            }

            return false;
        }

        var multiplier = Math.Clamp(Settings?.MerchantAutomation?.FaustusPriceMultiplier?.Value ?? 1f, 0.5f, 1.5f);
        var listingPriceChaos = Math.Max(1, (int)Math.Ceiling(priceChaos * multiplier));
        candidate = new MerchantListingCandidate(inventoryItem, beastName, listingPriceChaos);
        return true;
    }

    private bool TryGetNextSellableCapturedMonsterInventoryItem(out NormalInventoryItem item, out string beastName, out int listingPriceChaos)
    {
        item = null;
        beastName = null;
        listingPriceChaos = 0;

        foreach (var candidate in GetVisibleCapturedMonsterInventoryItems())
        {
            if (TryBuildMerchantListingCandidate(candidate, logMissingPrice: true, out var listingCandidate))
            {
                item = listingCandidate.Item;
                beastName = listingCandidate.BeastName;
                listingPriceChaos = listingCandidate.ListingPriceChaos;
                return true;
            }
        }

        return false;
    }

    private static string GetCapturedMonsterInventoryItemName(NormalInventoryItem item)
    {
        if (item?.Item == null)
        {
            return null;
        }

        var capturedMonster = item.Item.GetComponent<CapturedMonster>();
        return TryGetPropertyValueAsString(capturedMonster?.MonsterVariety, "MonsterName")?.Trim()
               ?? TryGetPropertyValueAsString(capturedMonster?.MonsterVariety, "Name")?.Trim()
               ?? item.Item.GetComponent<Base>()?.Name?.Trim();
    }

    private async Task<bool> EnsureFaustusMerchantPanelOpenAsync()
    {
        return await EnsureAutomationOpenWithRetryAsync(
            () => GetOfflineMerchantPanel()?.IsVisible == true,
            4000,
            AutomationTiming.StashOpenPollDelayMs,
            async () =>
            {
                if (await WaitForBestiaryConditionAsync(() => GetOfflineMerchantPanel()?.IsVisible == true, 400, 25))
                {
                    return AutomationOpenRetryResult.Continue;
                }

                if (!await TryAdvanceWorldEntityOpenStepAsync(
                        () => FindNearestAutomationEntity(
                            entity => entity.Metadata.EqualsIgnoreCase(FaustusHideoutMetadata),
                            requireVisible: true),
                        () => FindNearestAutomationEntity(
                            entity => entity.Metadata.EqualsIgnoreCase(FaustusHideoutMetadata),
                            requireVisible: false),
                        "Faustus",
                        "Faustus shop",
                        "Could not find Faustus in the current area.",
                        null,
                        CtrlAltClickWorldEntityAsync))
                {
                    return AutomationOpenRetryResult.Abort;
                }

                await WaitForBestiaryConditionAsync(() => GetOfflineMerchantPanel()?.IsVisible == true, 1000, 25);
                return AutomationOpenRetryResult.Continue;
            });
    }

    private async Task EnsureTravelToHideoutAsync()
    {
        if (GameController?.Area?.CurrentArea?.IsHideout == true)
        {
            return;
        }

        if (!await TryTravelViaChatCommandAsync(
                "/hideout",
                "hideout",
                () => GameController?.Area?.CurrentArea?.IsHideout == true,
                10000))
        {
            throw new InvalidOperationException("Timed out travelling to hideout before opening Faustus shop.");
        }
    }

    private async Task<bool> CtrlAltClickWorldEntityAsync(Entity entity, string statusMessage)
    {
        return await TryInteractWithWorldEntityAsync(
            entity,
            DescribeEntity(entity),
            statusMessage,
            "Could not find a clickable world position for Faustus.",
            "Could not hover Faustus.",
            MouseButtons.Left,
            AutomationTiming.OpenStashPostClickDelayMs,
            AutomationTiming.KeyTapDelayMs,
            Keys.LControlKey,
            Keys.LMenu);
    }

    private StashElement GetOfflineMerchantPanel() => GameController?.IngameState?.IngameUi?.OfflineMerchantPanel;

    private async Task<bool> EnsureOfflineMerchantShopInventorySelectedAsync()
    {
        var panel = GetOfflineMerchantPanel();
        if (panel?.IsVisible != true)
        {
            return false;
        }

        var shopInventoryIndex = ResolveOfflineMerchantInventoryIndex(panel, "Shop");
        if (shopInventoryIndex >= 0 && IsOfflineMerchantInventoryReady(panel, shopInventoryIndex))
        {
            LogDebug($"EnsureOfflineMerchantShopInventorySelectedAsync skipping because merchant inventory '{GetOfflineMerchantInventoryName(panel, shopInventoryIndex)}' is already visible.");
            return true;
        }

        var inventoriesRoot = TryGetPropertyValue<Element>(panel, "Inventories") ?? panel;
        var shopInventory = FindOfflineMerchantInventorySwitch(inventoriesRoot, "Shop");
        if (shopInventory == null)
        {
            return panel.VisibleStash != null && (shopInventoryIndex < 0 || IsOfflineMerchantInventoryReady(panel, shopInventoryIndex));
        }

        var tabButton = TryGetPropertyValue<Element>(shopInventory, "TabButton") ?? shopInventory;
        return await ClickElementAndConfirmAsync(
            tabButton,
            AutomationTiming.UiClickPreDelayMs,
            Math.Max(AutomationTiming.MinTabClickPostDelayMs, GetConfiguredTabSwitchDelayMs()),
            () => WaitForBestiaryConditionAsync(
                () => IsOfflineMerchantInventoryReady(GetOfflineMerchantPanel(), shopInventoryIndex),
                1000,
                Math.Max(AutomationTiming.FastPollDelayMs, 25)));
    }

    private static int ResolveOfflineMerchantInventoryIndex(StashElement panel, string inventoryName)
    {
        if (panel?.Inventories == null || string.IsNullOrWhiteSpace(inventoryName))
        {
            return -1;
        }

        for (var index = 0; index < panel.Inventories.Count; index++)
        {
            if (panel.Inventories[index]?.TabName.EqualsIgnoreCase(inventoryName) == true)
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetOfflineMerchantInventoryName(StashElement panel, int inventoryIndex)
    {
        if (panel?.Inventories == null || inventoryIndex < 0 || inventoryIndex >= panel.Inventories.Count)
        {
            return null;
        }

        return panel.Inventories[inventoryIndex]?.TabName?.Trim();
    }

    private static bool IsOfflineMerchantInventoryReady(StashElement panel, int inventoryIndex)
    {
        if (panel?.IsVisible != true || panel.VisibleStash == null)
        {
            return false;
        }

        if (inventoryIndex < 0)
        {
            return true;
        }

        var expectedInventory = panel.Inventories != null && inventoryIndex < panel.Inventories.Count
            ? panel.Inventories[inventoryIndex]?.Inventory
            : null;
        if (expectedInventory?.Address > 0 && panel.VisibleStash.Address > 0)
        {
            return expectedInventory.Address == panel.VisibleStash.Address;
        }

        return panel.IndexVisibleStash == inventoryIndex;
    }

    private static List<(string Name, Element Element)> GetOrderedOfflineMerchantShopTabs(StashElement panel)
    {
        var tabsRoot = TryGetElementByPathQuietly(panel, OfflineMerchantShopTabsPath);
        if (tabsRoot?.Children == null)
        {
            return [];
        }

        return tabsRoot.Children
            .Where(child => child?.IsVisible == true)
            .Select(child => (Name: GetOfflineMerchantShopTabName(child), Element: child))
            .Where(tab => !string.IsNullOrWhiteSpace(tab.Name))
            .OrderBy(tab => GetOfflineMerchantShopTabClickTarget(tab.Element).GetClientRect().Left)
            .ToList();
    }

    private static Element GetOfflineMerchantShopTabClickTarget(Element tabElement)
    {
        return TryGetPropertyValue<Element>(tabElement, "TabButton")
               ?? tabElement?.Children?.FirstOrDefault()
               ?? tabElement;
    }

    private static string GetCurrentOfflineMerchantShopTabName(StashElement panel)
    {
        var shopInventory = ResolveOfflineMerchantInventory(panel, "Shop");
        if (shopInventory?.IsVisible != true)
        {
            return null;
        }

        var orderedTabs = GetOrderedOfflineMerchantShopTabs(panel);
        var currentTabIndex = shopInventory.NestedVisibleInventoryIndex;
        if (!currentTabIndex.HasValue || currentTabIndex.Value < 0 || currentTabIndex.Value >= orderedTabs.Count)
        {
            return null;
        }

        return orderedTabs[currentTabIndex.Value].Name;
    }

    private static bool IsOfflineMerchantShopTabReady(StashElement panel, string tabName)
    {
        return !string.IsNullOrWhiteSpace(tabName)
               && GetCurrentOfflineMerchantShopTabName(panel).EqualsIgnoreCase(tabName);
    }

    private static Inventory ResolveOfflineMerchantInventory(StashElement panel, string inventoryName)
    {
        var inventoryIndex = ResolveOfflineMerchantInventoryIndex(panel, inventoryName);
        if (inventoryIndex < 0 || panel?.Inventories == null || inventoryIndex >= panel.Inventories.Count)
        {
            return null;
        }

        return panel.Inventories[inventoryIndex]?.Inventory;
    }

    private static Element FindOfflineMerchantInventorySwitch(Element inventoriesRoot, string tabName)
    {
        if (inventoriesRoot == null || string.IsNullOrWhiteSpace(tabName))
        {
            return null;
        }

        return EnumerateDescendants(inventoriesRoot, includeSelf: true)
            .FirstOrDefault(element =>
                TryGetPropertyValueAsString(element, "TabName").EqualsIgnoreCase(tabName) ||
                GetElementTextRecursive(element, 2)?.Trim().EqualsIgnoreCase(tabName) == true);
    }

    private string ResolveConfiguredFaustusShopTabName()
    {
        var panel = GetOfflineMerchantPanel();
        if (panel?.IsVisible != true)
        {
            LogDebug("ResolveConfiguredFaustusShopTabName aborted because Faustus merchant panel is not visible.");
            return null;
        }

        var configuredTabName = Settings?.MerchantAutomation?.SelectedFaustusShopTabName.Value?.Trim();
        var tabNames = GetAvailableOfflineMerchantShopTabNames(panel);
        if (!string.IsNullOrWhiteSpace(configuredTabName))
        {
            if (tabNames.Any(name => name.EqualsIgnoreCase(configuredTabName)))
            {
                return configuredTabName;
            }

            LogDebug($"Configured Faustus shop tab '{configuredTabName}' was not found. Available tabs: {string.Join(", ", tabNames.Select((name, index) => $"{index}:{name}"))}");
        }
        else
        {
            LogDebug($"No configured Faustus shop tab. Available tabs: {string.Join(", ", tabNames.Select((name, index) => $"{index}:{name}"))}");
        }

        return null;
    }

    private static List<string> GetAvailableOfflineMerchantShopTabNames(StashElement panel)
    {
        return GetOrderedOfflineMerchantShopTabs(panel)
            .Select(tab => tab.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetOfflineMerchantShopTabName(Element tabElement)
    {
        return TryGetAutomationElementText(TryGetChildFromIndicesQuietly(tabElement, OfflineMerchantShopTabTextPath));
    }

    private IList<NormalInventoryItem> GetVisibleOfflineMerchantInventoryItems()
    {
        return GetOfflineMerchantPanel()?.VisibleStash?.VisibleInventoryItems;
    }

    private int GetVisibleOfflineMerchantInventoryItemCount()
    {
        return GetVisibleOfflineMerchantInventoryItems()?.Count ?? 0;
    }

    private void EnsureOfflineMerchantTabCanFitSellableCapturedMonsters()
    {
        var failureMessage = BuildOfflineMerchantListingCapacityFailureMessage();
        if (string.IsNullOrWhiteSpace(failureMessage))
        {
            return;
        }

        UpdateAutomationStatus(failureMessage, forceLog: true);
        throw new InvalidOperationException(failureMessage);
    }

    private string BuildOfflineMerchantListingCapacityFailureMessage(int additionalExpectedBeastCount = 0)
    {
        var occupiedCells = GetOfflineMerchantOccupiedCells();
        if (occupiedCells == null)
        {
            return "Could not inspect Faustus shop tab capacity.";
        }

        var requiredFootprints = GetSellableCapturedMonsterInventoryItems(logMissingPrice: false)
            .Select(candidate => GetVisibleInventoryItemFootprint(candidate.Item))
            .ToList();
        for (var index = 0; index < Math.Max(0, additionalExpectedBeastCount); index++)
        {
            requiredFootprints.Add((1, 1));
        }

        if (requiredFootprints.Count <= 0)
        {
            return null;
        }

        if (CanFitItemFootprintsInGrid(occupiedCells, OfflineMerchantShopTabColumns, OfflineMerchantShopTabRows, requiredFootprints))
        {
            LogDebug($"Faustus listing preflight passed. requiredItems={requiredFootprints.Count}, requiredCells={CountRequiredGridCells(requiredFootprints)}, freeCells={CountFreeGridCells(occupiedCells, OfflineMerchantShopTabColumns, OfflineMerchantShopTabRows)}");
            return null;
        }

        var configuredTabName = Settings?.MerchantAutomation?.SelectedFaustusShopTabName.Value?.Trim();
        var freeCells = CountFreeGridCells(occupiedCells, OfflineMerchantShopTabColumns, OfflineMerchantShopTabRows);
        var requiredCells = CountRequiredGridCells(requiredFootprints);
        var spaceDetail = freeCells < requiredCells
            ? $"needs {requiredCells} shop cells but only {freeCells} are free"
            : $"has {freeCells} free cells, but the remaining layout cannot fit the required item footprints ({requiredCells} total cells)";
        var tabDescription = string.IsNullOrWhiteSpace(configuredTabName)
            ? "the selected Faustus shop tab"
            : $"Faustus shop tab '{configuredTabName}'";
        return $"Faustus listing preflight failed. {tabDescription} {spaceDetail}.";
    }

    private bool IsCurrentOfflineMerchantTabFull()
    {
        return !HasOfflineMerchantSpaceForItem(1, 1);
    }

    private bool HasOfflineMerchantSpaceForItem(int requiredWidth, int requiredHeight)
    {
        requiredWidth = Math.Max(1, requiredWidth);
        requiredHeight = Math.Max(1, requiredHeight);

        var occupied = GetOfflineMerchantOccupiedCells();
        if (occupied == null)
        {
            return false;
        }

        return CanFitItemFootprintsInGrid(
            occupied,
            OfflineMerchantShopTabColumns,
            OfflineMerchantShopTabRows,
            new[] { (requiredWidth, requiredHeight) });
    }

    private bool[,] GetOfflineMerchantOccupiedCells()
    {
        var items = GetVisibleOfflineMerchantInventoryItems();
        if (items == null)
        {
            return null;
        }

        return BuildVisibleItemOccupiedCells(items, OfflineMerchantShopTabColumns, OfflineMerchantShopTabRows);
    }

    private async Task SelectOfflineMerchantTabAsync(string tabName)
    {
        var panel = GetOfflineMerchantPanel();
        if (panel?.IsVisible != true)
        {
            throw new InvalidOperationException("Faustus merchant panel is not open.");
        }

        if (string.IsNullOrWhiteSpace(tabName))
        {
            throw new InvalidOperationException("Select a valid Faustus shop tab before listing itemized beasts.");
        }

        var currentTabName = GetCurrentOfflineMerchantShopTabName(panel);
        if (currentTabName.EqualsIgnoreCase(tabName))
        {
            LogDebug($"SelectOfflineMerchantTabAsync skipping because Faustus shop tab '{currentTabName}' is already visible.");
            return;
        }

        var orderedTabs = GetOrderedOfflineMerchantShopTabs(panel);
        var tabElement = orderedTabs
            .FirstOrDefault(tab => tab.Name.EqualsIgnoreCase(tabName))
            .Element;
        if (tabElement == null)
        {
            throw new InvalidOperationException($"Could not find the Faustus shop tab '{tabName}'.");
        }

        var clickTarget = GetOfflineMerchantShopTabClickTarget(tabElement);
        var switched = await ClickElementAndConfirmAsync(
            clickTarget,
            AutomationTiming.UiClickPreDelayMs,
            Math.Max(AutomationTiming.MinTabClickPostDelayMs, GetConfiguredTabSwitchDelayMs()),
            () => WaitForBestiaryConditionAsync(
                () => IsOfflineMerchantShopTabReady(GetOfflineMerchantPanel(), tabName),
                500,
                Math.Max(AutomationTiming.FastPollDelayMs, 25)));

        if (!switched)
        {
            throw new InvalidOperationException($"Could not switch Faustus to the shop tab '{tabName}'.");
        }
    }

    private bool IsMerchantPopupVisible() => GameController?.IngameState?.IngameUi?.PopUpWindow?.IsVisible == true;

    private async Task<bool> WaitForMerchantPopupVisibilityAsync(bool expectedVisible, int timeoutMs)
    {
        return await WaitForBestiaryConditionAsync(
            () => IsMerchantPopupVisible() == expectedVisible,
            timeoutMs,
            Math.Max(AutomationTiming.FastPollDelayMs, 10));
    }

    private async Task<MerchantPriceEntryResult> EnterMerchantListingPriceAsync(int priceChaos)
    {
        if (priceChaos <= 0)
        {
            throw new InvalidOperationException("Merchant listing price must be greater than zero.");
        }

        long popupReadyElapsedMs = 0;
        if (!IsMerchantPopupVisible())
        {
            var popupReadyStopwatch = Stopwatch.StartNew();
            if (!await WaitForMerchantPopupVisibilityAsync(expectedVisible: true, 1000))
            {
                throw new InvalidOperationException("Timed out waiting for the Faustus price popup.");
            }

            popupReadyElapsedMs = popupReadyStopwatch.ElapsedMilliseconds;
        }

        ReleaseAutomationTriggerKeys();
        var expectedPriceText = priceChaos.ToString(CultureInfo.InvariantCulture);

        var selectAndClearStopwatch = Stopwatch.StartNew();
        await CtrlTapKeyAsync(Keys.A, AutomationTiming.KeyTapDelayMs, AutomationTiming.KeyTapDelayMs);
        await TapKeyAsync(Keys.Back, AutomationTiming.KeyTapDelayMs, AutomationTiming.FastPollDelayMs);
        var selectAndClearElapsedMs = selectAndClearStopwatch.ElapsedMilliseconds;

        var typeDigitsStopwatch = Stopwatch.StartNew();
        await TypeDigitTextAsync(expectedPriceText);
        var typeDigitsElapsedMs = typeDigitsStopwatch.ElapsedMilliseconds;

        var confirmTextStopwatch = Stopwatch.StartNew();
        var observedPriceText = await WaitForMerchantPopupEnteredPriceTextAsync(expectedPriceText, 500);
        var confirmTextElapsedMs = confirmTextStopwatch.ElapsedMilliseconds;
        if (!IsMerchantPopupPriceTextMatch(observedPriceText, expectedPriceText))
        {
            throw new InvalidOperationException($"Merchant price text mismatch. Expected '{expectedPriceText}', observed '{observedPriceText ?? "<null>"}'.");
        }

        var submitCloseStopwatch = Stopwatch.StartNew();
        await TapKeyAsync(Keys.Enter, AutomationTiming.KeyTapDelayMs, 0);

        return new(
            popupReadyElapsedMs,
            selectAndClearElapsedMs,
            typeDigitsElapsedMs,
            confirmTextElapsedMs,
            submitCloseStopwatch.ElapsedMilliseconds);
    }

    private string GetMerchantPopupEnteredPriceText()
    {
        var textElement = TryGetChildFromIndicesQuietly(
            GameController?.IngameState?.IngameUi?.PopUpWindow,
            MerchantPopupEnteredPriceTextPath);
        if (textElement == null)
        {
            return null;
        }

        return TryGetAutomationElementText(textElement, 1);
    }

    private async Task<string> WaitForMerchantPopupEnteredPriceTextAsync(string expectedText, int timeoutMs)
    {
        return await PollAutomationValueAsync(
            GetMerchantPopupEnteredPriceText,
            observedText => IsMerchantPopupPriceTextMatch(observedText, expectedText),
            timeoutMs,
            AutomationTiming.FastPollDelayMs);
    }

    private static bool IsMerchantPopupPriceTextMatch(string observedText, string expectedText)
    {
        return string.Equals(NormalizeMerchantPopupPriceText(observedText), NormalizeMerchantPopupPriceText(expectedText), StringComparison.Ordinal);
    }

    private static string NormalizeMerchantPopupPriceText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return new string(text.Where(char.IsDigit).ToArray());
    }

    private void DrawFaustusShopTabSelectorPanel(MerchantAutomationSettings automation)
    {
        var panel = GetOfflineMerchantPanel();
        if (panel?.IsVisible != true)
        {
            var selectedTabNameText = automation?.SelectedFaustusShopTabName.Value?.Trim();
            ImGui.Text("Faustus shop tab");
            ImGui.SameLine();
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(selectedTabNameText) ? "Select tab" : selectedTabNameText);
            ImGui.TextDisabled("Open Faustus shop to change the selected shop tab.");
            return;
        }

        var tabNames = GetAvailableOfflineMerchantShopTabNames(panel);
        if (tabNames.Count <= 0)
        {
            ImGui.TextDisabled("No Faustus shop tabs available.");
            return;
        }

        var selectedTabName = automation?.SelectedFaustusShopTabName;
        var previewText = string.IsNullOrWhiteSpace(selectedTabName?.Value) ? "Select tab" : selectedTabName.Value;
        ImGui.Text("Faustus shop tab");
        ImGui.SameLine();

        if (ImGui.BeginCombo("##BeastsV2FaustusShopTab", previewText))
        {
            for (var i = 0; i < tabNames.Count; i++)
            {
                var tabName = tabNames[i];
                var isSelected = selectedTabName?.Value.EqualsIgnoreCase(tabName) == true;
                if (ImGui.Selectable($"{i}: {tabName}", isSelected))
                {
                    selectedTabName.Value = tabName;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    #endregion
}

