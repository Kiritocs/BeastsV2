using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using BeastsV2.Runtime.Analytics;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2;

public partial class Main : BaseSettingsPlugin<Settings>
{
    private static readonly TimeSpan AnalyticsWebSnapshotRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PreparedMapCostCapturePollInterval = TimeSpan.FromMilliseconds(250);
    private const string CounterLabel = "Beasts Found";
    private const string MapTimePrefix = "Map Time:";
    private const string BestiaryScarabOfDuplicatingName = "Bestiary Scarab of Duplicating";
    private const string MissingTrackedBeastName = "\0";
    private const string QuestProgressPattern = @"\((\d+)/(\d+)\)";
    private static readonly GameStat? IsCapturableMonsterStat = TryGetCapturableMonsterStat();
    private static readonly Regex QuestProgressRegex = new(QuestProgressPattern, RegexOptions.Compiled);
    private const int MaxMapHistoryEntries = 200;

    private static readonly TrackedBeast[] AllRedBeasts = BeastsV2BeastData.AllRedBeasts;

    private readonly HashSet<long> _countedRareBeastIds = new();
    private readonly HashSet<long> _capturedBeastIds = new();
    private readonly Dictionary<long, Entity> _trackedBeastEntities = new();
    private readonly Dictionary<string, string> _trackedBeastNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TrackedBeastRenderInfo> _trackedBeastRenderBuffer = new();
    private readonly List<string> _analyticsLineBuffer = new();
    private readonly Dictionary<string, int> _valuableBeastCounts = AllRedBeasts.ToDictionary(x => x.Name, _ => 0);
    private readonly Dictionary<string, int> _currentMapValuableBeastCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _currentMapValuableBeastCapturedCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, AnalyticsBeastEncounterState> _currentMapBeastEncounters = new();
    private readonly List<MapAnalyticsRecord> _mapHistory = new();
    private readonly List<MapCostItem> _preparedMapCostBreakdown = [];
    private readonly List<MapCostItem> _currentMapCostBreakdown = [];
    private readonly List<MapReplayEvent> _currentMapReplayEvents = [];
    private bool _preparedMapUsedDuplicatingScarab;
    private bool _currentMapUsedDuplicatingScarab;
    private int _currentMapBeastsFound;
    private int _currentMapRedBeastsFound;
    private double? _currentMapFirstRedSeenSeconds;
    private DateTime _lastAnalyticsWebSnapshotRefreshUtc;
    private DateTime _lastPreparedMapCostCapturePollUtc;
    private string _renderCounterText = string.Empty;
    private bool _renderAllBeastsFound;
    private bool _renderAllTrackedValuableBeastsCaptured;
    private bool _analyticsCollapsed = true;
    private int _rareBeastsFound;
    private int _sessionBeastsFound;
    private int _totalRedBeastsSession;
    private bool _wasBestiaryTabVisible;
    private bool _isBestiaryClipboardPasteRunning;
    private bool _sawBeastQuestThisMap;

    public Main()
    {
        Name = "Beasts V2";
    }

    public override void OnLoad()
    {
        Core.Initialize(this);

        var now = DateTime.UtcNow;
        Runtime.Initialize(now, CreateSettingsBindingTargets());

        InitializeCurrentAreaTracking(now);

        LoadPersistedBeastPriceSettings();
        QueuePriceFetch();

        _lastAnalyticsWebSnapshotRefreshUtc = DateTime.MinValue;
        RefreshAnalyticsWebSnapshot(now);
        EnsureAnalyticsWebServerState();
    }

