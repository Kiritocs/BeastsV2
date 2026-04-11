using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using Newtonsoft.Json;
using SharpDX;

namespace BeastsV2;

public partial class Main
{
    #region Map stash navigation and caching

    private const int MapStashMaxPageNumber = 6;
    private const int MapStashDiscoveryRetryCount = 3;
    private const int MapStashDiscoveryRetryDelayMs = 15;

    private T RetryMapStashDiscovery<T>(Func<int, T> attemptFunc, Func<T, bool> isResolved = null) where T : class
    {
        return RetryAutomation(
            attemptFunc,
            result => isResolved != null ? isResolved(result) : result != null,
            MapStashDiscoveryRetryCount,
            MapStashDiscoveryRetryDelayMs);
    }

    private Task EnsureMapStashTierTabSelectedAsync(StashAutomationTargetSettings target) => MapStashUi.EnsureTierTabSelectedAsync(target);

    private Task<bool> EnsureMapStashPageWithItemSelectedAsync(StashAutomationTargetSettings target, string metadata = null) =>
        MapStashUi.EnsurePageWithItemSelectedAsync(target, metadata);

    private async Task<bool> SearchMapStashPagesForMatchAsync(
        StashAutomationTargetSettings target,
        string itemName,
        string metadata,
        IReadOnlyList<int> pages)
    {
        foreach (var page in pages)
        {
            if (!await EnsureMapStashPageSelectedAsync(target, page))
            {
                continue;
            }

            if (MapStashVisiblePageContainsMatch(itemName, metadata))
            {
                LogDebug($"Found requested map stash item on page {page}. item='{itemName}', metadata='{metadata}'");
                return true;
            }
        }

        return false;
    }

    private async Task<bool> EnsureMapStashPageSelectedAsync(StashAutomationTargetSettings target, int pageNumber)
    {
        if (!IsMapStashTarget(target))
        {
            LogDebug($"EnsureMapStashPageSelectedAsync skipped because target is not a map stash target. pageNumber={pageNumber}, {DescribeTarget(target)}");
            return false;
        }

        LogMapStashPageTabsPathTraceIfChanged();

        if (!TryResolveMapStashPageTab(pageNumber, out var pageTab, out var pageTabsByNumber))
        {
            LogDebug(pageTabsByNumber == null || pageTabsByNumber.Count == 0
                ? $"EnsureMapStashPageSelectedAsync found no page tabs. requestedPage={pageNumber}"
                : $"Requested map stash page {pageNumber} was not found. Available pages: {string.Join(", ", pageTabsByNumber.Keys.OrderBy(x => x))}");
            return false;
        }

        _lastAutomationMapStashPageNumber = pageNumber;
        await SelectMapStashPageAsync(pageTab, GetMapStashPageSourceIndex(pageTab), pageNumber, Settings.StashAutomation);
        return true;
    }

    private void LogMapStashPageTabsPathTraceIfChanged()
    {
        var openLeftPanel = GameController?.IngameState?.IngameUi?.OpenLeftPanel;
        var pageTabsPathTrace = DescribePathLookup(openLeftPanel, MapStashPageTabPath);
        if (string.Equals(_lastAutomationMapStashPageTabsLogSignature, pageTabsPathTrace, StringComparison.Ordinal))
        {
            return;
        }

        _lastAutomationMapStashPageTabsLogSignature = pageTabsPathTrace;
        LogDebug($"Map stash page tabs path trace: {pageTabsPathTrace}");
    }

    private bool TryResolveMapStashPageTab(int pageNumber, out Element pageTab, out Dictionary<int, Element> pageTabsByNumber)
    {
        pageTab = null;
        pageTabsByNumber = GetMapStashPageTabsByNumber();
        return pageTabsByNumber?.TryGetValue(pageNumber, out pageTab) == true;
    }

    private async Task SelectMapStashPageAsync(Element pageTab, int sourceIndex, int pageNumber, StashAutomationSettings automation)
    {
        ThrowIfAutomationStopRequested();
        var rect = pageTab.GetClientRect();
        var tabSwitchDelayMs = GetConfiguredTabSwitchDelayMs();
        LogDebug($"Clicking map stash page {pageNumber}. sourceIndex={sourceIndex}, rect={DescribeRect(rect)}");

        await ClickElementAsync(
            pageTab,
            AutomationTiming.UiClickPreDelayMs,
            Math.Max(AutomationTiming.MinTabClickPostDelayMs, tabSwitchDelayMs));
        await DelayAutomationAsync(tabSwitchDelayMs);
    }

