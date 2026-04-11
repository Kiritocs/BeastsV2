using System;
using System.Collections.Generic;
using System.Linq;
using BeastsV2.Runtime.Features;
using ExileCore.PoEMemory.Components;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2;

public partial class Main
{
    // ── Exploration route ────────────────────────────────────────────────────

    private static readonly Vector2[] RadiusCirclePoints = BeastsV2Helpers.CreateUnitCirclePoints(48);
    private List<Vector2> _explorationRoute { get => ExplorationRouteRuntime.Route; set => ExplorationRouteRuntime.Route = value; }
    private HashSet<int> _visitedWaypointIndices => ExplorationRouteRuntime.VisitedWaypointIndices;
    private bool _routeNeedsRegen { get => ExplorationRouteRuntime.RouteNeedsRegen; set => ExplorationRouteRuntime.RouteNeedsRegen = value; }
    private int _lastRouteDetectionRadius { get => ExplorationRouteRuntime.LastRouteDetectionRadius; set => ExplorationRouteRuntime.LastRouteDetectionRadius = value; }
    private bool? _lastPreferPerimeterFirstRoute { get => ExplorationRouteRuntime.LastPreferPerimeterFirstRoute; set => ExplorationRouteRuntime.LastPreferPerimeterFirstRoute = value; }
    private bool? _lastVisitOuterShellLast { get => ExplorationRouteRuntime.LastVisitOuterShellLast; set => ExplorationRouteRuntime.LastVisitOuterShellLast = value; }
    private bool? _lastFollowMapOutlineFirst { get => ExplorationRouteRuntime.LastFollowMapOutlineFirst; set => ExplorationRouteRuntime.LastFollowMapOutlineFirst = value; }
    private bool? _lastExplorationRouteEnabled { get => ExplorationRouteRuntime.LastEnabled; set => ExplorationRouteRuntime.LastEnabled = value; }
    private string _lastExcludedEntityPathsSnapshot { get => ExplorationRouteRuntime.LastExcludedEntityPathsSnapshot; set => ExplorationRouteRuntime.LastExcludedEntityPathsSnapshot = value; }
    private int _lastEntityExclusionRadius { get => ExplorationRouteRuntime.LastEntityExclusionRadius; set => ExplorationRouteRuntime.LastEntityExclusionRadius = value; }
    private int _explorationRouteCacheStep { get => ExplorationRouteRuntime.ExplorationRouteCacheStep; set => ExplorationRouteRuntime.ExplorationRouteCacheStep = value; }
    private int _explorationRouteCacheMinWallDist { get => ExplorationRouteRuntime.ExplorationRouteCacheMinWallDist; set => ExplorationRouteRuntime.ExplorationRouteCacheMinWallDist = value; }
    private bool[][] _debugReachableMask { get => ExplorationRouteRuntime.DebugReachableMask; set => ExplorationRouteRuntime.DebugReachableMask = value; }
    private int[][] _debugDistanceField { get => ExplorationRouteRuntime.DebugDistanceField; set => ExplorationRouteRuntime.DebugDistanceField = value; }
    private int _debugMaxX { get => ExplorationRouteRuntime.DebugMaxX; set => ExplorationRouteRuntime.DebugMaxX = value; }
    private int _debugMaxY { get => ExplorationRouteRuntime.DebugMaxY; set => ExplorationRouteRuntime.DebugMaxY = value; }
    private List<Vector2> _exclusionZonePositions => ExplorationRouteRuntime.ExclusionZonePositions;

    private bool TryGetLocalPlayerGridPosition(out Vector2 playerGridPos)
    {
        var positioned = GameController?.Game?.IngameState?.Data?.LocalPlayer?.GetComponent<Positioned>();
        if (positioned == null)
        {
            playerGridPos = default;
            return false;
        }

        playerGridPos = new Vector2(positioned.GridPosNum.X, positioned.GridPosNum.Y);
        return true;
    }

    private bool IsExplorationRouteEnabled() => Settings.MapRender.ExplorationRoute.Enabled.Value;

    private void ClearExplorationRouteState()
    {
        _explorationRoute.Clear();
        _visitedWaypointIndices.Clear();
        _exclusionZonePositions.Clear();
        _debugReachableMask = null;
        _debugDistanceField = null;
        _debugMaxX = 0;
        _debugMaxY = 0;
        _explorationRouteCacheStep = 4;
        _explorationRouteCacheMinWallDist = 6;
    }