    public override void OnClose()
    {
        base.OnClose();
        SavePersistedBeastPriceSettings();
        if (_mapHistory.Count > 0 || _sessionBeastsFound > 0)
            AutoSaveSessionSnapshotToFile();
        DisposeAnalyticsWebServer();
        Runtime.Shutdown();
        _automationInputLockService?.Dispose();
        _automationRunCoordinator = null;
        _automationHotkeyTracker = null;
        _automationInputLockService = null;
        _bestiaryAutomationWorkflow = null;
        _bestiaryUiOpenService = null;
        _bestiaryClearService = null;
        _bestiaryCapturedMonsterStashService = null;
        _mapStashUiService = null;
        _restockTransferBatchService = null;
        _restockTransferConfirmationService = null;
        _restockTransferPlannerService = null;
        _mapDeviceAutomationWorkflow = null;
        _mapDeviceLoadPlanService = null;
        _mapDeviceVerificationService = null;
        _merchantAutomationWorkflow = null;
        _fullSequenceAutomationWorkflow = null;
        _explorationRouteState = null;
        _mapRenderState = null;
        _analyticsPersistenceState = null;
        _analyticsSessionPersistenceService = null;
        _analyticsSnapshotService = null;
        _analyticsWebRuntimeState = null;
        _analyticsWebServerCoordinator = null;
        _analyticsReplayEventTrackerService = null;
        _analyticsMapCostTrackingService = null;
        _analyticsSessionAggregationService = null;
        _bestiaryCapturedBeastsViewService = null;
        _beastLookupService = null;
        _mapRenderPresentationService = null;
        _mapRenderImGuiOverlayService = null;
        _mapRenderBeastOverlayService = null;
        _mapRenderLabelService = null;
        _mapRenderLargeMapOverlayService = null;
        _mapRenderDrawingPrimitivesService = null;
        _mapRenderPathOverlayService = null;
        _explorationRouteRefreshService = null;
        _explorationRoutePlanningService = null;
        _mapRenderPanelOverlayService = null;
        _runtime = null;
        Core.Shutdown();
    }

    private static bool IsHideoutLikeArea(AreaInstance area)
    {
        return area?.IsHideout == true ||
               area?.Name.EqualsIgnoreCase(MenagerieAreaName) == true;
    }

    private static bool IsRunnableMapArea(AreaInstance area)
    {
        return area is { IsTown: false } && !IsHideoutLikeArea(area);
    }

    private void InitializeCurrentAreaTracking(DateTime now)
    {
        var currentArea = GameController?.Area?.CurrentArea;
        _isCurrentAreaTrackable = IsRunnableMapArea(currentArea);
        if (_isCurrentAreaTrackable)
        {
            _activeMapAreaHash = BeastsV2Helpers.TryGetAreaHashText(currentArea);
            _activeMapAreaName = BeastsV2Helpers.TryGetAreaNameText(currentArea);
            _activeMapInstanceId = BeastsV2Helpers.TryGetAreaInstanceId(currentArea);
            _currentMapStartUtc = now;
        }
    }

