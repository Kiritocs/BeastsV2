using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeastsV2.Runtime.State;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BeastsV2.Runtime.Automation;

internal sealed record MapStashUiTiming(int UiClickPreDelayMs, int MinTabClickPostDelayMs, int TabSwitchDelayMs);

internal sealed record MapStashUiCallbacks(
    Func<StashElement> GetStash,
    Func<Element> GetOpenLeftPanel,
    Func<StashAutomationSettings> GetStashAutomationSettings,
    Func<StashAutomationDynamicHintSettings> GetDynamicHints,
    Func<MapStashUiTiming> GetTiming,
    Action ThrowIfAutomationStopRequested,
    Action<string> LogDebug,
    Func<StashAutomationTargetSettings, string> DescribeTarget,
    Func<StashElement, string> DescribeStash,
    Func<Element, string> DescribeElement,
    Func<RectangleF, string> DescribeRect,
    Func<IReadOnlyList<int>, string> DescribePath,
    Func<Element, IReadOnlyList<int>, string> DescribePathLookup,
    Func<IReadOnlyDictionary<int, Element>, string> DescribePageTabs,
    Func<Element, string> DescribeChildren,
    Func<Element, int, int, Task> ClickElementAsync,
    Func<int, Task> DelayAutomationAsync,
    Action SaveSettingsSnapshot,
    Func<StashAutomationTargetSettings, bool> IsMapStashTarget,
    Func<StashAutomationTargetSettings, int?> TryGetConfiguredMapTier,
    Func<Element, string, string, bool> IsMatchingVisibleMapStashItem,
    Func<string, string, bool> VisiblePageContainsMatch,
    IReadOnlyList<int> TierOneToNineTabPath,
    IReadOnlyList<int> TierTenToSixteenTabPath,
    IReadOnlyList<int> PageTabPath,
    IReadOnlyList<int> PageNumberPath,
    IReadOnlyList<int> PageContentPath);

internal sealed class MapStashUiService
{
    private const int MapStashMaxPageNumber = 6;
    private const int MapStashDiscoveryRetryCount = 3;
    private const int MapStashDiscoveryRetryDelayMs = 15;

    private readonly AutomationUiCacheState _cache;
    private readonly MapStashUiCallbacks _callbacks;

    public MapStashUiService(AutomationUiCacheState cache, MapStashUiCallbacks callbacks)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public async Task EnsureTierTabSelectedAsync(StashAutomationTargetSettings target)
    {
        var stash = _callbacks.GetStash();
        var tier = _callbacks.TryGetConfiguredMapTier(target);
        if (stash?.IsVisible != true || stash.VisibleStash?.InvType != InventoryType.MapStash || !tier.HasValue)
        {
            _callbacks.LogDebug($"EnsureMapStashTierTabSelectedAsync skipped. {_callbacks.DescribeStash(stash)}, configuredTier={(tier.HasValue ? tier.Value : -1)}, item='{target?.ItemName.Value}'");
            _cache.LastAutomationMapStashTierSelection = -1;
            return;
        }

        var selectionKey = stash.IndexVisibleStash * 100 + tier.Value;
        if (_cache.LastAutomationMapStashTierSelection == selectionKey)
        {
            return;
        }

        var tierTab = TryResolveTierTab(tier.Value);
        if (tierTab == null)
        {
            _callbacks.LogDebug($"Map stash tier tab not found. tier={tier.Value}, openLeftPanel={_callbacks.DescribeElement(_callbacks.GetOpenLeftPanel())}");
            _cache.LastAutomationMapStashTierSelection = -1;
            return;
        }

        var automation = _callbacks.GetStashAutomationSettings();
        var timing = _callbacks.GetTiming();
        _callbacks.LogDebug($"Clicking map stash tier tab. tier={tier.Value}, selectionKey={selectionKey}, tab={_callbacks.DescribeElement(tierTab)}");
        await _callbacks.ClickElementAsync(
            tierTab,
            timing.UiClickPreDelayMs,
            Math.Max(timing.MinTabClickPostDelayMs, timing.TabSwitchDelayMs));
        _cache.LastAutomationMapStashTierSelection = selectionKey;
    }