    internal int GetNextWaypointIndex()
    {
        if (!IsExplorationRouteEnabled()) return -1;

        for (var i = 0; i < _explorationRoute.Count; i++)
            if (!_visitedWaypointIndices.Contains(i)) return i;
        return -1;
    }

    internal void UpdateVisitedWaypoints(Vector2 playerGridPos)
    {
        if (!IsExplorationRouteEnabled() || _explorationRoute.Count == 0) return;

        var visitRadius = Settings.MapRender.ExplorationRoute.WaypointVisitRadius.Value;
        var visitSq     = (float)(visitRadius * visitRadius);
        var anyNew      = false;

        for (var i = 0; i < _explorationRoute.Count; i++)
        {
            if (_visitedWaypointIndices.Contains(i)) continue;
            var d = _explorationRoute[i] - playerGridPos;
            if (d.X * d.X + d.Y * d.Y <= visitSq)
            {
                _visitedWaypointIndices.Add(i);
                anyNew = true;
            }
        }

        if (anyNew)
            ReSortUnvisited(playerGridPos);
    }

    // Separates visited / unvisited waypoints, re-orders the unvisited list from the
    // player's current position, then rebuilds the route.
    private void ReSortUnvisited(Vector2 playerGridPos)
    {
        var visited   = new List<Vector2>();
        var unvisited = new List<Vector2>();
        for (var i = 0; i < _explorationRoute.Count; i++)
        {
            if (_visitedWaypointIndices.Contains(i)) visited.Add(_explorationRoute[i]);
            else                                     unvisited.Add(_explorationRoute[i]);
        }

        if (unvisited.Count == 0) return;

        var sorted = TryOrderUnvisitedForExploration(unvisited, playerGridPos);

        _explorationRoute = visited.Concat(sorted).ToList();
        _visitedWaypointIndices.Clear();
        for (var i = 0; i < visited.Count; i++)
            _visitedWaypointIndices.Add(i);

        CancelBeastPaths();
    }

    private void GenerateExplorationRoute()
    {
        if (!IsExplorationRouteEnabled())
        {
            ClearExplorationRouteState();
            return;
        }

        _explorationRoute.Clear();
        _visitedWaypointIndices.Clear();
        _exclusionZonePositions.Clear();

        var pathData = GameController?.IngameState?.Data?.RawPathfindingData;
        if (pathData == null) return;

        var areaDimensions = GameController?.IngameState?.Data?.AreaDimensions;
        if (areaDimensions == null || !TryGetLocalPlayerGridPosition(out var playerPos)) return;

        var maxX = areaDimensions.Value.X;
        var maxY = Math.Min(pathData.Length, areaDimensions.Value.Y);
        var exploration = Settings.MapRender.ExplorationRoute;
        var exclusionZonePositions = ResolveEntityExclusionZonePositions();
        _exclusionZonePositions.AddRange(exclusionZonePositions);

        var plan = ExplorationRoutePlanning.GeneratePlan(new ExplorationRoutePlanningRequest(
            pathData,
            maxX,
            maxY,
            playerPos,
            exploration.DetectionRadius.Value,
            exploration.PreferPerimeterFirstRoute.Value,
            exploration.VisitOuterShellLast.Value,
            exploration.FollowMapOutlineFirst.Value,
            exploration.EntityExclusionRadius.Value,
            exclusionZonePositions));
        if (plan == null) return;

        _debugReachableMask = plan.DebugReachableMask;
        _debugDistanceField = plan.DebugDistanceField;
        _debugMaxX = plan.DebugMaxX;
        _debugMaxY = plan.DebugMaxY;
        _explorationRouteCacheStep = plan.RouteStep;
        _explorationRouteCacheMinWallDist = plan.MinWallDist;
        _explorationRoute = plan.Route;
    }