    private void LoadPersistedBeastPriceSettings()
    {
        try
        {
            var settingsPath = GetBeastsV2SettingsFilePath();
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var root = JObject.Parse(File.ReadAllText(settingsPath));
            if (root["BeastPrices"] is not JObject beastPricesSection)
            {
                return;
            }

            Settings.BeastPrices.LastUpdated = beastPricesSection["LastUpdated"]?.Value<string>() ?? Settings.BeastPrices.LastUpdated;

            if (beastPricesSection["EnabledBeasts"] is JArray enabledBeastsArray)
            {
                Settings.BeastPrices.EnabledBeasts = new HashSet<string>(
                    enabledBeastsArray.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load persisted beast price settings", ex);
        }
    }

    private void SavePersistedBeastPriceSettings()
    {
        try
        {
            var settingsPath = GetBeastsV2SettingsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var content = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
            var root = !string.IsNullOrWhiteSpace(content) ? JObject.Parse(content) : new JObject();
            var beastPricesSection = root["BeastPrices"] as JObject ?? new JObject();
            beastPricesSection["LastUpdated"] = Settings.BeastPrices.LastUpdated;
            beastPricesSection["EnabledBeasts"] = new JArray(Settings.BeastPrices.EnabledBeasts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            root["BeastPrices"] = beastPricesSection;
            File.WriteAllText(settingsPath, root.ToString());
        }
        catch (Exception ex)
        {
            LogError("Failed to save persisted beast price settings", ex);
        }
    }

    private static string GetBeastsV2SettingsFilePath()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "config", "global", SettingsFileName);
    }

    private void QueuePriceFetch()
    {
        _ = Task.Run(FetchBeastPricesAsync);
    }

    public override void AreaChange(AreaInstance area)
    {
        Core.AreaChanged(area);

        var now = DateTime.UtcNow;

        _trackedBeastEntities.Clear();
        _routeNeedsRegen = true;
        CancelBeastPaths();
        PauseCurrentMapTimer(now);

        var decision = Runtime.AreaTransitions.Evaluate(area, now, _currentMapBeastsFound > 0);

        if (decision.ShouldFinalizePreviousMap)
        {
            FinalizeCurrentMapAnalytics(decision.PreviousAreaHash, decision.PreviousAreaName, now);
            FinalizePausedMap();
            AutoSaveSessionSnapshotToFile();
        }

        if (decision.Kind != global::BeastsV2.Runtime.Lifecycle.AreaTransitionKind.EnteredNewTrackableMap)
        {
            return;
        }

        ResetCurrentMapAnalytics();
        BeginCurrentMapCostTrackingFromPrepared();
        ResetCounter();
    }

    public override void EntityAdded(Entity entity)
    {
        if (!IsRareBeast(entity)) return;
        _trackedBeastEntities[entity.Id] = entity;
        if (_countedRareBeastIds.Add(entity.Id))
        {
            _rareBeastsFound++;
            RegisterSessionRareBeast(entity);
        }
    }

    public override void EntityRemoved(Entity entity)
    {
        _trackedBeastEntities.Remove(entity.Id);
    }

    private void TrackBeastCaptureStates()
    {
        if (_trackedBeastEntities.Count == 0)
        {
            return;
        }

        foreach (var (id, entity) in _trackedBeastEntities)
        {
            if (!entity.IsValid) continue;
            if (_capturedBeastIds.Contains(id)) continue;
            if (GetBeastCaptureState(entity) != BeastCaptureState.Captured) continue;
            if (!TryGetTrackedBeastNameCached(entity.Metadata, out var beastName)) continue;

            _capturedBeastIds.Add(id);
            _currentMapValuableBeastCapturedCounts[beastName] =
                _currentMapValuableBeastCapturedCounts.TryGetValue(beastName, out var prev) ? prev + 1 : 1;
            RegisterCurrentMapReplayCaptured(id, beastName, DateTime.UtcNow);
        }
    }

    private IReadOnlyList<TrackedBeastRenderInfo> CollectTrackedBeastRenderInfo()
    {
        _trackedBeastRenderBuffer.Clear();
        var showEnabledOnly = Settings.MapRender.ShowEnabledOnly.Value;
        var enabledBeasts = Settings.BeastPrices.EnabledBeasts;

        foreach (var (_, entity) in _trackedBeastEntities)
        {
            if (!entity.IsValid) continue;
            if (!TryGetTrackedBeastNameCached(entity.Metadata, out var beastName)) continue;
            if (showEnabledOnly && !enabledBeasts.Contains(beastName)) continue;

            var positioned = entity.GetComponent<Positioned>();
            if (positioned == null) continue;

            _trackedBeastRenderBuffer.Add(new TrackedBeastRenderInfo(
                entity,
                positioned,
                beastName,
                GetBeastCaptureState(entity)));
        }

        return _trackedBeastRenderBuffer;
    }

    public override void Render()
    {
        var now = DateTime.UtcNow;

        ApplyPauseMenuTimerState(now);

        if (_isCurrentAreaTrackable)
        {
            TrackBeastCaptureStates();
        }

        UpdateRenderCounterState();

        HandleBestiaryClipboardAutoCopy();

        HandleAutomationHotkey();
        DrawBestiaryAutomationQuickButtons();
        DrawMenagerieInventoryQuickButton();
        TryCapturePreparedMapCostBreakdownFromMapDeviceWindow();

        RefreshAnalyticsWebSnapshot(now);
        EnsureAnalyticsWebServerState();

        var beastPrices = Settings.BeastPrices;
        var mapRender = Settings.MapRender;
        var analyticsWindow = Settings.AnalyticsWindow;

        TryScheduleAutoPriceRefresh(now, beastPrices);

        var shouldCollectTrackedBeastRenderInfo = ShouldCollectTrackedBeastRenderInfo(mapRender);
        IReadOnlyList<TrackedBeastRenderInfo> trackedBeasts = Array.Empty<TrackedBeastRenderInfo>();
        if (shouldCollectTrackedBeastRenderInfo)
        {
            trackedBeasts = CollectTrackedBeastRenderInfo();
        }

        RenderMapOverlays(mapRender, trackedBeasts);

        RenderPriceOverlays(mapRender);

        RenderAnalyticsOverlay(analyticsWindow);

        RenderAutomationStatusOverlay();
    }

    private void RenderMapOverlays(MapRenderSettings mapRender, IReadOnlyList<TrackedBeastRenderInfo> trackedBeasts)
    {
        if (mapRender.ShowBeastLabelsInWorld.Value && trackedBeasts.Count > 0)
        {
            DrawInWorldBeasts(trackedBeasts);
        }

        if (ShouldDrawLargeMapOverlay(mapRender, trackedBeasts.Count > 0) && IsLargeMapVisible())
        {
            DrawBeastsOnLargeMap(trackedBeasts);
        }

        if (mapRender.ShowStylePreviewWindow.Value)
        {
            DrawMapRenderStylePreviewWindow();
        }

        if (mapRender.ShowTrackedBeastsWindow.Value && trackedBeasts.Count > 0)
        {
            DrawTrackedBeastsWindow(trackedBeasts);
        }
    }

    private void RenderPriceOverlays(MapRenderSettings mapRender)
    {
        if (mapRender.ShowPricesInInventory.Value)
        {
            DrawInventoryBeasts();
        }

        if (mapRender.ShowPricesInStash.Value)
        {
            DrawStashBeasts();
        }

        DrawMerchantBeasts();

        if (mapRender.ShowPricesInBestiary.Value)
        {
            DrawBestiaryPanelPrices();
        }
    }

    private void RenderAnalyticsOverlay(AnalyticsWindowSettings analyticsWindow)
    {
        GetOverlayVisibility(out var shouldRenderCounterAndMessage, out var shouldRenderAnalytics);

        if (!shouldRenderCounterAndMessage && !(shouldRenderAnalytics && analyticsWindow.Show.Value))
        {
            return;
        }

        if (shouldRenderCounterAndMessage)
        {
            DrawCounterAndCompletedMessage();
        }

        if (shouldRenderAnalytics && analyticsWindow.Show.Value)
        {
            DrawAnalyticsWindow();
        }
    }

    private void RenderAutomationStatusOverlay()
    {
        var overlay = Settings.AutomationStatusOverlay;
        if (!overlay.Show.Value)
        {
            return;
        }

        var hasLiveMessage = TryGetAutomationOverlayMessage(out var message, out var isError);
        if (!hasLiveMessage)
        {
            if (!overlay.ShowPreviewWhileIdle.Value)
            {
                return;
            }

            message = "Automation status preview";
            isError = false;
        }

        DrawOverlayWindow(
            "##BeastsV2AutomationStatusOverlay",
            message,
            overlay.XPos.Value,
            overlay.YPos.Value,
            overlay.Padding.Value,
            overlay.BorderThickness.Value,
            overlay.BorderRounding.Value,
            overlay.TextScale.Value,
            isError ? overlay.ErrorTextColor.Value : overlay.TextColor.Value,
            isError ? overlay.ErrorBorderColor.Value : overlay.BorderColor.Value,
            overlay.BackgroundColor.Value);
    }

    private void TryScheduleAutoPriceRefresh(DateTime now, BeastPricesSettings beastPrices)
    {
        var autoRefreshMinutes = beastPrices.AutoRefreshMinutes.Value;
        if (autoRefreshMinutes <= 0 || _isFetchingPrices ||
            (now - _lastPriceFetchAttempt).TotalMinutes < autoRefreshMinutes)
        {
            return;
        }

        QueuePriceFetch();
    }

    private void HandleAutomationHotkey()
    {
        var a = Settings.StashAutomation;
        if (CheckAndFireHotkey(Settings.FullSequenceAutomation.FullSequenceHotkey, "Full sequence", RunFullSequenceAutomationAsync)) return;
        if (CheckAndFireHotkey(Settings.BestiaryAutomation.RegexItemizeHotkey, "Bestiary regex itemize", RunBestiaryRegexItemizeAutomationFromHotkeyAsync)) return;
        if (CheckAndFireHotkey(Settings.BestiaryAutomation.DeleteHotkey, "Bestiary delete", RunBestiaryDeleteAutomationFromHotkeyAsync)) return;
        if (CheckAndFireHotkey(Settings.MerchantAutomation.FaustusListHotkey, "Faustus list", RunSellCapturedMonstersToFaustusAsync)) return;
        if (CheckAndFireHotkey(a.LoadMapDeviceHotkey, "Load map device", RunMapDeviceAutomationFromHotkeyAsync)) return;
        CheckAndFireHotkey(a.RestockHotkey, "Restock", RunStashAutomationFromHotkeyAsync);
    }

    private bool CheckAndFireHotkey(HotkeyNodeV2 hotkey, string label, Func<Task> action)
    {
        if (!TryGetPressedAutomationHotkey(hotkey, out var key, out var usedKeyDownFallback)) return false;

        LogDebug($"{label} hotkey pressed. key={key}, source={(usedKeyDownFallback ? "keydown-fallback" : "pressed-once")}");
        _ = action();
        return true;
    }

    private bool TryGetPressedAutomationHotkey(HotkeyNodeV2 hotkey, out Keys key, out bool usedKeyDownFallback)
    {
        return AutomationHotkeys.TryGetPressedHotkey(hotkey, _isAutomationRunning, out key, out usedKeyDownFallback);
    }

    private bool ShouldCollectTrackedBeastRenderInfo(MapRenderSettings mapRender)
    {
        return mapRender.ShowBeastLabelsInWorld.Value ||
               (mapRender.ShowBeastsOnMap.Value && IsLargeMapVisible()) ||
               mapRender.ShowTrackedBeastsWindow.Value;
    }

    private static bool ShouldDrawLargeMapOverlay(MapRenderSettings mapRender, bool hasTrackedBeasts)
    {
        var explorationRoute = mapRender.ExplorationRoute;
        return (mapRender.ShowBeastsOnMap.Value && hasTrackedBeasts) ||
               (explorationRoute.Enabled.Value && (
                   explorationRoute.ShowExplorationRoute.Value ||
                   explorationRoute.ShowPathsToBeasts.Value ||
                   explorationRoute.ShowCoverageOnMiniMap.Value));
    }

    private bool IsLargeMapVisible() => GameController?.IngameState?.IngameUi?.Map?.LargeMap?.IsVisible == true;

    private void DrawCounterAndCompletedMessage()
    {
        var counterText = _renderCounterText;
        var allBeastsFound = _renderAllBeastsFound;
        var allTrackedValuableBeastsCaptured = _renderAllTrackedValuableBeastsCaptured;

        var counterWindow = Settings.CounterWindow;
        var completedCounter = counterWindow.CompletedStyle;
        var completedMessage = counterWindow.CompletedMessage;
        var trackedCompletionMessage = counterWindow.TrackedCompletionMessage;
        var showCompletedCounterStyle = allBeastsFound || completedCounter.ShowWhileNotComplete.Value;

        if (counterWindow.Show.Value)
        {
            var counterTextColor = showCompletedCounterStyle ? completedCounter.TextColor.Value : counterWindow.TextColor.Value;
            var counterBorderColor = showCompletedCounterStyle ? completedCounter.BorderColor.Value : counterWindow.BorderColor.Value;
            var counterTextScale = showCompletedCounterStyle ? completedCounter.TextScale.Value : counterWindow.TextScale.Value;

            DrawOverlayWindow(
                "##BeastsV2Overlay",
                counterText,
                counterWindow.XPos.Value,
                counterWindow.YPos.Value,
                counterWindow.Padding.Value,
                counterWindow.BorderThickness.Value,
                counterWindow.BorderRounding.Value,
                counterTextScale,
                counterTextColor,
                counterBorderColor,
                counterWindow.BackgroundColor.Value);
        }

        var shouldShowCompletedMessage =
            completedMessage.Show.Value &&
            !string.IsNullOrWhiteSpace(completedMessage.Text.Value) &&
            (allBeastsFound || completedMessage.ShowWhileNotComplete.Value);

        var shouldShowTrackedCompletionMessage =
            trackedCompletionMessage.Show.Value &&
            !string.IsNullOrWhiteSpace(trackedCompletionMessage.Text.Value) &&
            (allTrackedValuableBeastsCaptured || trackedCompletionMessage.ShowWhileNotComplete.Value);

        if (!shouldShowCompletedMessage && !shouldShowTrackedCompletionMessage)
        {
            return;
        }

        if (shouldShowCompletedMessage)
            DrawMessageWindow("##BeastsV2CompletedMessageOverlay", completedMessage);

        if (shouldShowTrackedCompletionMessage)
            DrawMessageWindow("##BeastsV2TrackedCompletionMessageOverlay", trackedCompletionMessage);
    }

    private void DrawMessageWindow(string windowId, CompletedMessageWindowSettings s) =>
        DrawOverlayWindow(windowId, s.Text.Value, s.XPos.Value, s.YPos.Value,
            s.Padding.Value, s.BorderThickness.Value, s.BorderRounding.Value,
            s.TextScale.Value, s.TextColor.Value, s.BorderColor.Value, s.BackgroundColor.Value);

    private void DrawAnalyticsWindow()
    {
        var includeBeastBreakdown = !_analyticsCollapsed;
        var allLines = _analyticsLineBuffer;
        BuildAnalyticsLines(allLines, includeBeastBreakdown);
        if (allLines.Count == 0) return;

        var displayText = includeBeastBreakdown
            ? string.Join('\n', allLines)
            : allLines[0];

        var s = Settings.AnalyticsWindow;
        var windowRect = GameController.Window.GetWindowRectangle();
        var anchor = new Vector2(
            windowRect.Width * (s.XPos.Value / 100f),
            windowRect.Height * (s.YPos.Value / 100f));

        var baseTextSize = ImGui.CalcTextSize(displayText);
        var estimatedWindowSize = new Vector2(
            baseTextSize.X * s.TextScale.Value + s.Padding.Value * 2,
            baseTextSize.Y * s.TextScale.Value + s.Padding.Value * 2);

        var position = new Vector2(anchor.X, anchor.Y);

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(estimatedWindowSize, ImGuiCond.Always);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, BeastsV2Helpers.ToImGuiColor(s.BackgroundColor.Value));
        ImGui.PushStyleColor(ImGuiCol.Border, BeastsV2Helpers.ToImGuiColor(s.BorderColor.Value));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, s.BorderRounding.Value);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, s.BorderThickness.Value);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(s.Padding.Value, s.Padding.Value));

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoMove;

        ImGui.Begin("##BeastsV2AnalyticsOverlay", flags);
        ImGui.SetWindowFontScale(s.TextScale.Value);
        ImGui.TextColored(BeastsV2Helpers.ToImGuiColor(s.TextColor.Value), displayText);
        ImGui.SetWindowFontScale(1f);

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _analyticsCollapsed = !_analyticsCollapsed;
        }

        ImGui.End();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void DrawOverlayWindow(
        string windowId,
        string text,
        float xPosPercent,
        float yPosPercent,
        float padding,
        int borderThickness,
        float borderRounding,
        float textScale,
        Color textColor,
        Color borderColor,
        Color backgroundColor)
    {
        var windowRect = GameController.Window.GetWindowRectangle();
        var anchor = new Vector2(
            windowRect.Width * (xPosPercent / 100f),
            windowRect.Height * (yPosPercent / 100f));

        var baseTextSize = ImGui.CalcTextSize(text);
        var estimatedWindowSize = new Vector2(
            baseTextSize.X * textScale + padding * 2,
            baseTextSize.Y * textScale + padding * 2);

        var position = new Vector2(anchor.X - estimatedWindowSize.X / 2f, anchor.Y);

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(estimatedWindowSize, ImGuiCond.Always);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, BeastsV2Helpers.ToImGuiColor(backgroundColor));
        ImGui.PushStyleColor(ImGuiCol.Border, BeastsV2Helpers.ToImGuiColor(borderColor));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, borderRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, borderThickness);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoMove;

        ImGui.Begin(windowId, flags);
        ImGui.SetWindowFontScale(textScale);
        ImGui.TextColored(BeastsV2Helpers.ToImGuiColor(textColor), text);
        ImGui.SetWindowFontScale(1f);
        ImGui.End();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private void ResetCounter()
    {
        _countedRareBeastIds.Clear();
        _capturedBeastIds.Clear();
        _rareBeastsFound = 0;
        _sawBeastQuestThisMap = false;
    }

    private void MarkAllMapBeastsCaptured()
    {
        foreach (var (beastName, count) in _currentMapValuableBeastCounts)
        {
            var alreadyCaptured = _currentMapValuableBeastCapturedCounts.TryGetValue(beastName, out var prev) ? prev : 0;
            if (alreadyCaptured < count)
                _currentMapValuableBeastCapturedCounts[beastName] = count;
        }

        foreach (var (id, entity) in _trackedBeastEntities)
        {
            if (entity.IsValid && TryGetTrackedBeastNameCached(entity.Metadata, out var beastName))
            {
                _capturedBeastIds.Add(id);
                RegisterCurrentMapReplayCaptured(id, beastName, DateTime.UtcNow);
            }
        }
    }

    private static bool IsRareBeast(Entity entity)
    {
        return entity.Rarity == MonsterRarity.Rare &&
               IsCapturableMonsterStat is { } capturableStat &&
               entity.Stats?.ContainsKey(capturableStat) == true;
    }

    private static GameStat? TryGetCapturableMonsterStat()
    {
        return Enum.TryParse<GameStat>("IsCapturableMonster", out var stat) ? stat : null;
    }

    private void BuildCounterDisplay(out string text, out bool allBeastsFound)
    {
        allBeastsFound = false;

        if (TryGetBeastQuestProgress(out _, out var totalBeasts) && totalBeasts > 0)
        {
            _sawBeastQuestThisMap = true;
            text = $"{CounterLabel}: {_rareBeastsFound}/{totalBeasts}";
            allBeastsFound = _rareBeastsFound >= totalBeasts;
            return;
        }

        text = $"{CounterLabel}: {_rareBeastsFound}";
    }

    private void UpdateRenderCounterState()
    {
        if (Settings?.Visibility?.HideInHideout?.Value == true && IsHideoutLikeArea(GameController?.Area?.CurrentArea))
        {
            _renderAllBeastsFound = false;
            _renderAllTrackedValuableBeastsCaptured = false;
            return;
        }

        BuildCounterDisplay(out _renderCounterText, out var allBeastsFound);

        var allTrackedValuableBeastsCaptured = false;
        if (allBeastsFound || _currentMapWasComplete)
        {
            allTrackedValuableBeastsCaptured = AreAllTrackedValuableBeastsCaptured();
        }

        if (_isCurrentAreaTrackable && !_currentMapWasComplete)
        {
            if (allBeastsFound && allTrackedValuableBeastsCaptured)
            {
                _currentMapWasComplete = true;
            }
            else if (_sawBeastQuestThisMap && IsBeastQuestMissionComplete())
            {
                MarkAllMapBeastsCaptured();
                _currentMapWasComplete = true;
                allTrackedValuableBeastsCaptured = true;
            }
        }

        _renderAllBeastsFound = allBeastsFound || _currentMapWasComplete;
        _renderAllTrackedValuableBeastsCaptured = _renderAllBeastsFound && allTrackedValuableBeastsCaptured;
    }

    private bool AreAllTrackedValuableBeastsCaptured()
    {
        var enabledBeasts = Settings.BeastPrices.EnabledBeasts;
        var tracked = _trackedBeastEntities.Values
            .Where(e => e?.IsValid == true)
            .Where(e => TryGetTrackedBeastNameCached(e.Metadata, out var name) &&
                        (enabledBeasts.Count == 0 || enabledBeasts.Contains(name)))
            .ToList();

        if (tracked.Count == 0 && enabledBeasts.Count > 0)
            return _currentMapBeastsFound > 0;

        return tracked.All(e => GetBeastCaptureState(e) == BeastCaptureState.Captured);
    }

    private void HandleBestiaryClipboardAutoCopy()
    {
        if (!Settings.BestiaryClipboard.EnableAutoCopy.Value)
        {
            _wasBestiaryTabVisible = false;
            return;
        }

        var isVisible = IsBestiaryTabVisible();
        if (isVisible && !_wasBestiaryTabVisible)
        {
            var regex = GetConfiguredBestiaryRegex();
            ImGui.SetClipboardText(regex);

            if (!_isAutomationRunning &&
                Settings.BestiaryClipboard.AutoPasteAfterCopy.Value &&
                !_isBestiaryClipboardPasteRunning &&
                !string.IsNullOrWhiteSpace(regex))
            {
                _isBestiaryClipboardPasteRunning = true;
                _ = ApplyBestiaryClipboardAutoPasteAsync(regex);
            }
        }

        _wasBestiaryTabVisible = isVisible;

        async Task ApplyBestiaryClipboardAutoPasteAsync(string regexToPaste)
        {
            try
            {
                await DelayAutomationAsync(Math.Max(AutomationTiming.FastPollDelayMs, 25));
                if (_isAutomationRunning || !IsBestiaryTabVisible())
                {
                    return;
                }

                await ApplyBestiarySearchRegexAsync(regexToPaste);
            }
            catch (Exception ex)
            {
                LogDebug($"Bestiary clipboard auto-paste skipped. {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _isBestiaryClipboardPasteRunning = false;
            }
        }
    }

    private string GetConfiguredBestiaryRegex()
    {
        return Settings.BestiaryClipboard.UseAutoRegex.Value
            ? BuildAutoRegexFromEnabledBeasts()
            : (Settings.BestiaryClipboard.BeastRegex.Value ?? string.Empty);
    }

    private string BuildAutoRegexFromEnabledBeasts()
    {
        var enabledBeasts = Settings.BeastPrices.EnabledBeasts;
        if (enabledBeasts.Count == 0) return string.Empty;
        return string.Join('|', AllRedBeasts
            .Where(b => enabledBeasts.Contains(b.Name) && !string.IsNullOrEmpty(b.RegexFragment))
            .Select(b => b.RegexFragment));
    }

    private string GetAnalyticsWebServerUrl()
    {
        return AnalyticsWeb.GetServerUrl();
    }

    private void CopyAnalyticsWebServerUrlToClipboard()
    {
        try
        {
            ImGui.SetClipboardText(GetAnalyticsWebServerUrl());
            LogDebug($"Analytics web server URL copied: {GetAnalyticsWebServerUrl()}");
        }
        catch (Exception ex)
        {
            LogError("Failed to copy analytics web server URL", ex);
        }
    }

    private void OpenAnalyticsWebServerInBrowser()
    {
        try
        {
            EnsureAnalyticsWebServerState();
            var url = GetAnalyticsWebServerUrl();
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            LogDebug($"Opened analytics web server URL in browser: {url}");
        }
        catch (Exception ex)
        {
            LogError("Failed to open analytics web server URL in browser", ex);
        }
    }

    private void RefreshAnalyticsWebSnapshot(DateTime now)
    {
        if (Settings?.AnalyticsWebServer?.Enabled?.Value != true)
        {
            _lastAnalyticsWebSnapshotRefreshUtc = DateTime.MinValue;
            return;
        }

        if (_lastAnalyticsWebSnapshotRefreshUtc != DateTime.MinValue &&
            now - _lastAnalyticsWebSnapshotRefreshUtc < AnalyticsWebSnapshotRefreshInterval)
        {
            return;
        }

        AnalyticsWeb.RefreshSnapshot(now);
        _lastAnalyticsWebSnapshotRefreshUtc = now;
    }

    private void EnsureAnalyticsWebServerState()
    {
        AnalyticsWeb.EnsureServerState();
    }

    private void DisposeAnalyticsWebServer()
    {
        AnalyticsWeb.DisposeServer();
    }
}