    public async Task<bool> EnsurePageWithItemSelectedAsync(StashAutomationTargetSettings target, string metadata)
    {
        if (!_callbacks.IsMapStashTarget(target))
        {
            _callbacks.LogDebug($"EnsureMapStashPageWithItemSelectedAsync skipped because target is not a map stash target. {_callbacks.DescribeTarget(target)}");
            return false;
        }

        var itemName = target.ItemName.Value?.Trim();
        if (_callbacks.VisiblePageContainsMatch(itemName, metadata))
        {
            return true;
        }

        var pageTabsByNumber = GetPageTabsByNumber();
        if (pageTabsByNumber?.Count > 0 != true)
        {
            _callbacks.LogDebug($"No map stash page tabs found while looking for item='{itemName}', metadata='{metadata}'.");
            return false;
        }

        var pages = GetSearchPageNumbers(pageTabsByNumber);
        _callbacks.LogDebug($"Searching map stash pages for item='{itemName}', metadata='{metadata}'. Pages={(pages.Count > 0 ? string.Join(", ", pages) : "<none>")}, tabs={_callbacks.DescribePageTabs(pageTabsByNumber)}");

        foreach (var page in pages)
        {
            if (!await EnsurePageSelectedAsync(target, page))
            {
                continue;
            }

            if (await VisiblePageContainsMatchQuicklyAsync(page, itemName, metadata))
            {
                _callbacks.LogDebug($"Found requested map stash item on page {page}. item='{itemName}', metadata='{metadata}'");
                return true;
            }
        }

        _callbacks.LogDebug($"Requested map stash item was not found on searchable pages. item='{itemName}', metadata='{metadata}'");
        return false;
    }

    public IList<Element> GetVisiblePageItems()
    {
        return RetryDiscovery(
            attempt =>
            {
                var visibleInventoryItems = _callbacks.GetStash()?.VisibleStash?.VisibleInventoryItems;
                if (visibleInventoryItems?.Count > 0)
                {
                    return visibleInventoryItems.Cast<Element>().ToList();
                }

                var pageContent = ResolvePageContentRoot();
                if (pageContent != null)
                {
                    var items = new List<Element>();
                    CollectVisibleEntityDescendants(pageContent, items);
                    if (items.Count > 0)
                    {
                        return items;
                    }

                    if (attempt == MapStashDiscoveryRetryCount - 1)
                    {
                        _callbacks.LogDebug($"GetVisibleMapStashPageItems found no visible entity descendants in page content. content={_callbacks.DescribeElement(pageContent)}, children={_callbacks.DescribeChildren(pageContent)}");
                    }

                    return null;
                }

                if (attempt == MapStashDiscoveryRetryCount - 1)
                {
                    _callbacks.LogDebug($"GetVisibleMapStashPageItems could not resolve page content. pathTrace={_callbacks.DescribePathLookup(_callbacks.GetOpenLeftPanel(), _callbacks.PageContentPath)}");
                }

                return null;
            });
    }

    private async Task<bool> VisiblePageContainsMatchQuicklyAsync(int pageNumber, string itemName, string metadata)
    {
        var timing = _callbacks.GetTiming();
        var timeoutMs = Math.Max(250, timing.TabSwitchDelayMs * 6);
        var pollDelayMs = Math.Max(15, timing.TabSwitchDelayMs);
        var startedAt = DateTime.UtcNow;

        while ((DateTime.UtcNow - startedAt).TotalMilliseconds < timeoutMs)
        {
            _callbacks.ThrowIfAutomationStopRequested();

            var visibleItems = _callbacks.GetStash()?.VisibleStash?.VisibleInventoryItems;
            if (visibleItems?.Count > 0)
            {
                return visibleItems.Cast<Element>().Any(item => _callbacks.IsMatchingVisibleMapStashItem(item, metadata, itemName));
            }

            await _callbacks.DelayAutomationAsync(pollDelayMs);
        }

        _callbacks.LogDebug($"Map stash page {pageNumber} did not expose visible inventory items within {timeoutMs}ms while probing for item='{itemName}', metadata='{metadata}'. Skipping full page-content fallback for this page.");
        return false;
    }