    private Dictionary<int, Element> GetMapStashPageTabsByNumber()
    {
        var container = ResolveMapStashPageTabContainer();
        if (AreCachedMapStashPageTabsByNumberValid(container, _lastAutomationMapStashPageTabsByNumber))
        {
            return _lastAutomationMapStashPageTabsByNumber;
        }

        var tabs = container?.Children;
        if (tabs == null)
        {
            _lastAutomationMapStashPageTabsByNumber = null;
            return null;
        }

        var byNumber = new Dictionary<int, Element>();
        foreach (var tab in tabs)
        {
            var number = GetMapStashPageNumber(tab);
            if (number.HasValue)
            {
                byNumber.TryAdd(number.Value, tab);
            }
        }

        _lastAutomationMapStashPageTabContainer = container;
        _lastAutomationMapStashPageTabsByNumber = byNumber;
        return byNumber;
    }

    private IReadOnlyList<int> GetMapStashSearchPageNumbers(IReadOnlyDictionary<int, Element> pageTabsByNumber)
    {
        if (pageTabsByNumber?.Count > 0 != true)
        {
            return Array.Empty<int>();
        }

        var pages = pageTabsByNumber.Keys.OrderBy(x => x).ToArray();
        return pageTabsByNumber.ContainsKey(_lastAutomationMapStashPageNumber)
            ? pages.Where(x => x > _lastAutomationMapStashPageNumber).ToArray()
            : pages;
    }

    private static int GetMapStashPageSourceIndex(Element pageTab) => pageTab?.IndexInParent ?? pageTab?.Parent?.Children?.IndexOf(pageTab) ?? -1;

    private static int? GetMapStashPageNumber(Element pageTab)
    {
        var text = TryGetElementText(TryGetChildFromIndicesQuietly(pageTab, MapStashPageNumberPath));
        return int.TryParse(text, out var pageNumber) && pageNumber is >= 1 and <= MapStashMaxPageNumber ? pageNumber : null;
    }

    private static Element GetChildAtOrDefault(Element parent, int childIndex) =>
        parent?.Children is { } children && childIndex >= 0 && childIndex < children.Count ? children[childIndex] : null;