    private List<Vector2> ResolveEntityExclusionZonePositions()
    {
        var excludedPathsSetting = Settings.MapRender.ExplorationRoute.ExcludedEntityPaths.Value;
        var excludedPaths = SplitExcludedEntityPathsSetting(excludedPathsSetting);
        if (excludedPaths.Count == 0) return [];

        var clusterTarget = GameController.PluginBridge
            .GetMethod<Func<string, int, Vector2[]>>("Radar.ClusterTarget");
        if (clusterTarget == null) return [];

        var positions = new List<Vector2>();
        foreach (var path in excludedPaths)
        {
            var locations = clusterTarget(path, 1);
            if (locations == null) continue;
            positions.AddRange(locations);
        }

        return positions;
    }

    private List<Vector2> TryOrderUnvisitedForExploration(List<Vector2> unvisited, Vector2 playerGridPos) =>
        ExplorationRoutePlanning.OrderUnvisited(new ExplorationRouteReorderRequest(
            unvisited,
            playerGridPos,
            Settings.MapRender.ExplorationRoute.FollowMapOutlineFirst.Value,
            Settings.MapRender.ExplorationRoute.PreferPerimeterFirstRoute.Value,
            _debugReachableMask,
            _debugMaxX,
            _debugMaxY,
            _explorationRouteCacheStep,
            _explorationRouteCacheMinWallDist,
            _debugDistanceField,
            Settings.MapRender.ExplorationRoute.VisitOuterShellLast.Value));

    private static bool IsWalkableCell(int[][] pathData, int y, int x)
    {
        if (y < 0 || y >= pathData.Length || pathData[y] == null) return false;
        if (x < 0 || x >= pathData[y].Length) return false;
        var v = pathData[y][x];
        return v is >= 1 and <= 5;
    }

    private void DrawExplorationRouteOnMap(Vector2 mapCenter)
    {
        if (!IsExplorationRouteEnabled()) return;

        EnsureExplorationRouteIsCurrent();

        if (_explorationRoute.Count == 0 || !TryGetLocalPlayerGridPosition(out var playerGridPos)) return;

        UpdateVisitedWaypoints(playerGridPos);
        var nextIdx = GetNextWaypointIndex();

        var style = Settings.MapRender.ExplorationRoute.Style;
        if (Settings.MapRender.ExplorationRoute.ShowCoverageOnMiniMap.Value)
        {
            var coverageCol = BeastsV2Helpers.ToImGuiColorU32(style.CoverageColor.Value);
            var coverageRadius = Settings.MapRender.ExplorationRoute.DetectionRadius.Value;
            for (var i = 0; i < _explorationRoute.Count; i++)
            {
                if (_visitedWaypointIndices.Contains(i)) continue;
                DrawRadiusCircleOnMap(
                    mapCenter + TranslateGridDeltaToMapDelta(_explorationRoute[i] - playerGridPos, 0),
                    coverageRadius, coverageCol, style.CoverageLineThickness.Value);
            }
        }

        var visitedCol = BeastsV2Helpers.ToImGuiColorU32(style.VisitedLineColor.Value);
        var routeCol = BeastsV2Helpers.ToImGuiColorU32(style.RouteLineColor.Value);
        var nextCol = BeastsV2Helpers.ToImGuiColorU32(style.NextWaypointColor.Value);
        var waypointCol = BeastsV2Helpers.ToImGuiColorU32(style.WaypointColor.Value);
        var lineThickness = style.RouteLineThickness.Value;
        var waypointRadius = style.WaypointDotRadius.Value;
        var nextWaypointRadius = style.NextWaypointDotRadius.Value;

        for (var i = 0; i < _explorationRoute.Count - 1; i++)
        {
            var a = mapCenter + TranslateGridDeltaToMapDelta(_explorationRoute[i] - playerGridPos, 0);
            var b = mapCenter + TranslateGridDeltaToMapDelta(_explorationRoute[i + 1] - playerGridPos, 0);
            var col = _visitedWaypointIndices.Contains(i) && _visitedWaypointIndices.Contains(i + 1)
                ? visitedCol : routeCol;
            _mapDrawList.AddLine(a, b, col, lineThickness);
        }

        for (var i = 0; i < _explorationRoute.Count; i++)
        {
            var pos = mapCenter + TranslateGridDeltaToMapDelta(_explorationRoute[i] - playerGridPos, 0);
            if (i == nextIdx)
                _mapDrawList.AddCircleFilled(pos, nextWaypointRadius, nextCol);
            else if (!_visitedWaypointIndices.Contains(i))
                _mapDrawList.AddCircleFilled(pos, waypointRadius, waypointCol);
        }

        DrawDetectionRadiusOnMap(mapCenter, Settings.MapRender.ExplorationRoute.DetectionRadius.Value,
            style.DetectionRadiusColor.Value, style.DetectionRadiusThickness.Value);
    }