    private async Task<bool> EnsurePageSelectedAsync(StashAutomationTargetSettings target, int pageNumber)
    {
        if (!_callbacks.IsMapStashTarget(target))
        {
            _callbacks.LogDebug($"EnsureMapStashPageSelectedAsync skipped because target is not a map stash target. pageNumber={pageNumber}, {_callbacks.DescribeTarget(target)}");
            return false;
        }

        LogPageTabsPathTraceIfChanged();

        if (!TryResolvePageTab(pageNumber, out var pageTab, out var pageTabsByNumber))
        {
            _callbacks.LogDebug(pageTabsByNumber == null || pageTabsByNumber.Count == 0
                ? $"EnsureMapStashPageSelectedAsync found no page tabs. requestedPage={pageNumber}"
                : $"Requested map stash page {pageNumber} was not found. Available pages: {string.Join(", ", pageTabsByNumber.Keys.OrderBy(x => x))}");
            return false;
        }

        _cache.LastAutomationMapStashPageNumber = pageNumber;
        await SelectPageAsync(pageTab, GetPageSourceIndex(pageTab), pageNumber);
        return true;
    }

    private void LogPageTabsPathTraceIfChanged()
    {
        var pageTabsPathTrace = _callbacks.DescribePathLookup(_callbacks.GetOpenLeftPanel(), _callbacks.PageTabPath);
        if (string.Equals(_cache.LastAutomationMapStashPageTabsLogSignature, pageTabsPathTrace, StringComparison.Ordinal))
        {
            return;
        }

        _cache.LastAutomationMapStashPageTabsLogSignature = pageTabsPathTrace;
        _callbacks.LogDebug($"Map stash page tabs path trace: {pageTabsPathTrace}");
    }

    private bool TryResolvePageTab(int pageNumber, out Element pageTab, out Dictionary<int, Element> pageTabsByNumber)
    {
        pageTab = null;
        pageTabsByNumber = GetPageTabsByNumber();
        return pageTabsByNumber?.TryGetValue(pageNumber, out pageTab) == true;
    }

    private async Task SelectPageAsync(Element pageTab, int sourceIndex, int pageNumber)
    {
        _callbacks.ThrowIfAutomationStopRequested();
        var rect = pageTab.GetClientRect();
        var timing = _callbacks.GetTiming();
        _callbacks.LogDebug($"Clicking map stash page {pageNumber}. sourceIndex={sourceIndex}, rect={_callbacks.DescribeRect(rect)}");

        await _callbacks.ClickElementAsync(
            pageTab,
            timing.UiClickPreDelayMs,
            Math.Max(timing.MinTabClickPostDelayMs, timing.TabSwitchDelayMs));
        await _callbacks.DelayAutomationAsync(timing.TabSwitchDelayMs);
    }

    private Dictionary<int, Element> GetPageTabsByNumber()
    {
        var container = ResolvePageTabContainer();
        if (AreCachedPageTabsByNumberValid(container, _cache.LastAutomationMapStashPageTabsByNumber))
        {
            return _cache.LastAutomationMapStashPageTabsByNumber;
        }

        var tabs = container?.Children;
        if (tabs == null)
        {
            _cache.LastAutomationMapStashPageTabsByNumber = null;
            return null;
        }

        var byNumber = new Dictionary<int, Element>();
        foreach (var tab in tabs)
        {
            var number = GetPageNumber(tab);
            if (number.HasValue)
            {
                byNumber.TryAdd(number.Value, tab);
            }
        }

        _cache.LastAutomationMapStashPageTabContainer = container;
        _cache.LastAutomationMapStashPageTabsByNumber = byNumber;
        return byNumber;
    }

    private IReadOnlyList<int> GetSearchPageNumbers(IReadOnlyDictionary<int, Element> pageTabsByNumber)
    {
        if (pageTabsByNumber?.Count > 0 != true)
        {
            return Array.Empty<int>();
        }

        var pages = pageTabsByNumber.Keys.OrderBy(x => x).ToArray();
        return pageTabsByNumber.ContainsKey(_cache.LastAutomationMapStashPageNumber)
            ? pages.Where(x => x > _cache.LastAutomationMapStashPageNumber).ToArray()
            : pages;
    }

    private static int GetPageSourceIndex(Element pageTab) => pageTab?.IndexInParent ?? pageTab?.Parent?.Children?.IndexOf(pageTab) ?? -1;

