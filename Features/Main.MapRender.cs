using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using GameOffsets.Native;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace BeastsV2;

public partial class Main
{
    private const int TileToGridConversion = 23;
    private const int TileToWorldConversion = 250;
    private const string CaptureMonsterTrappedBuffName = "capture_monster_trapped";
    private const string CaptureMonsterCapturedBuffName = "capture_monster_captured";
    private const string ItemizedCapturedMonsterMetadata = "Metadata/Items/Currency/CurrencyItemisedCapturedMonster";
    private const float GridToWorldMultiplier = TileToWorldConversion / (float)TileToGridConversion;
    private const double CameraAngle = 38.7 * Math.PI / 180;
    private static readonly float CameraAngleCos = (float)Math.Cos(CameraAngle);
    private static readonly float CameraAngleSin = (float)Math.Sin(CameraAngle);
    private static readonly Vector2[] WorldCirclePoints = BeastsV2Helpers.CreateUnitCirclePoints(15, closeLoop: false);

    private double _mapScale { get => MapRenderRuntime.MapScale; set => MapRenderRuntime.MapScale = value; }
    private RectangleF _mapRect { get => MapRenderRuntime.MapRect; set => MapRenderRuntime.MapRect = value; }
    private ImDrawListPtr _mapDrawList { get => MapRenderRuntime.MapDrawList; set => MapRenderRuntime.MapDrawList = value; }
    private Vector2[] _worldCircleScreenPoints => MapRenderRuntime.WorldCircleScreenPoints;

    private CancellationTokenSource _pathFindingCts { get => MapRenderRuntime.PathFindingCts; set => MapRenderRuntime.PathFindingCts = value; }
    private List<Vector2i> _explorationPath { get => MapRenderRuntime.ExplorationPath; set => MapRenderRuntime.ExplorationPath = value; }
    private int _explorationPathForIdx { get => MapRenderRuntime.ExplorationPathForIdx; set => MapRenderRuntime.ExplorationPathForIdx = value; }

    private bool TryGetMapPlayerContext(out Vector2 playerGridPos, out float playerHeight, out float[][] heightData) =>
        MapRenderPathOverlay.TryGetMapPlayerContext(out playerGridPos, out playerHeight, out heightData);

    private void RequestExplorationPath(int waypointIdx, Vector2 gridPos) => MapRenderPathOverlay.RequestExplorationPath(waypointIdx, gridPos);

    internal void CancelBeastPaths() => MapRenderPathOverlay.CancelBeastPaths();

    private void DrawPathsToBeasts(Vector2 mapCenter) => MapRenderPathOverlay.DrawPathsToBeasts(mapCenter);

    private void DrawInWorldBeasts(IReadOnlyList<TrackedBeastRenderInfo> beasts) => MapRenderBeastOverlays.DrawInWorldBeasts(beasts);

    private void DrawBeastsOnLargeMap(IReadOnlyList<TrackedBeastRenderInfo> beasts) => MapRenderLargeMapOverlay.DrawBeastsOnLargeMap(beasts);

    private void DrawBeastMarkersOnMap(Vector2 mapCenter, IReadOnlyList<TrackedBeastRenderInfo> beasts)
    {
        if (!TryGetMapPlayerContext(out var playerGridPos, out var playerHeight, out var heightData))
        {
            return;
        }

        MapRenderBeastOverlays.DrawBeastMarkersOnMap(mapCenter, beasts, playerGridPos, playerHeight, heightData);
    }

    private Vector2 TranslateGridDeltaToMapDelta(Vector2 delta, float deltaZ)
    {
        deltaZ /= GridToWorldMultiplier;
        return (float)_mapScale * new Vector2(
            (delta.X - delta.Y) * CameraAngleCos,
            (deltaZ - (delta.X + delta.Y)) * CameraAngleSin);
    }

    private void DrawMapMarker(string beastName, BeastCaptureState captureState, Vector2 pos) => MapRenderLabels.DrawMapMarker(beastName, captureState, pos);

    private void DrawMapRenderStylePreviewWindow() => MapRenderImGuiOverlays.DrawStylePreviewWindow();