    private static Element TryGetChildFromIndicesQuietly(Element root, IReadOnlyList<int> path)
    {
        var current = root;
        if (current == null || path == null)
        {
            return null;
        }

        foreach (var index in path)
        {
            current = GetChildAtOrDefault(current, index);
            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    private static Element TryGetElementByPathQuietly(Element root, IReadOnlyList<int> path) => TryGetChildFromIndicesQuietly(root, path);

    private Element TryResolveMapStashTierTab(int mapTier)
    {
        var childIndex = mapTier <= 9 ? mapTier - 1 : mapTier - 10;
        var tierPath = mapTier <= 9 ? MapStashTierOneToNineTabPath : MapStashTierTenToSixteenTabPath;
        var openLeftPanel = GameController?.IngameState?.IngameUi?.OpenLeftPanel;
        InvalidateCachedMapStashUiStateIfNeeded();
        LogDebug($"Map stash tier path trace: {DescribePathLookup(openLeftPanel, tierPath)}");

        var fixedTab = GetChildAtOrDefault(TryGetElementByPathQuietly(openLeftPanel, tierPath), childIndex);
        if (fixedTab != null)
        {
            return fixedTab;
        }

        var tierGroupTab = GetChildAtOrDefault(GetChildAtOrDefault(ResolveMapStashTierGroupRoot(openLeftPanel), mapTier <= 9 ? 0 : 1), childIndex);
        if (tierGroupTab != null)
        {
            return tierGroupTab;
        }

        var tierText = mapTier.ToString();
        var dynamicTab = EnumerateDescendants(openLeftPanel)
            .FirstOrDefault(x => x?.IsVisible == true && GetElementTextRecursive(x).EqualsIgnoreCase(tierText));
        if (dynamicTab == null)
        {
            LogDebug($"Map stash tier fixed path failed. tier={mapTier}, childIndex={childIndex}, path={DescribePath(tierPath)}");
        }

        return dynamicTab;
    }

    private Element ResolveMapStashPageTabContainer()
    {
        var openLeftPanel = GameController?.IngameState?.IngameUi?.OpenLeftPanel;
        InvalidateCachedMapStashUiStateIfNeeded();
        var pageTabContainer = TryGetElementByPathQuietly(openLeftPanel, MapStashPageTabPath);
        if (CountValidMapStashPageTabs(pageTabContainer) >= 6)
        {
            _lastAutomationMapStashPageTabContainer = pageTabContainer;
            TryPersistMapStashElementPath(
                openLeftPanel,
                pageTabContainer,
                hints => hints.MapStashPageTabContainerPath,
                (hints, path) => hints.MapStashPageTabContainerPath = path,
                "map stash page tab container");
            return pageTabContainer;
        }

        if (IsReusableMapStashPageTabContainer(openLeftPanel, _lastAutomationMapStashPageTabContainer))
        {
            return _lastAutomationMapStashPageTabContainer;
        }

        var persistedContainer = TryResolvePersistedMapStashElementPath(
            openLeftPanel,
            GetAutomationDynamicHints()?.MapStashPageTabContainerPath,
            element => CountValidMapStashPageTabs(element) >= 6,
            "map stash page tab container");
        if (persistedContainer != null)
        {
            _lastAutomationMapStashPageTabContainer = persistedContainer;
            return persistedContainer;
        }

        Element dynamicContainer = null;
        var bestPageCount = 0;
        var bestArea = float.MinValue;
        foreach (var element in EnumerateDescendants(openLeftPanel))
        {
            var pageCount = CountValidMapStashPageTabs(element);
            if (pageCount < 6)
            {
                continue;
            }

            var area = GetRectangleArea(element.GetClientRect());
            if (dynamicContainer == null || pageCount > bestPageCount || pageCount == bestPageCount && area > bestArea)
            {
                dynamicContainer = element;
                bestPageCount = pageCount;
                bestArea = area;
            }
        }

        _lastAutomationMapStashPageTabContainer = dynamicContainer ?? pageTabContainer;
        TryPersistMapStashElementPath(
            openLeftPanel,
            _lastAutomationMapStashPageTabContainer,
            hints => hints.MapStashPageTabContainerPath,
            (hints, path) => hints.MapStashPageTabContainerPath = path,
            "map stash page tab container");

        return _lastAutomationMapStashPageTabContainer;
    }

    private Element ResolveMapStashPageContentRoot()
    {
        var openLeftPanel = GameController?.IngameState?.IngameUi?.OpenLeftPanel;
        InvalidateCachedMapStashUiStateIfNeeded();
        var pageContent = TryGetElementByPathQuietly(openLeftPanel, MapStashPageContentPath);
        if (TryRememberMapStashPageContentRoot(openLeftPanel, pageContent, "fixed path"))
        {
            return pageContent;
        }

        if (IsReusableMapStashPageContentRoot(_lastAutomationMapStashPageContentRoot))
        {
            return _lastAutomationMapStashPageContentRoot;
        }

        var persistedContentRoot = TryResolvePersistedMapStashElementPath(
            openLeftPanel,
            GetAutomationDynamicHints()?.MapStashPageContentRootPath,
            IsReusableMapStashPageContentRoot,
            "map stash page content root");
        if (persistedContentRoot != null)
        {
            if (TryRememberMapStashPageContentRoot(openLeftPanel, persistedContentRoot, "persisted path"))
            {
                return persistedContentRoot;
            }

            _lastAutomationMapStashPageContentRoot = null;
        }

        var dynamicContent = RetryMapStashDiscovery(
            attempt =>
            {
                openLeftPanel = GameController?.IngameState?.IngameUi?.OpenLeftPanel;
                Element candidate = null;
                var bestMapDescendants = 0;
                var bestArea = float.MaxValue;
                foreach (var element in EnumerateDescendants(openLeftPanel))
                {
                    if (!TryGetMapStashPageContentCandidateScore(element, out var mapDescendants, out var area))
                    {
                        continue;
                    }

                    if (candidate == null || mapDescendants > bestMapDescendants || mapDescendants == bestMapDescendants && area < bestArea)
                    {
                        candidate = element;
                        bestMapDescendants = mapDescendants;
                        bestArea = area;
                    }
                }

                return TryRememberMapStashPageContentRoot(openLeftPanel, candidate, $"dynamic attempt {attempt + 1}")
                    ? candidate
                    : null;
            });
        if (dynamicContent != null)
        {
            return dynamicContent;
        }

        return IsReusableMapStashPageContentRoot(_lastAutomationMapStashPageContentRoot)
            ? _lastAutomationMapStashPageContentRoot
            : null;
    }

    private void InvalidateCachedMapStashUiStateIfNeeded()
    {
        var stash = GameController?.IngameState?.IngameUi?.StashElement;
        var currentCacheKey = stash?.IsVisible == true && stash.VisibleStash?.InvType == InventoryType.MapStash
            ? stash.IndexVisibleStash
            : -1;

        if (_lastAutomationMapStashUiCacheKey == currentCacheKey)
        {
            return;
        }

        _lastAutomationMapStashUiCacheKey = currentCacheKey;
        _lastAutomationMapStashTierGroupRoot = null;
        _lastAutomationMapStashPageTabContainer = null;
        _lastAutomationMapStashPageTabsByNumber = null;
        _lastAutomationMapStashPageContentRoot = null;
        _lastAutomationMapStashPageContentLogSignature = null;
        _lastAutomationMapStashPageTabsLogSignature = null;
    }

    private Element ResolveMapStashTierGroupRoot(Element openLeftPanel)
    {
        if (IsMapStashTierGroupContainer(_lastAutomationMapStashTierGroupRoot))
        {
            return _lastAutomationMapStashTierGroupRoot;
        }

        var persistedTierGroup = TryResolvePersistedMapStashElementPath(
            openLeftPanel,
            GetAutomationDynamicHints()?.MapStashTierGroupPath,
            IsMapStashTierGroupContainer,
            "map stash tier group");
        if (persistedTierGroup != null)
        {
            _lastAutomationMapStashTierGroupRoot = persistedTierGroup;
            return persistedTierGroup;
        }

        Element bestTierGroup = null;
        var bestArea = float.MinValue;
        foreach (var element in EnumerateDescendants(openLeftPanel))
        {
            if (!IsMapStashTierGroupContainer(element))
            {
                continue;
            }

            var area = GetRectangleArea(element.GetClientRect());
            if (bestTierGroup == null || area > bestArea)
            {
                bestTierGroup = element;
                bestArea = area;
            }
        }

        _lastAutomationMapStashTierGroupRoot = bestTierGroup;
        TryPersistMapStashElementPath(
            openLeftPanel,
            _lastAutomationMapStashTierGroupRoot,
            hints => hints.MapStashTierGroupPath,
            (hints, path) => hints.MapStashTierGroupPath = path,
            "map stash tier group");
        return _lastAutomationMapStashTierGroupRoot;
    }

    private bool IsReusableMapStashPageTabContainer(Element root, Element element)
    {
        return IsElementAttachedToRoot(root, element) && CountValidMapStashPageTabs(element) >= 6;
    }

    private bool AreCachedMapStashPageTabsByNumberValid(Element pageTabContainer, IReadOnlyDictionary<int, Element> pageTabsByNumber)
    {
        if (pageTabContainer == null || pageTabsByNumber == null || pageTabsByNumber.Count <= 0)
        {
            return false;
        }

        foreach (var entry in pageTabsByNumber)
        {
            if (!ReferenceEquals(entry.Value?.Parent, pageTabContainer))
            {
                return false;
            }

            if ((entry.Value.Parent?.Children?.IndexOf(entry.Value) ?? -1) < 0)
            {
                return false;
            }

            if (GetMapStashPageNumber(entry.Value) != entry.Key)
            {
                return false;
            }
        }

        return true;
    }

    private StashAutomationDynamicHintSettings GetAutomationDynamicHints()
    {
        return Settings?.StashAutomation?.DynamicHints;
    }

    private Element TryResolvePersistedMapStashElementPath(
        Element root,
        IReadOnlyList<int> path,
        Func<Element, bool> validator,
        string label)
    {
        if (root == null || path == null || path.Count <= 0 || validator == null)
        {
            return null;
        }

        var resolvedElement = TryGetElementByPathQuietly(root, path);
        if (!validator(resolvedElement))
        {
            return null;
        }

        LogDebug($"Resolved {label} from persisted path {DescribePath(path)}. element={DescribeElement(resolvedElement)}");
        return resolvedElement;
    }

    private List<int> TryPersistMapStashElementPath(
        Element root,
        Element target,
        Func<StashAutomationDynamicHintSettings, List<int>> getter,
        Action<StashAutomationDynamicHintSettings, List<int>> setter,
        string label)
    {
        var hints = GetAutomationDynamicHints();
        if (root == null || target == null || hints == null || getter == null || setter == null)
        {
            return null;
        }

        var resolvedPath = TryFindPathFromRoot(root, target);
        if (resolvedPath == null || resolvedPath.Count <= 0)
        {
            return null;
        }

        var existingPath = getter(hints);
        if (existingPath != null && existingPath.SequenceEqual(resolvedPath))
        {
            LogDebug($"Persisted {label} path unchanged ({DescribePath(resolvedPath)}); skipping settings snapshot save.");
            return resolvedPath;
        }

        setter(hints, resolvedPath);
        LogDebug($"Persisted {label} path {DescribePath(resolvedPath)}");
        TrySaveSettingsSnapshot();
        return resolvedPath;
    }

    private void TrySaveSettingsSnapshot()
    {
        try
        {
            var settings = Settings;
            if (settings == null)
            {
                return;
            }

            var configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "global");
            Directory.CreateDirectory(configDirectory);
            var settingsPath = Path.Combine(configDirectory, SettingsFileName);
            var settingsJson = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(settingsPath, settingsJson);
            LogDebug($"Saved settings snapshot to '{settingsPath}'.");
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to save settings snapshot: {ex.Message}");
        }
    }

    private static List<int> TryFindPathFromRoot(Element root, Element target)
    {
        if (root == null || target == null)
        {
            return null;
        }

        if (ReferenceEquals(root, target))
        {
            return [];
        }

        var stack = new Stack<(Element Element, List<int> Path)>();
        stack.Push((root, []));
        while (stack.Count > 0)
        {
            var (current, path) = stack.Pop();
            if (current?.Children == null)
            {
                continue;
            }

            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                var child = current.Children[i];
                if (child == null)
                {
                    continue;
                }

                var childPath = new List<int>(path.Count + 1);
                childPath.AddRange(path);
                childPath.Add(i);
                if (ReferenceEquals(child, target))
                {
                    return childPath;
                }

                stack.Push((child, childPath));
            }
        }

        return null;
    }