    private int? GetPageNumber(Element pageTab)
    {
        var text = TryGetElementText(TryGetChildFromIndicesQuietly(pageTab, _callbacks.PageNumberPath));
        return int.TryParse(text, out var pageNumber) && pageNumber is >= 1 and <= MapStashMaxPageNumber ? pageNumber : null;
    }

    private Element TryResolveTierTab(int mapTier)
    {
        var childIndex = mapTier <= 9 ? mapTier - 1 : mapTier - 10;
        var tierPath = mapTier <= 9 ? _callbacks.TierOneToNineTabPath : _callbacks.TierTenToSixteenTabPath;
        var openLeftPanel = _callbacks.GetOpenLeftPanel();
        InvalidateCachedUiStateIfNeeded();
        _callbacks.LogDebug($"Map stash tier path trace: {_callbacks.DescribePathLookup(openLeftPanel, tierPath)}");

        var fixedTab = GetChildAtOrDefault(TryGetChildFromIndicesQuietly(openLeftPanel, tierPath), childIndex);
        if (fixedTab != null)
        {
            return fixedTab;
        }

        var tierGroupTab = GetChildAtOrDefault(GetChildAtOrDefault(ResolveTierGroupRoot(openLeftPanel), mapTier <= 9 ? 0 : 1), childIndex);
        if (tierGroupTab != null)
        {
            return tierGroupTab;
        }

        var tierText = mapTier.ToString();
        var dynamicTab = EnumerateDescendants(openLeftPanel)
            .FirstOrDefault(x => x?.IsVisible == true && GetElementTextRecursive(x).EqualsIgnoreCase(tierText));
        if (dynamicTab == null)
        {
            _callbacks.LogDebug($"Map stash tier fixed path failed. tier={mapTier}, childIndex={childIndex}, path={_callbacks.DescribePath(tierPath)}");
        }

        return dynamicTab;
    }

