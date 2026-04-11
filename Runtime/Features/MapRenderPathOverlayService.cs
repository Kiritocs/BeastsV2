using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using GameOffsets.Native;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2.Runtime.Features;

internal readonly record struct MapRenderPlayerContext(Vector2 PlayerGridPos, float PlayerHeight, float[][] HeightData);

internal sealed record MapRenderPathOverlayCallbacks(
    Func<Entity> GetLocalPlayer,
    Func<float[][]> GetRawTerrainHeightData,
    Func<Func<Vector2, Action<List<Vector2i>>, CancellationToken, Task>> ResolveLookForRoute,
    Func<CancellationTokenSource> GetPathFindingCts,
    Action<CancellationTokenSource> SetPathFindingCts,
    Func<List<Vector2i>> GetExplorationPath,
    Action<List<Vector2i>> SetExplorationPath,
    Func<int> GetExplorationPathForIndex,
    Action<int> SetExplorationPathForIndex,
    Func<bool> GetExplorationRouteEnabled,
    Func<IReadOnlyList<Vector2>> GetExplorationRoute,
    Func<IReadOnlySet<int>> GetVisitedWaypointIndices,
    Action<Vector2> UpdateVisitedWaypoints,
    Func<int> GetNextWaypointIndex,
    Action EnsureExplorationRouteIsCurrent,
    Func<ImDrawListPtr> GetMapDrawList,
    Func<Vector2, float, Vector2> TranslateGridDeltaToMapDelta);

internal sealed class MapRenderPathOverlayService
{
    private readonly MapRenderPathOverlayCallbacks _callbacks;

    public MapRenderPathOverlayService(MapRenderPathOverlayCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public bool TryGetMapPlayerContext(out Vector2 playerGridPos, out float playerHeight, out float[][] heightData)
    {
        var player = _callbacks.GetLocalPlayer();
        var playerPositioned = player?.GetComponent<Positioned>();
        var playerRender = player?.GetComponent<Render>();
        if (playerPositioned == null || playerRender == null)
        {
            playerGridPos = default;
            playerHeight = 0;
            heightData = null;
            return false;
        }

        playerGridPos = new Vector2(playerPositioned.GridPosNum.X, playerPositioned.GridPosNum.Y);
        playerHeight = -playerRender.RenderStruct.Height;
        heightData = _callbacks.GetRawTerrainHeightData();
        return heightData != null;
    }

    public void RequestExplorationPath(int waypointIdx, Vector2 gridPos)
    {
        var lookForRoute = _callbacks.ResolveLookForRoute();
        if (lookForRoute == null)
        {
            return;
        }

        var token = _callbacks.GetPathFindingCts().Token;
        _ = lookForRoute(gridPos, path =>
        {
            if (path != null && !token.IsCancellationRequested && _callbacks.GetExplorationPathForIndex() == waypointIdx)
            {
                _callbacks.SetExplorationPath(path);
            }
        }, token);
    }

    public void CancelBeastPaths()
    {
        _callbacks.GetPathFindingCts().Cancel();
        _callbacks.SetPathFindingCts(new CancellationTokenSource());
        _callbacks.SetExplorationPath(null);
        _callbacks.SetExplorationPathForIndex(-1);
    }

    public void DrawPathsToBeasts(Vector2 mapCenter)
    {
        if (!_callbacks.GetExplorationRouteEnabled())
        {
            return;
        }

        _callbacks.EnsureExplorationRouteIsCurrent();
        var route = _callbacks.GetExplorationRoute();
        if (route.Count == 0 || !TryGetMapPlayerContext(out var playerGridPos, out var playerHeight, out var heightData))
        {
            return;
        }

        _callbacks.UpdateVisitedWaypoints(playerGridPos);
        var nextIdx = _callbacks.GetNextWaypointIndex();
        if (nextIdx < 0)
        {
            return;
        }

        if (nextIdx != _callbacks.GetExplorationPathForIndex())
        {
            _callbacks.SetExplorationPathForIndex(nextIdx);
            _callbacks.SetExplorationPath(null);
            RequestExplorationPath(nextIdx, route[nextIdx]);
        }

        var mapDrawList = _callbacks.GetMapDrawList();
        var routeColor = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.2f, 0.8f, 1f, 0.45f));
        var nextCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(1f, 0.65f, 0f, 1f));
        const float waypointRadius = 2f;
        const float nextWaypointRadius = 5f;

        var visitedWaypointIndices = _callbacks.GetVisitedWaypointIndices();
        for (var i = 0; i < route.Count; i++)
        {
            if (visitedWaypointIndices.Contains(i))
            {
                continue;
            }

            var waypointPos = mapCenter + _callbacks.TranslateGridDeltaToMapDelta(route[i] - playerGridPos, 0);
            mapDrawList.AddCircleFilled(waypointPos, i == nextIdx ? nextWaypointRadius : waypointRadius, i == nextIdx ? nextCol : routeColor);
        }

        var path = _callbacks.GetExplorationPath();
        if (path == null)
        {
            return;
        }

        var pathCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(1f, 0.65f, 0f, 0.85f));
        const float pathThickness = 2f;

        Vector2? prev = null;
        var skip = 0;
        foreach (var node in path)
        {
            if (++skip % 2 != 0)
            {
                continue;
            }

            BeastsV2Helpers.TryGetTerrainHeight(heightData, node.X, node.Y, out var nodeHeight);
            var pos = mapCenter + _callbacks.TranslateGridDeltaToMapDelta(new Vector2(node.X, node.Y) - playerGridPos, playerHeight + nodeHeight);
            if (prev.HasValue)
            {
                mapDrawList.AddLine(prev.Value, pos, pathCol, pathThickness);
            }

            prev = pos;
        }
    }
}