    private bool IsElementAttachedToRoot(Element root, Element target)
    {
        for (var current = target; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private Element FindFragmentScarabTabDynamically(Element root) => EnumerateDescendants(root)
        .FirstOrDefault(x => x?.IsVisible == true && GetElementTextRecursive(x)?.IndexOf("Scarab", StringComparison.OrdinalIgnoreCase) >= 0);

    private static int CountValidMapStashPageTabs(Element element) =>
        element?.Children?
            .Select(GetMapStashPageNumber)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .Distinct()
            .Count() ?? 0;

    private static bool IsMapStashTierGroupContainer(Element element)
    {
        var children = element?.Children;
        return children?.Count >= 2 && IsMapStashTierContainer(children[0]) && IsMapStashTierContainer(children[1]);
    }

    private static bool IsMapStashTierContainer(Element element) =>
        (element?.Children?.Count ?? 0) >= 7 && element.Children.Count(child => child?.IsVisible == true) >= 7;

    private static bool IsMapStashPageContentCandidate(Element element) => TryGetMapStashPageContentCandidateScore(element, out _, out _);

    private static bool TryGetMapStashPageContentCandidateScore(Element element, out int mapDescendants, out float area)
    {
        mapDescendants = 0;
        area = 0;
        if (element?.IsVisible != true || (element.Children?.Count ?? 0) <= 0)
        {
            return false;
        }

        mapDescendants = CountVisibleMapEntityDescendants(element);
        if (mapDescendants <= 0 || mapDescendants > 96)
        {
            return false;
        }

        if (CountValidMapStashPageTabs(element) >= 6 || IsMapStashTierGroupContainer(element))
        {
            return false;
        }

        foreach (var descendant in EnumerateDescendants(element))
        {
            if (CountValidMapStashPageTabs(descendant) >= 6 || IsMapStashTierGroupContainer(descendant))
            {
                return false;
            }
        }

        area = GetRectangleArea(element.GetClientRect());
        return true;
    }

    private static bool IsReusableMapStashPageContentRoot(Element element)
    {
        if (element?.IsVisible != true)
        {
            return false;
        }

        var childCount = element.Children?.Count ?? 0;
        if (childCount <= 0 || childCount > 32)
        {
            return false;
        }

        return CountValidMapStashPageTabs(element) < 6 && !IsMapStashTierGroupContainer(element);
    }

    private bool TryRememberMapStashPageContentRoot(Element root, Element element, string source)
    {
        if (!IsMapStashPageContentCandidate(element))
        {
            return false;
        }

        _lastAutomationMapStashPageContentRoot = element;
        var persistedPath = TryPersistMapStashElementPath(
            root,
            element,
            hints => hints.MapStashPageContentRootPath,
            (hints, path) => hints.MapStashPageContentRootPath = path,
            "map stash page content root");
        var logSignature = persistedPath != null
            ? DescribePath(persistedPath)
            : DescribeRect(element.GetClientRect());
        if (string.Equals(_lastAutomationMapStashPageContentLogSignature, logSignature, StringComparison.Ordinal))
        {
            return true;
        }

        _lastAutomationMapStashPageContentLogSignature = logSignature;
        if (persistedPath == null)
        {
            LogDebug($"Could not capture map stash page content root path from discovery root. source={source}, root={DescribeElement(root)}, content={DescribeElement(element)}");
        }
        LogDebug($"Map stash page content dynamically resolved via {source}. content={DescribeElement(element)}, mapDescendants={CountVisibleMapEntityDescendants(element)}, children={DescribeChildren(element)}");

        return true;
    }

    private static int CountVisibleMapEntityDescendants(Element root) =>
        root == null
            ? 0
            : EnumerateDescendants(root, includeSelf: true).Count(x => x?.IsVisible == true && x.Entity?.Metadata?.IndexOf("Metadata/Items/Maps", StringComparison.OrdinalIgnoreCase) >= 0);

    private static string GetElementTextRecursive(Element element, int maxDepth = 3)
    {
        if (element == null)
        {
            return null;
        }

        var text = TryGetElementText(element);
        if (!string.IsNullOrWhiteSpace(text) || maxDepth <= 0 || element.Children == null)
        {
            return text;
        }

        foreach (var child in element.Children)
        {
            text = GetElementTextRecursive(child, maxDepth - 1);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string TryGetElementText(Element element)
    {
        try { return element?.Text?.Trim() ?? element?.GetText(16)?.Trim(); }
        catch { return null; }
    }

    private static IEnumerable<Element> EnumerateDescendants(Element root, bool includeSelf = false)
    {
        if (root == null)
        {
            yield break;
        }

        var stack = new Stack<Element>();
        if (includeSelf)
        {
            stack.Push(root);
        }
        else if (root.Children != null)
        {
            for (var i = root.Children.Count - 1; i >= 0; i--)
            {
                if (root.Children[i] != null)
                {
                    stack.Push(root.Children[i]);
                }
            }
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (current?.Children == null)
            {
                continue;
            }

            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                if (current.Children[i] != null)
                {
                    stack.Push(current.Children[i]);
                }
            }
        }
    }

    private static float GetRectangleArea(RectangleF rect) => Math.Max(0, rect.Width) * Math.Max(0, rect.Height);

    private IList<Element> GetVisibleMapStashPageItems() => MapStashUi.GetVisiblePageItems();

    private static void CollectVisibleEntityDescendants(Element root, ICollection<Element> results)
    {
        if (root == null || results == null)
        {
            return;
        }

        foreach (var element in EnumerateDescendants(root, includeSelf: true))
        {
            if (element?.IsVisible == true && element.Entity != null)
            {
                results.Add(element);
            }
        }
    }

    private async Task<Element> WaitForNextMatchingMapStashPageItemAsync(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        var timing = AutomationTiming;
        var clickDelayMs = GetConfiguredClickDelayMs();
        var timeoutMs = Math.Max(
            timing.QuantityChangeBaseDelayMs,
            clickDelayMs + timing.QuantityChangeBaseDelayMs + GetServerLatencyMs());

        var nextPageItem = await PollAutomationValueAsync(
            () => FindNextMatchingMapStashPageItem(GetVisibleMapStashPageItems(), metadata),
            item => item?.Entity != null,
            timeoutMs,
            timing.FastPollDelayMs);
        if (nextPageItem?.Entity != null)
        {
            LogDebug($"WaitForNextMatchingMapStashPageItemAsync found metadata='{metadata}'. item={DescribeElement(nextPageItem)}");
            return nextPageItem;
        }

        LogDebug($"WaitForNextMatchingMapStashPageItemAsync timed out for metadata='{metadata}'. pathTrace={DescribePathLookup(GameController?.IngameState?.IngameUi?.OpenLeftPanel, MapStashPageContentPath)}");

        return null;
    }

    private static Element FindMapStashPageItemByName(IList<Element> items, string itemName) =>
        string.IsNullOrWhiteSpace(itemName)
            ? null
            : items?.FirstOrDefault(x => x?.Entity?.GetComponent<Base>()?.Name.EqualsIgnoreCase(itemName) == true);

    private static Element FindNextMatchingMapStashPageItem(IList<Element> items, string metadata) =>
        string.IsNullOrWhiteSpace(metadata)
            ? null
            : items?
                .Where(x => x?.Entity?.Metadata.EqualsIgnoreCase(metadata) == true)
                .OrderByScreenPosition(x => x.GetClientRect())
                .FirstOrDefault();

    private static int CountMatchingMapStashPageItems(IList<Element> items, string metadata) =>
        string.IsNullOrWhiteSpace(metadata)
            ? 0
            : items?.Count(x => x?.Entity?.Metadata.EqualsIgnoreCase(metadata) == true) ?? 0;

    private static string DescribeEntity(Entity entity)
    {
        if (entity == null)
        {
            return "entity=null";
        }

        return $"name='{entity.GetComponent<Base>()?.Name}', metadata='{entity.Metadata}'";
    }

    private bool MapStashVisiblePageContainsMatch(string itemName, string metadata)
    {
        var visiblePageItems = GetVisibleMapStashPageItems();
        if (visiblePageItems == null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(metadata)
            ? visiblePageItems.Any(x => x?.Entity?.Metadata.EqualsIgnoreCase(metadata) == true)
            : visiblePageItems.Any(x => x?.Entity?.GetComponent<Base>()?.Name.EqualsIgnoreCase(itemName) == true);
    }

    private int GetVisibleMapStashPageMatchingQuantity(string metadata) =>
        GetVisibleMatchingQuantity(GetVisibleMapStashPageItems, metadata, CountMatchingMapStashPageItems);

    private static int? TryGetConfiguredMapTier(StashAutomationTargetSettings target)
    {
        var configuredItemName = target?.ItemName.Value?.Trim();
        if (string.IsNullOrWhiteSpace(configuredItemName))
        {
            return null;
        }

        const string tierPrefix = "(Tier ";
        var tierStartIndex = configuredItemName.IndexOf(tierPrefix, StringComparison.OrdinalIgnoreCase);
        if (tierStartIndex < 0)
        {
            return null;
        }

        tierStartIndex += tierPrefix.Length;
        var tierEndIndex = configuredItemName.IndexOf(')', tierStartIndex);
        if (tierEndIndex <= tierStartIndex)
        {
            return null;
        }

        return int.TryParse(configuredItemName.Substring(tierStartIndex, tierEndIndex - tierStartIndex), out var tier) && tier is >= 1 and <= 16
            ? tier
            : null;
    }

    #endregion
}