    private Element ResolvePageTabContainer()
    {
        var openLeftPanel = _callbacks.GetOpenLeftPanel();
        InvalidateCachedUiStateIfNeeded();
        var pageTabContainer = TryGetChildFromIndicesQuietly(openLeftPanel, _callbacks.PageTabPath);
        if (CountValidPageTabs(pageTabContainer) >= 6)
        {
            _cache.LastAutomationMapStashPageTabContainer = pageTabContainer;
            TryPersistElementPath(
                openLeftPanel,
                pageTabContainer,
                hints => hints.MapStashPageTabContainerPath,
                (hints, path) => hints.MapStashPageTabContainerPath = path,
                "map stash page tab container");
            return pageTabContainer;
        }

        if (IsReusablePageTabContainer(openLeftPanel, _cache.LastAutomationMapStashPageTabContainer))
        {
            return _cache.LastAutomationMapStashPageTabContainer;
        }

        var persistedContainer = TryResolvePersistedElementPath(
            openLeftPanel,
            _callbacks.GetDynamicHints()?.MapStashPageTabContainerPath,
            element => CountValidPageTabs(element) >= 6,
            "map stash page tab container");
        if (persistedContainer != null)
        {
            _cache.LastAutomationMapStashPageTabContainer = persistedContainer;
            return persistedContainer;
        }

        Element dynamicContainer = null;
        var bestPageCount = 0;
        var bestArea = float.MinValue;
        foreach (var element in EnumerateDescendants(openLeftPanel))
        {
            var pageCount = CountValidPageTabs(element);
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

        _cache.LastAutomationMapStashPageTabContainer = dynamicContainer ?? pageTabContainer;
        TryPersistElementPath(
            openLeftPanel,
            _cache.LastAutomationMapStashPageTabContainer,
            hints => hints.MapStashPageTabContainerPath,
            (hints, path) => hints.MapStashPageTabContainerPath = path,
            "map stash page tab container");

        return _cache.LastAutomationMapStashPageTabContainer;
    }

    private Element ResolvePageContentRoot()
    {
        var openLeftPanel = _callbacks.GetOpenLeftPanel();
        InvalidateCachedUiStateIfNeeded();
        var pageContent = TryGetChildFromIndicesQuietly(openLeftPanel, _callbacks.PageContentPath);
        if (TryRememberPageContentRoot(openLeftPanel, pageContent, "fixed path"))
        {
            return pageContent;
        }

        if (IsReusablePageContentRoot(_cache.LastAutomationMapStashPageContentRoot))
        {
            return _cache.LastAutomationMapStashPageContentRoot;
        }

        var persistedContentRoot = TryResolvePersistedElementPath(
            openLeftPanel,
            _callbacks.GetDynamicHints()?.MapStashPageContentRootPath,
            IsReusablePageContentRoot,
            "map stash page content root");
        if (persistedContentRoot != null)
        {
            if (TryRememberPageContentRoot(openLeftPanel, persistedContentRoot, "persisted path"))
            {
                return persistedContentRoot;
            }

            _cache.LastAutomationMapStashPageContentRoot = null;
        }

        var dynamicContent = RetryDiscovery(
            attempt =>
            {
                openLeftPanel = _callbacks.GetOpenLeftPanel();
                Element candidate = null;
                var bestMapDescendants = 0;
                var bestArea = float.MaxValue;
                foreach (var element in EnumerateDescendants(openLeftPanel))
                {
                    if (!TryGetPageContentCandidateScore(element, out var mapDescendants, out var area))
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

                return TryRememberPageContentRoot(openLeftPanel, candidate, $"dynamic attempt {attempt + 1}")
                    ? candidate
                    : null;
            });
        if (dynamicContent != null)
        {
            return dynamicContent;
        }

        return IsReusablePageContentRoot(_cache.LastAutomationMapStashPageContentRoot)
            ? _cache.LastAutomationMapStashPageContentRoot
            : null;
    }

    private void InvalidateCachedUiStateIfNeeded()
    {
        var stash = _callbacks.GetStash();
        var currentCacheKey = stash?.IsVisible == true && stash.VisibleStash?.InvType == InventoryType.MapStash
            ? stash.IndexVisibleStash
            : -1;

        if (_cache.LastAutomationMapStashUiCacheKey == currentCacheKey)
        {
            return;
        }

        _cache.LastAutomationMapStashUiCacheKey = currentCacheKey;
        _cache.LastAutomationMapStashTierGroupRoot = null;
        _cache.LastAutomationMapStashPageTabContainer = null;
        _cache.LastAutomationMapStashPageTabsByNumber = null;
        _cache.LastAutomationMapStashPageContentRoot = null;
        _cache.LastAutomationMapStashPageContentLogSignature = null;
        _cache.LastAutomationMapStashPageTabsLogSignature = null;
    }

    private Element ResolveTierGroupRoot(Element openLeftPanel)
    {
        if (IsMapStashTierGroupContainer(_cache.LastAutomationMapStashTierGroupRoot))
        {
            return _cache.LastAutomationMapStashTierGroupRoot;
        }

        var persistedTierGroup = TryResolvePersistedElementPath(
            openLeftPanel,
            _callbacks.GetDynamicHints()?.MapStashTierGroupPath,
            IsMapStashTierGroupContainer,
            "map stash tier group");
        if (persistedTierGroup != null)
        {
            _cache.LastAutomationMapStashTierGroupRoot = persistedTierGroup;
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

        _cache.LastAutomationMapStashTierGroupRoot = bestTierGroup;
        TryPersistElementPath(
            openLeftPanel,
            _cache.LastAutomationMapStashTierGroupRoot,
            hints => hints.MapStashTierGroupPath,
            (hints, path) => hints.MapStashTierGroupPath = path,
            "map stash tier group");
        return _cache.LastAutomationMapStashTierGroupRoot;
    }

    private bool IsReusablePageTabContainer(Element root, Element element)
    {
        return IsElementAttachedToRoot(root, element) && CountValidPageTabs(element) >= 6;
    }

    private bool AreCachedPageTabsByNumberValid(Element pageTabContainer, IReadOnlyDictionary<int, Element> pageTabsByNumber)
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

            if (GetPageNumber(entry.Value) != entry.Key)
            {
                return false;
            }
        }

        return true;
    }

    private Element TryResolvePersistedElementPath(
        Element root,
        IReadOnlyList<int> path,
        Func<Element, bool> validator,
        string label)
    {
        if (root == null || path == null || path.Count <= 0 || validator == null)
        {
            return null;
        }

        var resolvedElement = TryGetChildFromIndicesQuietly(root, path);
        if (!validator(resolvedElement))
        {
            return null;
        }

        _callbacks.LogDebug($"Resolved {label} from persisted path {_callbacks.DescribePath(path)}. element={_callbacks.DescribeElement(resolvedElement)}");
        return resolvedElement;
    }

    private List<int> TryPersistElementPath(
        Element root,
        Element target,
        Func<StashAutomationDynamicHintSettings, List<int>> getter,
        Action<StashAutomationDynamicHintSettings, List<int>> setter,
        string label)
    {
        var hints = _callbacks.GetDynamicHints();
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
            _callbacks.LogDebug($"Persisted {label} path unchanged ({_callbacks.DescribePath(resolvedPath)}); skipping settings snapshot save.");
            return resolvedPath;
        }

        setter(hints, resolvedPath);
        _callbacks.LogDebug($"Persisted {label} path {_callbacks.DescribePath(resolvedPath)}");
        _callbacks.SaveSettingsSnapshot();
        return resolvedPath;
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

    private static bool IsElementAttachedToRoot(Element root, Element target)
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

    private int CountValidPageTabs(Element element) =>
        element?.Children?
            .Select(GetPageNumber)
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

    private bool IsPageContentCandidate(Element element) => TryGetPageContentCandidateScore(element, out _, out _);

    private bool TryGetPageContentCandidateScore(Element element, out int mapDescendants, out float area)
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

        if (CountValidPageTabs(element) >= 6 || IsMapStashTierGroupContainer(element))
        {
            return false;
        }

        foreach (var descendant in EnumerateDescendants(element))
        {
            if (CountValidPageTabs(descendant) >= 6 || IsMapStashTierGroupContainer(descendant))
            {
                return false;
            }
        }

        area = GetRectangleArea(element.GetClientRect());
        return true;
    }

