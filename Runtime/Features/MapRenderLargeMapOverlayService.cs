using System;
using System.Collections.Generic;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2.Runtime.Features;

internal readonly record struct MapRenderLargeMapState(bool IsVisible, float MapScale, Vector2 MapCenter);

internal sealed record MapRenderLargeMapOverlayCallbacks(
    Func<RectangleF> GetBaseWindowRect,
    Func<bool> IsOpenRightPanelVisible,
    Func<float> GetOpenRightPanelLeft,
    Func<bool> IsOpenLeftPanelVisible,
    Func<float> GetOpenLeftPanelRight,
    Func<MapRenderLargeMapState> GetLargeMapState,
    Action<RectangleF> SetMapRect,
    Action<double> SetMapScale,
    Action<ImDrawListPtr> SetMapDrawList,
    Func<bool> GetShowBeastsOnMap,
    Func<bool> GetShowPathsToBeasts,
    Func<bool> GetShowExplorationRoute,
    Action<Vector2, IReadOnlyList<TrackedBeastMapMarkerInfo>> DrawBeastMarkersOnMap,
    Action<Vector2> DrawPathsToBeasts,
    Action<Vector2> DrawExplorationRouteOnMap,
    Action<Vector2> DrawEntityExclusionZones,
    Action<Vector2> DrawExplorationDebugOnMap);

internal sealed class MapRenderLargeMapOverlayService
{
    private readonly MapRenderLargeMapOverlayCallbacks _callbacks;

    public MapRenderLargeMapOverlayService(MapRenderLargeMapOverlayCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void DrawBeastsOnLargeMap(IReadOnlyList<TrackedBeastMapMarkerInfo> beasts)
    {
        var mapRect = _callbacks.GetBaseWindowRect();

        if (_callbacks.IsOpenRightPanelVisible())
        {
            mapRect.Right = _callbacks.GetOpenRightPanelLeft();
        }

        if (_callbacks.IsOpenLeftPanelVisible())
        {
            mapRect.Left = _callbacks.GetOpenLeftPanelRight();
        }

        _callbacks.SetMapRect(mapRect);

        ImGui.SetNextWindowSize(new Vector2(mapRect.Width, mapRect.Height));
        ImGui.SetNextWindowPos(new Vector2(mapRect.Left, mapRect.Top));

        ImGui.Begin("##RareBeastMapOverlay",
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoBackground);

        _callbacks.SetMapDrawList(ImGui.GetWindowDrawList());

        var largeMap = _callbacks.GetLargeMapState();
        if (largeMap.IsVisible)
        {
            _callbacks.SetMapScale(largeMap.MapScale);
            if (_callbacks.GetShowBeastsOnMap())
            {
                _callbacks.DrawBeastMarkersOnMap(largeMap.MapCenter, beasts);
            }

            if (_callbacks.GetShowPathsToBeasts())
            {
                _callbacks.DrawPathsToBeasts(largeMap.MapCenter);
            }

            if (_callbacks.GetShowExplorationRoute())
            {
                _callbacks.DrawExplorationRouteOnMap(largeMap.MapCenter);
            }

            _callbacks.DrawEntityExclusionZones(largeMap.MapCenter);
            _callbacks.DrawExplorationDebugOnMap(largeMap.MapCenter);
        }

        ImGui.End();
    }
}