    private void DrawDetectionRadiusOnMap(Vector2 mapCenter, int radius, Color color, float thickness)
    {
        DrawRadiusCircleOnMap(mapCenter, radius, BeastsV2Helpers.ToImGuiColorU32(color), thickness);
    }

    private void DrawEntityExclusionZones(Vector2 mapCenter)
    {
        if (!IsExplorationRouteEnabled()) return;
        if (!Settings.MapRender.ExplorationRoute.ShowEntityExclusionZones.Value) return;

        EnsureExplorationRouteIsCurrent();

        if (_exclusionZonePositions.Count == 0 || !TryGetLocalPlayerGridPosition(out var playerGridPos)) return;

        var exclusionRadius = Settings.MapRender.ExplorationRoute.EntityExclusionRadius.Value;
        var color = BeastsV2Helpers.ToImGuiColorU32(Settings.MapRender.ExplorationRoute.ExclusionZoneColor.Value);

        foreach (var loc in _exclusionZonePositions)
        {
            var mapPos = mapCenter + TranslateGridDeltaToMapDelta(loc - playerGridPos, 0);
            DrawRadiusCircleOnMap(mapPos, exclusionRadius, color, 1.5f);
        }
    }

    private void DrawRadiusCircleOnMap(Vector2 center, int radius, uint color, float thickness)
    {
        Vector2? prev = null;
        foreach (var point in RadiusCirclePoints)
        {
            var mapPos = center + TranslateGridDeltaToMapDelta(point * radius, 0);
            if (prev.HasValue)
            {
                _mapDrawList.AddLine(prev.Value, mapPos, color, thickness);
            }

            prev = mapPos;
        }
    }

    private static float GetHeightDeltaInGridUnits(float[][] heightData, Vector2 gridPos, float playerHeight)
    {
        return BeastsV2Helpers.TryGetTerrainHeight(heightData, (int)gridPos.X, (int)gridPos.Y, out var terrainHeight)
            ? (playerHeight + terrainHeight) / GridToWorldMultiplier
            : playerHeight / GridToWorldMultiplier;
    }

    private void DrawExplorationDebugOnMap(Vector2 mapCenter)
    {
        if (!IsExplorationRouteEnabled()) return;

        var dbg = Settings.MapRender.ExplorationRoute.Debug;
        var showWalkable = dbg.ShowWalkableCells.Value;
        var showObstacles = dbg.ShowObstacleCells.Value;
        var showDistField = dbg.ShowDistanceField.Value;
        if (!showWalkable && !showObstacles && !showDistField) return;

        EnsureExplorationRouteIsCurrent();

        var pathData = GameController?.IngameState?.Data?.RawPathfindingData;
        if (pathData == null || !TryGetLocalPlayerGridPosition(out var playerGridPos)) return;

        var px = (int)playerGridPos.X;
        var py = (int)playerGridPos.Y;

        var radius     = dbg.DebugCellRadius.Value;
        var sampleStep = Math.Max(1, dbg.DebugCellSampleStep.Value);
        var dotRadius  = dbg.DebugDotRadius.Value;

        var maxY = _debugMaxY > 0 ? _debugMaxY : pathData.Length;
        var maxX = _debugMaxX > 0 ? _debugMaxX : (pathData.Length > 0 ? pathData[0]?.Length ?? 0 : 0);

        var yStart = Math.Max(0, py - radius);
        var yEnd   = Math.Min(maxY, py + radius);
        var xStart = Math.Max(0, px - radius);
        var xEnd   = Math.Min(maxX, px + radius);

        var walkableCol = BeastsV2Helpers.ToImGuiColorU32(dbg.WalkableColor.Value);
        var obstacleCol = BeastsV2Helpers.ToImGuiColorU32(dbg.ObstacleColor.Value);

        var maxDist = 1;
        if (showDistField && _debugDistanceField != null)
        {
            for (var cy = yStart; cy < yEnd; cy += sampleStep)
            {
                var distRow = _debugDistanceField[cy];
                if (distRow == null) continue;
                for (var cx = xStart; cx < xEnd; cx += sampleStep)
                {
                    if (cx >= distRow.Length) break;
                    var d = distRow[cx];
                    if (d != int.MaxValue && d > maxDist) maxDist = d;
                }
            }
        }

        for (var cy = yStart; cy < yEnd; cy += sampleStep)
        for (var cx = xStart; cx < xEnd; cx += sampleStep)
        {
            var walkable = IsWalkableCell(pathData, cy, cx);
            var mapPos   = mapCenter + TranslateGridDeltaToMapDelta(new Vector2(cx, cy) - playerGridPos, 0);

            if (showDistField && walkable && _debugDistanceField != null)
            {
                var distRow = _debugDistanceField[cy];
                var d = distRow != null && cx < distRow.Length ? distRow[cx] : 0;
                if (d == int.MaxValue) d = maxDist;
                _mapDrawList.AddCircleFilled(mapPos, dotRadius, DistanceHeatmapColor(d, maxDist));
            }
            else if (showWalkable && walkable)
            {
                _mapDrawList.AddCircleFilled(mapPos, dotRadius, walkableCol);
            }
            else if (showObstacles && !walkable)
            {
                var adj = IsWalkableCell(pathData, cy, cx + sampleStep) ||
                          IsWalkableCell(pathData, cy, cx - sampleStep) ||
                          IsWalkableCell(pathData, cy + sampleStep, cx) ||
                          IsWalkableCell(pathData, cy - sampleStep, cx);
                if (adj)
                    _mapDrawList.AddCircleFilled(mapPos, dotRadius, obstacleCol);
            }
        }
    }