    private bool IsReusablePageContentRoot(Element element)
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

        return CountValidPageTabs(element) < 6 && !IsMapStashTierGroupContainer(element);
    }

    private bool TryRememberPageContentRoot(Element root, Element element, string source)
    {
        if (!IsPageContentCandidate(element))
        {
            return false;
        }

        _cache.LastAutomationMapStashPageContentRoot = element;
        var persistedPath = TryPersistElementPath(
            root,
            element,
            hints => hints.MapStashPageContentRootPath,
            (hints, path) => hints.MapStashPageContentRootPath = path,
            "map stash page content root");
        var logSignature = persistedPath != null
            ? _callbacks.DescribePath(persistedPath)
            : _callbacks.DescribeRect(element.GetClientRect());
        if (string.Equals(_cache.LastAutomationMapStashPageContentLogSignature, logSignature, StringComparison.Ordinal))
        {
            return true;
        }

        _cache.LastAutomationMapStashPageContentLogSignature = logSignature;
        if (persistedPath == null)
        {
            _callbacks.LogDebug($"Could not capture map stash page content root path from discovery root. source={source}, root={_callbacks.DescribeElement(root)}, content={_callbacks.DescribeElement(element)}");
        }
        _callbacks.LogDebug($"Map stash page content dynamically resolved via {source}. content={_callbacks.DescribeElement(element)}, mapDescendants={CountVisibleMapEntityDescendants(element)}, children={_callbacks.DescribeChildren(element)}");

        return true;
    }

    private int CountVisibleMapEntityDescendants(Element root) =>
        root == null
            ? 0
            : EnumerateDescendants(root, includeSelf: true).Count(x => x?.IsVisible == true && x.Entity?.Metadata?.IndexOf("Metadata/Items/Maps", StringComparison.OrdinalIgnoreCase) >= 0);

    private static float GetRectangleArea(RectangleF rect) => Math.Max(0, rect.Width) * Math.Max(0, rect.Height);

    private void CollectVisibleEntityDescendants(Element root, ICollection<Element> results)
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

    private T RetryDiscovery<T>(Func<int, T> attemptFunc) where T : class
    {
        for (var attempt = 0; attempt < MapStashDiscoveryRetryCount; attempt++)
        {
            var result = attemptFunc(attempt);
            if (result != null)
            {
                return result;
            }
        }

        return null;
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
}