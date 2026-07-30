using System;
using System.Collections.Generic;
using ImGuiNET;
using SharpDX;

namespace BeastsV2.Runtime.Features;

internal sealed record MapRenderImGuiOverlayCallbacks(
    Action<string, BeastCaptureState, bool> DrawPreviewWorldLabel,
    Action<string, BeastCaptureState, bool> DrawPreviewMapLabel,
    Action<string, string, BeastCaptureState, bool> DrawTrackedBeastPreviewRow,
    Action DrawPreviewCircles,
    Func<bool, Color> GetTrackedWindowBeastColor,
    Func<string, string> GetBeastPriceTextOrNull,
    Func<string, bool> IsTalismanOnlyBeast,
    Func<BeastCaptureState, Color> GetDisplayedCaptureStatusColor,
    Func<BeastCaptureState, string> GetDisplayedCaptureStatusText);

internal sealed class MapRenderImGuiOverlayService
{
    private const string PreviewBeastName = "Craicic Croaker";
    private const string TalismanOnlyRowHint = "Second row shows the talisman-only styling.";
    private readonly MapRenderImGuiOverlayCallbacks _callbacks;

    public MapRenderImGuiOverlayService(MapRenderImGuiOverlayCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void DrawStylePreviewWindow()
    {
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin("Beast Style Preview##BeastsV2StylePreview",
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("World Label Preview");
        ImGui.TextDisabled(TalismanOnlyRowHint);
        _callbacks.DrawPreviewWorldLabel(PreviewBeastName, BeastCaptureState.None, false);
        _callbacks.DrawPreviewWorldLabel(PreviewBeastName, BeastCaptureState.None, true);
        _callbacks.DrawPreviewWorldLabel(PreviewBeastName, BeastCaptureState.Capturing, false);
        _callbacks.DrawPreviewWorldLabel(PreviewBeastName, BeastCaptureState.Captured, false);

        ImGui.Separator();
        ImGui.Text("Large Map Label Preview");
        ImGui.TextDisabled(TalismanOnlyRowHint);
        _callbacks.DrawPreviewMapLabel(PreviewBeastName, BeastCaptureState.None, false);
        _callbacks.DrawPreviewMapLabel(PreviewBeastName, BeastCaptureState.None, true);
        _callbacks.DrawPreviewMapLabel(PreviewBeastName, BeastCaptureState.Capturing, false);
        _callbacks.DrawPreviewMapLabel(PreviewBeastName, BeastCaptureState.Captured, false);

        ImGui.Separator();
        ImGui.Text("Tracked Beasts Window Preview");
        ImGui.TextDisabled(TalismanOnlyRowHint);
        if (ImGui.BeginTable("##TrackedWindowPreviewTable", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV))
        {
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 52);
            ImGui.TableSetupColumn("Beast", ImGuiTableColumnFlags.WidthStretch);

            _callbacks.DrawTrackedBeastPreviewRow("1c", PreviewBeastName, BeastCaptureState.None, false);
            _callbacks.DrawTrackedBeastPreviewRow("1c +1c", PreviewBeastName, BeastCaptureState.None, true);
            _callbacks.DrawTrackedBeastPreviewRow("1c", PreviewBeastName, BeastCaptureState.Capturing, false);
            _callbacks.DrawTrackedBeastPreviewRow("1c", PreviewBeastName, BeastCaptureState.Captured, false);

            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.Text("Circle Preview");
        _callbacks.DrawPreviewCircles();

        ImGui.End();
    }

    public void DrawTrackedBeastsWindow(IReadOnlyList<TrackedBeastMapMarkerInfo> beasts)
    {
        if (beasts.Count == 0)
        {
            return;
        }

        var trackedWindowBeastColor = BeastsV2Helpers.ToImGuiColor(_callbacks.GetTrackedWindowBeastColor(false));
        var talismanOnlyBeastColor = BeastsV2Helpers.ToImGuiColor(_callbacks.GetTrackedWindowBeastColor(true));

        ImGui.SetNextWindowBgAlpha(0.6f);
        ImGui.Begin("##RareBeastTrackerWindow", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize);

        if (ImGui.BeginTable("##TrackerTable", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV))
        {
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 52);
            ImGui.TableSetupColumn("Beast", ImGuiTableColumnFlags.WidthStretch);

            foreach (var beast in beasts)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(_callbacks.GetBeastPriceTextOrNull(beast.BeastName) ?? "?");
                ImGui.TableNextColumn();
                ImGui.TextColored(
                    _callbacks.IsTalismanOnlyBeast(beast.BeastName) ? talismanOnlyBeastColor : trackedWindowBeastColor,
                    beast.BeastName);
                if (beast.CaptureState != BeastCaptureState.None)
                {
                    ImGui.SameLine(0, 0);
                    ImGui.TextColored(BeastsV2Helpers.ToImGuiColor(_callbacks.GetDisplayedCaptureStatusColor(beast.CaptureState)),
                        $" {_callbacks.GetDisplayedCaptureStatusText(beast.CaptureState)}");
                }
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }
}