    private static uint DistanceHeatmapColor(int dist, int maxDist)
    {
        var t = maxDist > 0 ? Math.Clamp((float)dist / maxDist, 0f, 1f) : 0f;
        byte r, g, b;
        if (t < 0.5f)
        {
            var u = t * 2f;
            r = (byte)(255 * (1f - u));
            g = (byte)(255 * u);
            b = 0;
        }
        else
        {
            var u = (t - 0.5f) * 2f;
            r = 0;
            g = (byte)(255 * (1f - u));
            b = (byte)(255 * u);
        }

        return BeastsV2Helpers.ToImGuiColorU32(new Color(r, g, b, (byte)180));
    }

    private static List<string> SplitExcludedEntityPathsSetting(string excludedPathsSetting)
    {
        if (string.IsNullOrWhiteSpace(excludedPathsSetting)) return [];
        return excludedPathsSetting
            .Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    private static List<string> ParseExcludedPathsForListEditor(string raw)
    {
        if (raw == null || raw.Length == 0) return new List<string>();
        return raw.Replace("\r\n", "\n").Split('\n', StringSplitOptions.None).ToList();
    }

    private void DrawExcludedEntityPathsListPanel()
    {
        var er = Settings.MapRender.ExplorationRoute;
        var paths = ParseExcludedPathsForListEditor(er.ExcludedEntityPaths.Value);

        ImGui.TextDisabled("One row per path. Edits sync to the text field above and refresh the route.");
        if (ImGui.Button("Add path##ExcludedEntityPathsAdd"))
        {
            paths.Add(string.Empty);
            CommitExcludedEntityPaths(paths);
        }

        ImGui.BeginChild("##ExcludedEntityPathsScroll", new Vector2(0, 200), ImGuiChildFlags.Border);
        for (var i = 0; i < paths.Count; i++)
        {
            ImGui.PushID(i);
            var line = paths[i];
            var avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(Math.Max(50f, avail - 76f));
            if (ImGui.InputText("##line", ref line, 2048u))
            {
                paths[i] = line;
                CommitExcludedEntityPaths(paths);
            }

            ImGui.SameLine();
            if (ImGui.Button("Remove##ExcludedEntityPathsRm"))
            {
                paths.RemoveAt(i);
                CommitExcludedEntityPaths(paths);
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private void CommitExcludedEntityPaths(List<string> paths) => ExplorationRouteRefresh.CommitExcludedEntityPaths(paths);

    private void EnsureExplorationRouteIsCurrent() => ExplorationRouteRefresh.EnsureCurrent();

    private void RequestExplorationRouteRegen() => ExplorationRouteRefresh.RequestRegen();
}