    private void DrawPreviewWorldLabel(string beastName, BeastCaptureState captureState) => MapRenderLabels.DrawPreviewWorldLabel(beastName, captureState);

    private void DrawPreviewMapLabel(string beastName, BeastCaptureState captureState) => MapRenderLabels.DrawPreviewMapLabel(beastName, captureState);

    private void DrawTrackedBeastPreviewRow(string priceText, string beastName, BeastCaptureState captureState) => MapRenderLabels.DrawTrackedBeastPreviewRow(priceText, beastName, captureState);

    private void DrawPreviewCircles() => MapRenderLabels.DrawPreviewCircles();

    private void BuildMarkerTexts(string label, BeastCaptureState captureState, out string primaryText, out string secondaryText) =>
        MapRenderPresentation.BuildMarkerTexts(label, captureState, out primaryText, out secondaryText);

    private void BuildPreviewMapMarkerTexts(string beastName, BeastCaptureState captureState, out string primaryText, out string secondaryText) =>
        MapRenderPresentation.BuildPreviewMapMarkerTexts(beastName, captureState, out primaryText, out secondaryText);

    private void DrawTrackedBeastsWindow(IReadOnlyList<TrackedBeastRenderInfo> beasts) => MapRenderImGuiOverlays.DrawTrackedBeastsWindow(beasts);

    private void DrawInventoryBeasts()
    {
        var inventory = GameController?.Game?.IngameState?.IngameUi?.InventoryPanel?[InventoryIndex.PlayerInventory];
        if (inventory?.IsVisible != true) return;
        DrawCapturedBeastItems(inventory.VisibleInventoryItems);
    }

    private void DrawVisibleStashBeasts(StashElement stash)
    {
        if (stash?.IsVisible != true) return;

        var items = stash.VisibleStash?.VisibleInventoryItems;
        if (items != null)
        {
            DrawCapturedBeastItems(items);
        }
    }

    private void DrawStashBeasts() => DrawVisibleStashBeasts(GameController?.Game?.IngameState?.IngameUi?.StashElement);

    private void DrawMerchantBeasts() => DrawVisibleStashBeasts(GameController?.Game?.IngameState?.IngameUi?.OfflineMerchantPanel);

    private void DrawCapturedBeastItems(IList<NormalInventoryItem> items) =>
        MapRenderPanelOverlays.DrawCapturedBeastItems(items, ItemizedCapturedMonsterMetadata);

    private void DrawBestiaryPanelPrices() => MapRenderPanelOverlays.DrawBestiaryPanelPrices();

    private bool TryGetBestiaryCapturedBeastsDisplay(out Element beastsDisplay, out RectangleF visibleRect) =>
        BestiaryCapturedBeastsView.TryGetCapturedBeastsDisplay(out beastsDisplay, out visibleRect);

    private BeastCaptureState GetBeastCaptureState(Entity entity) => BeastLookup.GetBeastCaptureState(entity);

    private bool TryGetBeastPriceText(string beastName, out string priceText) => BeastLookup.TryGetBeastPriceText(beastName, out priceText);

    private string GetDisplayedCaptureStatusText(BeastCaptureState captureState) => MapRenderPresentation.GetDisplayedCaptureStatusText(captureState);

    private Color GetDisplayedCaptureStatusColor(BeastCaptureState captureState) => MapRenderPresentation.GetDisplayedCaptureStatusColor(captureState);

    private void BuildMapMarkerTexts(string beastName, BeastCaptureState captureState, out string primaryText, out string secondaryText) =>
        MapRenderPresentation.BuildMapMarkerTexts(beastName, captureState, out primaryText, out secondaryText);

    private Color GetWorldBeastColor(BeastCaptureState captureState) => MapRenderPresentation.GetWorldBeastColor(captureState);

    private Color GetWorldBeastCircleColor(BeastCaptureState captureState) => MapRenderPresentation.GetWorldBeastCircleColor(captureState);

    private Color GetTrackedWindowBeastColor() => MapRenderPresentation.GetTrackedWindowBeastColor();

}

