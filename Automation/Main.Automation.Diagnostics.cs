using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using SharpDX;

namespace BeastsV2;

public partial class Main
{
    #region Diagnostics

    private string _automationOverlayMessage;
    private bool _automationOverlayIsError;
    private DateTime _automationOverlayHideAtUtc;

    private void UpdateAutomationStatus(string message, bool forceLog = false)
    {
        var changed = !string.Equals(_lastAutomationStatusMessage, message, StringComparison.Ordinal);
        SetAutomationOverlayMessage(message, isError: false);
        if (!forceLog && !changed)
        {
            return;
        }
        _lastAutomationStatusMessage = message;
        LogDebug($"STATUS: {message}");
    }
    
    private void ShowAutomationError(string message)
    {
        _lastAutomationStatusMessage = message;
        SetAutomationOverlayMessage(message, isError: true);
    }
    
    private void BeginAutomationOverlaySession()
    {
        ClearAutomationOverlayMessage();
    }
    
    private void EndAutomationOverlaySession()
    {
        if (string.IsNullOrWhiteSpace(_automationOverlayMessage))
        {
            return;
        }

        if (_automationOverlayIsError)
        {
            if (_automationOverlayHideAtUtc == default || _automationOverlayHideAtUtc == DateTime.MaxValue)
            {
                _automationOverlayHideAtUtc = DateTime.UtcNow.AddSeconds(GetAutomationOverlayDurationSeconds(isError: true));
            }

            return;
        }

        _automationOverlayHideAtUtc = DateTime.UtcNow.AddSeconds(GetAutomationOverlayDurationSeconds(isError: false));
    }
    
    private bool TryGetAutomationOverlayMessage(out string message, out bool isError)
    {
        message = null;
        isError = false;

        if (Settings?.AutomationStatusOverlay == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_automationOverlayMessage))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (!_isAutomationRunning && _automationOverlayHideAtUtc != DateTime.MaxValue && now >= _automationOverlayHideAtUtc)
        {
            ClearAutomationOverlayMessage();
            return false;
        }

        message = _automationOverlayMessage;
        isError = _automationOverlayIsError;
        return true;
    }
    
    private void SetAutomationOverlayMessage(string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ClearAutomationOverlayMessage();
            return;
        }

        _automationOverlayMessage = message.Trim();
        _automationOverlayIsError = isError;
        _automationOverlayHideAtUtc = _isAutomationRunning && !isError
            ? DateTime.MaxValue
            : DateTime.UtcNow.AddSeconds(GetAutomationOverlayDurationSeconds(isError));
    }
    
    private void ClearAutomationOverlayMessage()
    {
        _automationOverlayMessage = null;
        _automationOverlayIsError = false;
        _automationOverlayHideAtUtc = default;
    }
    
    private double GetAutomationOverlayDurationSeconds(bool isError)
    {
        var settings = Settings?.AutomationStatusOverlay;
        var seconds = isError
            ? settings?.ErrorDurationSeconds?.Value ?? 10
            : settings?.StatusDurationSeconds?.Value ?? 5;
        return Math.Max(1, seconds);
    }

    private string _logFilePath;
    private bool _logFileSessionStarted;
    private readonly object _logFileLock = new();

    private void WriteToLogFile(string line)
    {
        try
        {
            _logFilePath ??= Path.Combine(ConfigDirectory, "BeastsV2.log");
            lock (_logFileLock)
            {
                if (!_logFileSessionStarted)
                {
                    File.WriteAllText(_logFilePath, $"=== BeastsV2 session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                    _logFileSessionStarted = true;
                }
                File.AppendAllText(_logFilePath, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private void LogDebug(string message)
    {
        var line = $"[BeastsV2] {message}";
        if (Settings?.DebugLogging?.Value == true)
        {
            try { DebugWindow.LogMsg(line); } catch { }
        }
        WriteToLogFile($"[DEBUG] {line}");
    }

    private void Log(string message)
    {
        var line = $"[BeastsV2] {message}";
        try { DebugWindow.LogMsg(line); } catch { }
        WriteToLogFile(line);
    }

    private void LogError(string message, Exception ex = null)
    {
        var full = ex == null ? message : $"{message} {ex.GetType().Name}: {ex.Message}";
        var line = $"[BeastsV2] ERROR: {full}";
        try { DebugWindow.LogMsg(line); } catch { }
        WriteToLogFile(line);
    }

    private static string DescribeTarget(StashAutomationTargetSettings target) =>
        target == null ? "target=null"
        : $"enabled={target.Enabled.Value}, item='{target.ItemName.Value}', quantity={GetConfiguredTargetQuantity(target)}, selectedTab='{target.SelectedTabName.Value}'";

    private static string DescribeStash(StashElement stash) =>
        stash == null ? "stash=null"
        : $"stashVisible={stash.IsVisible}, visibleTabIndex={stash.IndexVisibleStash}, totalTabs={stash.TotalStashes}, visibleType={stash.VisibleStash?.InvType.ToString() ?? "null"}";

    private static string DescribeElement(Element element)
    {
        if (element == null)
        {
            return "element=null";
        }

        try
        {
            return $"visible={element.IsVisible}, children={element.Children?.Count ?? 0}, rect={DescribeRect(element.GetClientRect())}";
        }
        catch (Exception ex)
        {
            return $"element=error({ex.GetType().Name}: {ex.Message})";
        }
    }

    private static string DescribeRect(RectangleF rect) =>
        $"[{rect.Left:0.#},{rect.Top:0.#}] -> [{rect.Right:0.#},{rect.Bottom:0.#}]";

    private static string DescribePath(IReadOnlyList<int> path) =>
        path == null ? "null" : string.Join("->", path);

    private static string DescribePageTabs(IReadOnlyDictionary<int, Element> pageTabsByNumber) =>
        pageTabsByNumber == null || pageTabsByNumber.Count == 0 ? "none"
        : string.Join(" | ", pageTabsByNumber.OrderBy(x => x.Key).Select(x => $"{x.Key}:{DescribeElement(x.Value)}"));

    private static string DescribeChildren(Element parent, int maxChildren = 12) =>
        parent?.Children == null ? "children=null"
        : string.Join(" | ", parent.Children.Take(maxChildren).Select((child, i) => $"{i}:{DescribeElement(child)}"));

    private static int FindChildIndex(Element parent, Element child)
    {
        if (parent?.Children == null || child == null)
        {
            return -1;
        }

        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], child))
            {
                return i;
            }
        }

        return -1;
    }

    private static string DescribeIndexedChildren(Element parent, Func<Element, bool> predicate, int maxChildren = 12)
    {
        if (parent?.Children == null)
        {
            return "children=null";
        }

        var children = parent.Children
            .Select((child, i) => (child, i))
            .Where(tuple => predicate?.Invoke(tuple.child) != false)
            .Take(maxChildren)
            .Select(tuple => $"{tuple.i}:{DescribeElement(tuple.child)}");
        var description = string.Join(" | ", children);
        return string.IsNullOrWhiteSpace(description) ? "none" : description;
    }

    private static string DescribePathLookup(Element root, IReadOnlyList<int> path)
    {
        if (root == null) return $"root=null, path={DescribePath(path)}";
        if (path == null || path.Count == 0) return $"path empty, root={DescribeElement(root)}";

        var sb = new StringBuilder();
        var current = root;
        sb.Append($"root={DescribeElement(root)}");

        for (var i = 0; i < path.Count; i++)
        {
            var idx = path[i];
            var children = current?.Children;
            sb.Append($" -> [{idx}] children={children?.Count ?? 0}");

            if (children == null || idx < 0 || idx >= children.Count)
            {
                sb.Append(" (missing)");
                if (current != null) sb.Append($", siblings={DescribeChildren(current)}");
                return sb.ToString();
            }

            current = children[idx];
            sb.Append($" => {DescribeElement(current)}");
        }

        if (current != null) sb.Append($", finalChildren={DescribeChildren(current)}");
        return sb.ToString();
    }

    private void LogBestiaryUiState(string context)
    {
        var challengesPanel = BestiaryChallengesPanel;
        var bestiaryPanel = TryGetBestiaryPanel();
        var capturedTab = TryGetBestiaryCapturedBeastsTab();
        var buttonContainer = TryGetBestiaryCapturedBeastsButtonContainer();
        var capturedButton = TryGetBestiaryCapturedBeastsButton();
        var windowOpen = TryGetBestiaryCapturedBeastsDisplay(out var beastsDisplay, out var visibleRect);
        var panelContainer = TryGetChildFromIndicesQuietly(challengesPanel, BestiaryPanelPath.Take(BestiaryPanelPath.Count - 1).ToArray());
        var innerRoot = TryGetBestiaryInnerRoot(bestiaryPanel);
        var footer = TryGetBestiaryCapturedBeastsFooter(capturedTab);
        var filterInput = TryFindBestiaryFilterInputContainer(capturedTab);
        var viewport = TryFindBestiaryCapturedBeastsViewport(capturedTab);
        var scrollbar = TryGetBestiaryCapturedBeastsScrollbar(capturedTab);

        LogDebug(
            $"Bestiary UI [{context}] challengeOpen={capturedButton?.IsVisible == true}, capturedTabVisible={capturedTab?.IsVisible == true}, windowOpen={windowOpen}, panel={DescribeElement(bestiaryPanel)}, capturedTab={DescribeElement(capturedTab)}, buttonContainer={DescribeElement(buttonContainer)}, capturedButton={DescribeElement(capturedButton)}, beastsDisplay={DescribeElement(beastsDisplay)}, visibleRect={DescribeRect(visibleRect)}");
        LogDebug(
            $"Bestiary UI paths [{context}] root={DescribeElement(challengesPanel)}, panelPath={DescribePath(BestiaryPanelPath)}, panelTrace={DescribePathLookup(challengesPanel, BestiaryPanelPath)}, capturedTabPath={DescribePath(BestiaryCapturedBeastsTabPath)}, capturedTabTrace={DescribePathLookup(challengesPanel, BestiaryCapturedBeastsTabPath)}, buttonContainerPath={DescribePath(BestiaryCapturedBeastsButtonContainerPath)}, buttonContainerTrace={DescribePathLookup(challengesPanel, BestiaryCapturedBeastsButtonContainerPath)}");
        LogDebug(
            $"Bestiary UI resolved [{context}] panelContainer={DescribeElement(panelContainer)}, resolvedPanelIndex={FindChildIndex(panelContainer, bestiaryPanel)}, visiblePanels={DescribeIndexedChildren(panelContainer, child => child?.IsVisible == true)}, innerRoot={DescribeElement(innerRoot)}, resolvedCapturedTabIndex={FindChildIndex(innerRoot, capturedTab)}, resolvedButtonContainerIndex={FindChildIndex(innerRoot, buttonContainer)}, visiblePageCandidates={DescribeIndexedChildren(innerRoot, child => child?.IsVisible == true && child.GetClientRect().Width >= 300 && child.GetClientRect().Height >= 300)}, strictLayout=footer:{footer != null}, filter:{filterInput != null}, viewport:{viewport != null}, scrollbar:{scrollbar != null}");
    }

    #endregion
}

