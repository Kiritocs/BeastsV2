using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;

namespace BeastsV2;

public partial class Main
{
    private const string HideoutMapDeviceMetadataMarker = "MappingDevice";

    private async Task CloseMapDeviceBlockingUiAsync()
    {
        await CloseBlockingUiWithSpaceAsync(IsAutomationBlockingUiOpen, "map device automation", MapDeviceCloseUiMaxAttempts, throwOnFailure: true);
    }

    private async Task<bool> EnsureMapDeviceWindowOpenAsync()
    {
        var ui = GameController?.IngameState?.IngameUi;
        if (ui?.Atlas?.IsVisible == true && ui.MapDeviceWindow?.IsVisible == true)
        {
            if (await EnsureConfiguredMapDeviceWindowStateAsync())
            {
                return true;
            }

            ui = GameController?.IngameState?.IngameUi;
            if (ui?.Atlas?.IsVisible == true && IsSpecificMapSelectionConfigured())
            {
                return true;
            }

            return false;
        }

        var timing = AutomationTiming;
        return await EnsureAutomationOpenWithRetryAsync(
            IsMapDeviceWindowOpenReadyForAutomation,
            MapDeviceOpenTimeoutMs,
            timing.StashOpenPollDelayMs,
            async () =>
            {
                ui = GameController?.IngameState?.IngameUi;
                if (ui?.Atlas?.IsVisible == true)
                {
                    if (ui.MapDeviceWindow?.IsVisible == true)
                    {
                        if (await EnsureConfiguredMapDeviceWindowStateAsync())
                        {
                            return AutomationOpenRetryResult.Success;
                        }

                        return AutomationOpenRetryResult.Continue;
                    }

                    if (IsSpecificMapSelectionConfigured())
                    {
                        LogDebug("Atlas is visible without Map Device window; continuing because a specific map is configured.");
                        return AutomationOpenRetryResult.Success;
                    }

                    UpdateAutomationStatus("Atlas opened, but the Map Device window is not visible.");
                    return AutomationOpenRetryResult.Abort;
                }

                if (!await TryOpenMapDeviceFromWorldAsync())
                {
                    return AutomationOpenRetryResult.Continue;
                }

                await WaitForBestiaryConditionAsync(
                    () => GameController?.IngameState?.IngameUi?.Atlas?.IsVisible == true,
                    1000,
                    25);
                return AutomationOpenRetryResult.Continue;
            },
            "Timed out opening the Map Device.");
    }

    private bool IsMapDeviceWindowOpenReadyForAutomation()
    {
        var ui = GameController?.IngameState?.IngameUi;
        if (ui?.Atlas?.IsVisible != true)
        {
            return false;
        }

        if (ui.MapDeviceWindow?.IsVisible == true)
        {
            var selectedMap = GetConfiguredMapSelection();
            return selectedMap.EqualsIgnoreCase(OpenMapSelectionValue) ||
                   IsConfiguredMapAlreadyOpenInMapDeviceWindow(selectedMap);
        }

        return IsSpecificMapSelectionConfigured();
    }

    private async Task<bool> EnsureConfiguredMapDeviceWindowStateAsync()
    {
        var selectedMap = GetConfiguredMapSelection();
        if (selectedMap.EqualsIgnoreCase(OpenMapSelectionValue))
        {
            return true;
        }

        if (IsConfiguredMapAlreadyOpenInMapDeviceWindow(selectedMap))
        {
            return true;
        }

        var titleText = GetMapDeviceWindowTitleText();
        LogDebug($"Map Device window title mismatch for configured map selection. configured='{selectedMap}', title='{titleText ?? "<null>"}'. Closing with Esc.");
        await TapKeyAsync(Keys.Escape, AutomationTiming.KeyTapDelayMs, AutomationTiming.FastPollDelayMs);
        await DelayForUiCheckAsync(150);
        return false;
    }

    private async Task<bool> TryOpenMapDeviceFromWorldAsync()
    {
        var visibleMapDevice = FindNearestAutomationMapDeviceEntity(requireVisible: true);
        var mapDeviceEntity = visibleMapDevice ?? FindNearestAutomationMapDeviceEntity(requireVisible: false);
        if (mapDeviceEntity == null)
        {
            UpdateAutomationStatus("Could not find a Map Device in the current area.", forceLog: true);
            return false;
        }

        var distance = GetPlayerDistanceToEntity(mapDeviceEntity);
        var statusMessage = distance.HasValue && distance.Value <= AutomationTiming.StashInteractionDistance
            ? "Opening Map Device..."
            : "Moving to Map Device...";

        if (await ClickMapDeviceEntityAsync(mapDeviceEntity, statusMessage))
        {
            return true;
        }

        if (!await NavigateTowardsEntityAsync(mapDeviceEntity, HideoutMapDeviceName, "Moving to Map Device..."))
        {
            UpdateAutomationStatus("Could not navigate to the Map Device.", forceLog: true);
            return false;
        }

        return true;
    }

    private Entity FindNearestAutomationMapDeviceEntity(bool requireVisible)
    {
        return FindNearestAutomationEntity(IsAutomationMapDeviceEntity, requireVisible);
    }

    private bool IsAutomationMapDeviceEntity(Entity entity)
    {
        if (entity?.IsValid != true)
        {
            return false;
        }

        if (entity.Metadata.EqualsIgnoreCase(HideoutMapDeviceMetadata))
        {
            return true;
        }

        return (!string.IsNullOrWhiteSpace(entity.Metadata) &&
            entity.Metadata.IndexOf(HideoutMapDeviceMetadataMarker, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (!string.IsNullOrWhiteSpace(entity.Path) &&
            entity.Path.IndexOf(HideoutMapDeviceMetadataMarker, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private async Task SelectConfiguredMapOnAtlasIfNeededAsync(StashAutomationSettings automation)
    {
        var selectedMap = GetConfiguredMapSelection();
        if (selectedMap.EqualsIgnoreCase(OpenMapSelectionValue))
        {
            return;
        }

        if (IsConfiguredMapAlreadyOpenInMapDeviceWindow(selectedMap))
        {
            LogDebug($"Skipping atlas map check: Map Device window already has configured map '{selectedMap}' selected.");
            return;
        }

        var atlas = GameController?.IngameState?.IngameUi?.Atlas;
        if (atlas?.IsVisible != true)
        {
            throw new InvalidOperationException("Atlas is not visible while selecting the configured map.");
        }

        var innerAtlas = TryGetChildAtIndex(atlas, 0);
        if (innerAtlas == null)
        {
            throw new InvalidOperationException("Could not resolve IngameUi.Atlas->Child(0) for map selection.");
        }

        await EnsureInventoryClosedBeforeAtlasMapSearchAsync(automation);

        var mapIndex = await ResolveConfiguredMapIndexAsync(innerAtlas, selectedMap);
        if (!mapIndex.HasValue)
        {
            throw new InvalidOperationException(
                $"Configured map '{selectedMap}' was not found in AtlasNodes. Choose a map from Automation -> Stash & Map Device -> Atlas Map Selection.");
        }

        var selectedMapElement = TryGetChildAtIndex(innerAtlas, mapIndex.Value);
        if (selectedMapElement == null || !TryGetElementIsVisible(selectedMapElement))
        {
            throw new InvalidOperationException($"Configured map '{selectedMap}' (Atlas UI index {mapIndex.Value}) is not visible in IngameUi.Atlas.InnerAtlas.");
        }

        var mapCenter = TryGetElementCenter(selectedMapElement);
        if (!mapCenter.HasValue)
        {
            throw new InvalidOperationException($"Configured map '{selectedMap}' (index {mapIndex.Value}) does not have a clickable center position.");
        }

        UpdateAutomationStatus($"Selecting map: {selectedMap}");

        var selectedWindowOpened = await RetryAutomationAsync(
            async _ =>
            {
                await ClickAtAsync(
                    mapCenter.Value,
                    holdCtrl: false,
                    preClickDelayMs: AutomationTiming.UiClickPreDelayMs,
                    postClickDelayMs: AutomationTiming.CtrlClickPostDelayMs);

                return await WaitForBestiaryConditionAsync(
                    () =>
                    {
                        var currentUi = GameController?.IngameState?.IngameUi;
                        if (currentUi?.MapDeviceWindow?.IsVisible != true)
                        {
                            return false;
                        }

                        return DoesMapDeviceWindowTitleMatchSelectedMap(GetMapDeviceWindowTitleText(), selectedMap);
                    },
                    Math.Max(500, AutomationTiming.OpenStashPostClickDelayMs),
                    Math.Max(10, AutomationTiming.FastPollDelayMs));
            },
            opened => opened,
            maxAttempts: 2,
            retryDelayMs: Math.Max(50, AutomationTiming.FastPollDelayMs));

        if (selectedWindowOpened)
        {
            return;
        }

        var mapDeviceWindowVisible = GameController?.IngameState?.IngameUi?.MapDeviceWindow?.IsVisible == true;
        var observedTitle = GetMapDeviceWindowTitleText();
        if (!mapDeviceWindowVisible)
        {
            throw new InvalidOperationException(
                $"Configured map '{selectedMap}' was clicked on the Atlas but the Map Device window did not appear.");
        }

        throw new InvalidOperationException(
            $"Configured map '{selectedMap}' was clicked, but the Map Device window title did not match. Observed title: '{observedTitle ?? "<null>"}'.");
    }

    private bool IsConfiguredMapAlreadyOpenInMapDeviceWindow(string selectedMap)
    {
        if (string.IsNullOrWhiteSpace(selectedMap) ||
            selectedMap.EqualsIgnoreCase(OpenMapSelectionValue))
        {
            return false;
        }

        return DoesMapDeviceWindowTitleMatchSelectedMap(GetMapDeviceWindowTitleText(), selectedMap);
    }

    private async Task EnsureInventoryClosedBeforeAtlasMapSearchAsync(StashAutomationSettings automation)
    {
        if (!IsPlayerInventoryPanelVisibleForMapSearch())
        {
            return;
        }

        var inventoryToggleKey = automation?.InventoryToggleHotkey?.Value.Key ?? Keys.None;
        if (inventoryToggleKey == Keys.None)
        {
            throw new InvalidOperationException("Inventory is open before map search, but Stash Automation > Inventory Toggle Hotkey is not configured.");
        }

        UpdateAutomationStatus("Closing inventory before map search...");

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await TapKeyAsync(inventoryToggleKey, AutomationTiming.KeyTapDelayMs, AutomationTiming.FastPollDelayMs);
            await DelayForUiCheckAsync(100);
            if (!IsPlayerInventoryPanelVisibleForMapSearch())
            {
                LogDebug($"Closed inventory before atlas map search using key '{inventoryToggleKey}'.");
                return;
            }

            LogDebug($"Inventory is still visible after inventory-toggle key tap. key='{inventoryToggleKey}', attempt={attempt}/2.");
        }

        throw new InvalidOperationException($"Inventory is still open after pressing configured Inventory Toggle Hotkey '{inventoryToggleKey}' before map search.");
    }

    private bool IsPlayerInventoryPanelVisibleForMapSearch()
    {
        var ui = GameController?.IngameState?.IngameUi;
        return ui?.InventoryPanel?.IsVisible == true;
    }

    private async Task<int?> ResolveConfiguredMapIndexAsync(object innerAtlas, string selectedMap)
    {
        await PrepareAtlasForMapCheckAsync(innerAtlas, selectedMap);
        var resolvedIndex = TryResolveConfiguredMapUiIndexFromAtlasNodes(innerAtlas, selectedMap);
        if (resolvedIndex.HasValue)
        {
            LogDebug($"Resolved atlas map index from AtlasNodes. map='{selectedMap}', uiIndex={resolvedIndex.Value}");
        }
        else
        {
            LogDebug($"Configured map was not found in AtlasNodes. map='{selectedMap}'");
        }

        return resolvedIndex;
    }

    private int? TryResolveConfiguredMapUiIndexFromAtlasNodes(object innerAtlas, string selectedMap)
    {
        var lookupName = NormalizeAtlasMapName(selectedMap);
        if (string.IsNullOrWhiteSpace(lookupName))
        {
            return null;
        }

        var nodes = GameController?.Files?.AtlasNodes?.EntriesList;
        if (nodes == null || nodes.Count <= 0)
        {
            return null;
        }

        const int atlasNodeUiOffset = 2;
        var limit = Math.Min(nodes.Count, 110);
        for (var nodeIndex = 0; nodeIndex < limit; nodeIndex++)
        {
            var nodeName = NormalizeAtlasMapName(nodes[nodeIndex].Area?.Name);
            if (string.IsNullOrWhiteSpace(nodeName) || !nodeName.EqualsIgnoreCase(lookupName))
            {
                continue;
            }

            var uiIndex = nodeIndex + atlasNodeUiOffset;
            var childCount = TryGetElementChildCount(innerAtlas);
            if (uiIndex >= childCount)
            {
                throw new InvalidOperationException(
                    $"Configured map '{selectedMap}' resolved to AtlasNodes index {nodeIndex}, but UI index {uiIndex} is out of range (childCount={childCount}).");
            }

            return uiIndex;
        }

        return null;
    }

    private async Task PrepareAtlasForMapCheckAsync(object innerAtlas, string selectedMap)
    {
        await EnsureInnerAtlasScaleBeforeMapScanAsync(innerAtlas, selectedMap);
        await TryCenterInnerAtlasYBeforeMapScanAsync(innerAtlas, selectedMap);
    }

    private async Task EnsureInnerAtlasScaleBeforeMapScanAsync(object innerAtlas, string selectedMap)
    {
        if (innerAtlas == null)
        {
            return;
        }

        var currentScale = TryGetInnerAtlasScale(innerAtlas);
        if (!currentScale.HasValue)
        {
            LogDebug($"Atlas pre-scan scale check skipped for '{selectedMap}': InnerAtlas.Scale is unavailable.");
            return;
        }

        if (IsInnerAtlasScaleTarget(currentScale.Value))
        {
            LogDebug($"Atlas pre-scan scale already at target. InnerAtlas.Scale={currentScale.Value:0.###}.");
            return;
        }

        const int maxScrollAttempts = 18;
        for (var attempt = 1; attempt <= maxScrollAttempts; attempt++)
        {
            var anchor = await TryResolveTooltipFreeAtlasPanStartAsync(innerAtlas, 0);
            if (!anchor.HasValue)
            {
                LogDebug("Atlas pre-scan scale adjustment aborted: could not resolve tooltip-free cursor anchor.");
                return;
            }

            SetAutomationCursorPosition(anchor.Value);
            await DelayAutomationAsync(Math.Max(35, AutomationTiming.FastPollDelayMs));

            ScrollMouseWheelDown();
            await DelayForUiCheckAsync(Math.Max(90, AutomationTiming.FastPollDelayMs + 20));

            currentScale = TryGetInnerAtlasScale(innerAtlas);
            if (!currentScale.HasValue)
            {
                LogDebug("Atlas pre-scan scale adjustment aborted: InnerAtlas.Scale became unavailable during scroll.");
                return;
            }

            if (IsInnerAtlasScaleTarget(currentScale.Value))
            {
                LogDebug($"Atlas pre-scan scale normalized after {attempt} scroll(s). InnerAtlas.Scale={currentScale.Value:0.###}.");
                return;
            }
        }

        if (currentScale.HasValue)
        {
            LogDebug($"Atlas pre-scan scale adjustment ended outside target after retries. InnerAtlas.Scale={currentScale.Value:0.###}, target={AtlasTargetScale:0.###}.");
        }
    }

    private async Task TryCenterInnerAtlasYBeforeMapScanAsync(object innerAtlas, string selectedMap)
    {
        if (innerAtlas == null)
        {
            return;
        }

        var currentY = TryGetInnerAtlasY(innerAtlas);
        if (!currentY.HasValue)
        {
            LogDebug($"Atlas pre-scan centering skipped for '{selectedMap}': InnerAtlas.Y is unavailable.");
            return;
        }

        var currentCenteredY = NormalizeInnerAtlasYForCentering(currentY.Value);
        if (IsInnerAtlasYCentered(currentCenteredY))
        {
            LogDebug($"Atlas pre-scan center already in range. InnerAtlas.Y(raw)={currentY.Value:0.###}, normalized={currentCenteredY:0.##}.");
            return;
        }

        const int maxCenterAttempts = 14;
        int? lastDirection = null;
        var directionFlipCount = 0;

        for (var attempt = 1; attempt <= maxCenterAttempts; attempt++)
        {
            currentY = TryGetInnerAtlasY(innerAtlas);
            if (!currentY.HasValue)
            {
                LogDebug("Atlas pre-scan centering aborted: InnerAtlas.Y became unavailable.");
                return;
            }

            var normalizedCurrentY = NormalizeInnerAtlasYForCentering(currentY.Value);
            if (IsInnerAtlasYCentered(normalizedCurrentY))
            {
                LogDebug($"Atlas pre-scan centered. InnerAtlas.Y(raw)={currentY.Value:0.###}, normalized={normalizedCurrentY:0.##} (target {AtlasCenteredMinY:0}..{AtlasCenteredMaxY:0}).");
                return;
            }

            var distanceToBoundary = normalizedCurrentY > AtlasCenteredMaxY
                ? normalizedCurrentY - AtlasCenteredMaxY
                : AtlasCenteredMinY - normalizedCurrentY;

            // Move toward the nearest boundary instead of midpoint to avoid up/down ping-pong.
            var desiredDirection = normalizedCurrentY > AtlasCenteredMaxY ? -1 : 1;

            var dragPixels = GetAtlasCenteringDragPixels(Math.Abs(distanceToBoundary));
            if (Math.Abs(distanceToBoundary) < 120f)
            {
                dragPixels = Math.Min(dragPixels, 52f);
            }

            if (Math.Abs(distanceToBoundary) < 55f)
            {
                dragPixels = Math.Min(dragPixels, 38f);
            }

            if (lastDirection.HasValue && lastDirection.Value != desiredDirection)
            {
                directionFlipCount++;
                var dampening = directionFlipCount >= 2 ? 0.45f : 0.6f;
                dragPixels = Math.Max(26f, dragPixels * dampening);
                LogDebug($"Atlas pre-scan centering direction flip detected. attempt={attempt}, flips={directionFlipCount}, y={normalizedCurrentY:0.##}, drag={dragPixels:0.#}");
            }

            var verticalDrag = desiredDirection < 0 ? -dragPixels : dragPixels;

            var panned = await TryPanAtlasForCenteringAsync(innerAtlas, verticalDrag, attempt);
            if (!panned)
            {
                LogDebug("Atlas pre-scan centering failed: vertical pan did not execute.");
                return;
            }

            lastDirection = desiredDirection;
        }

        var finalY = TryGetInnerAtlasY(innerAtlas);
        if (finalY.HasValue)
        {
            var normalizedFinalY = NormalizeInnerAtlasYForCentering(finalY.Value);
            LogDebug($"Atlas pre-scan centering ended outside target range after retries. InnerAtlas.Y(raw)={finalY.Value:0.###}, normalized={normalizedFinalY:0.##}, target={AtlasCenteredMinY:0}..{AtlasCenteredMaxY:0}.");
            LogDebug($"InnerAtlas Y diagnostics: {BuildInnerAtlasYDiagnostics(innerAtlas)}");
        }
    }

    private static float GetAtlasCenteringDragPixels(float distanceToTarget)
    {
        if (distanceToTarget > 1600f) return 170f;
        if (distanceToTarget > 900f) return 135f;
        if (distanceToTarget > 450f) return 105f;
        if (distanceToTarget > 220f) return 80f;
        return 60f;
    }

    private static bool IsInnerAtlasYCentered(float innerAtlasY)
    {
        return innerAtlasY >= AtlasCenteredMinY && innerAtlasY <= AtlasCenteredMaxY;
    }

    private static float NormalizeInnerAtlasYForCentering(float innerAtlasY)
    {
        return Math.Abs(innerAtlasY) < 1f ? innerAtlasY * 10000f : innerAtlasY;
    }

    private static bool IsInnerAtlasScaleTarget(float scale)
    {
        return Math.Abs(scale - AtlasTargetScale) <= AtlasTargetScaleTolerance;
    }

    private static float? TryGetInnerAtlasY(object innerAtlas)
    {
        if (innerAtlas == null)
        {
            return null;
        }

        return TryReadNestedNumericMember(innerAtlas, "Position", "Y")
               ?? TryReadNestedNumericMember(innerAtlas, "PositionNum", "Y")
               ?? TryReadNumericMember(innerAtlas, "Y")
               ?? TryReadNumericMember(innerAtlas, "PosY")
               ?? TryReadNumericMember(innerAtlas, "OffsetY")
               ?? TryReadNumericMember(innerAtlas, "TranslateY")
               ?? TryReadNumericMember(innerAtlas, "TranslationY");
    }

    private static string BuildInnerAtlasYDiagnostics(object innerAtlas)
    {
        if (innerAtlas == null)
        {
            return "innerAtlas=<null>";
        }

        var names = new[] { "Y", "PosY", "OffsetY", "TranslateY", "TranslationY" };
        var values = names
            .Select(name => (Name: name, Value: TryReadNumericMember(innerAtlas, name)))
            .Select(x => $"{x.Name}={(x.Value.HasValue ? x.Value.Value.ToString("0.###") : "<null>")}");
        var positionY = TryReadNestedNumericMember(innerAtlas, "Position", "Y");
        var positionNumY = TryReadNestedNumericMember(innerAtlas, "PositionNum", "Y");
        var rawY = TryGetInnerAtlasY(innerAtlas);
        var normalizedY = rawY.HasValue ? NormalizeInnerAtlasYForCentering(rawY.Value) : (float?)null;

        return string.Join(", ", values)
               + $", Position.Y={(positionY.HasValue ? positionY.Value.ToString("0.###") : "<null>")}"
               + $", PositionNum.Y={(positionNumY.HasValue ? positionNumY.Value.ToString("0.###") : "<null>")}"
               + $", NormalizedYForCentering={(normalizedY.HasValue ? normalizedY.Value.ToString("0.##") : "<null>")}";
    }

    private static float? TryGetInnerAtlasScale(object innerAtlas)
    {
        if (innerAtlas == null)
        {
            return null;
        }

        var directScale = TryReadNumericMember(innerAtlas, "Scale");
        if (directScale.HasValue)
        {
            return directScale.Value;
        }

        try
        {
            var innerAtlasType = innerAtlas.GetType();
            var scaleObject = innerAtlasType.GetProperty("Scale")?.GetValue(innerAtlas)
                              ?? innerAtlasType.GetField("Scale")?.GetValue(innerAtlas);
            if (scaleObject == null)
            {
                return null;
            }

            return TryReadNumericMember(scaleObject, "Value")
                   ?? TryReadNumericMember(scaleObject, "Current")
                   ?? TryReadNumericMember(scaleObject, "X");
        }
        catch
        {
            return null;
        }
    }

    private static void ScrollMouseWheelDown(int clicks = 1)
    {
        var wheelAmount = Math.Max(1, clicks) * MouseWheelDelta;
        mouse_event(MouseEventWheel, 0, 0, -wheelAmount, UIntPtr.Zero);
    }

    private async Task<bool> TryPanAtlasForCenteringAsync(object innerAtlas, float verticalDragPixels, int attempt)
    {
        if (innerAtlas == null)
        {
            return false;
        }

        if (Math.Abs(verticalDragPixels) < 20f)
        {
            return false;
        }

        var passHint = verticalDragPixels < 0 ? 0 : 1;
        var tooltipFreeStart = await TryResolveTooltipFreeAtlasPanStartAsync(innerAtlas, passHint);
        if (!tooltipFreeStart.HasValue)
        {
            LogDebug("Atlas centering pan failed: could not find a tooltip-free drag start.");
            return false;
        }

        var start = ClampCursorPositionToGameWindow(tooltipFreeStart.Value);
        var end = ClampCursorPositionToGameWindow(start + new SharpDX.Vector2(0f, verticalDragPixels));
        if (SharpDX.Vector2.DistanceSquared(start, end) < 225f)
        {
            return false;
        }

        await DragLeftCursorAsync(
            start,
            end,
            Math.Max(25, AutomationTiming.FastPollDelayMs),
            Math.Max(20, AutomationTiming.KeyTapDelayMs / 2),
            Math.Max(120, AutomationTiming.UiCheckInitialSettleDelayMs),
            Math.Max(90, AutomationTiming.FastPollDelayMs));

        LogDebug($"Panned atlas for centering. attempt={attempt}, from=({start.X:0},{start.Y:0}) to=({end.X:0},{end.Y:0})");
        return true;
    }

    private async Task<SharpDX.Vector2?> TryResolveTooltipFreeAtlasPanStartAsync(object innerAtlas, int passIndex)
    {
        var windowRect = GameController?.Window?.GetWindowRectangle();
        if (!windowRect.HasValue || windowRect.Value.Width <= 0 || windowRect.Value.Height <= 0)
        {
            return null;
        }

        var wr = windowRect.Value;
        var screenCenter = new SharpDX.Vector2(wr.Left + wr.Width * 0.5f, wr.Top + wr.Height * 0.5f);
        var centerProbe = await TryFindTooltipFreePointNearAsync(screenCenter, wr);
        if (centerProbe.HasValue)
        {
            return centerProbe;
        }

        var fallbackAnchor = TryResolveAtlasPanAnchor(innerAtlas, passIndex);
        if (!fallbackAnchor.HasValue)
        {
            LogDebug("Atlas pan start probe failed: screen center and fallback anchor were not available.");
            return null;
        }

        var fallbackProbe = await TryFindTooltipFreePointNearAsync(fallbackAnchor.Value, wr);
        if (!fallbackProbe.HasValue)
        {
            LogDebug("Atlas pan start probe failed: no tooltip-free point was found near center or fallback anchor.");
        }

        return fallbackProbe;
    }

    private async Task<SharpDX.Vector2?> TryFindTooltipFreePointNearAsync(SharpDX.Vector2 origin, SharpDX.RectangleF windowRect)
    {
        var normalizedOrigin = ClampCursorPositionToGameWindow(AdjustAtlasPanAnchorToSafeZone(origin));
        var step = Math.Clamp(Math.Min(windowRect.Width, windowRect.Height) * 0.035f, 26f, 64f);
        var probeOffsets = new[]
        {
            new SharpDX.Vector2(0f, 0f),
            new SharpDX.Vector2(step, 0f),
            new SharpDX.Vector2(-step, 0f),
            new SharpDX.Vector2(0f, step),
            new SharpDX.Vector2(0f, -step),
            new SharpDX.Vector2(step, step),
            new SharpDX.Vector2(-step, step),
            new SharpDX.Vector2(step, -step),
            new SharpDX.Vector2(-step, -step),
            new SharpDX.Vector2(step * 2f, 0f),
            new SharpDX.Vector2(-step * 2f, 0f),
            new SharpDX.Vector2(0f, step * 2f),
            new SharpDX.Vector2(0f, -step * 2f),
        };

        for (var i = 0; i < probeOffsets.Length; i++)
        {
            var probe = ClampCursorPositionToGameWindow(AdjustAtlasPanAnchorToSafeZone(normalizedOrigin + probeOffsets[i]));
            if (!IsAtlasHoverPositionUsable(probe))
            {
                continue;
            }

            SetAutomationCursorPosition(probe);
            await DelayAutomationAsync(Math.Max(40, AutomationTiming.FastPollDelayMs));

            if (TryGetCurrentUiHoverTooltip() == null)
            {
                if (i > 0)
                {
                    LogDebug($"Atlas pan start shifted by probe offset index {i} to avoid tooltip overlap.");
                }

                return probe;
            }
        }

        return null;
    }

    private SharpDX.Vector2? TryResolveAtlasPanAnchor(object innerAtlas, int passIndex)
    {
        if (innerAtlas != null && TryGetElementRect(innerAtlas, out var left, out var top, out var right, out var bottom))
        {
            var anchors = new[]
            {
                new SharpDX.Vector2(left + (right - left) * 0.36f, top + (bottom - top) * 0.40f),
                new SharpDX.Vector2(left + (right - left) * 0.64f, top + (bottom - top) * 0.40f),
                new SharpDX.Vector2(left + (right - left) * 0.36f, top + (bottom - top) * 0.62f),
                new SharpDX.Vector2(left + (right - left) * 0.64f, top + (bottom - top) * 0.62f),
                new SharpDX.Vector2(left + (right - left) * 0.50f, top + (bottom - top) * 0.48f),
            };

            var selected = anchors[Math.Abs(passIndex) % anchors.Length];
            return AdjustAtlasPanAnchorToSafeZone(selected);
        }

        var center = TryGetElementCenter(innerAtlas);
        if (center.HasValue)
        {
            return AdjustAtlasPanAnchorToSafeZone(center.Value);
        }

        var windowRect = GameController?.Window?.GetWindowRectangle();
        if (!windowRect.HasValue || windowRect.Value.Width <= 0 || windowRect.Value.Height <= 0)
        {
            return null;
        }

        var wr = windowRect.Value;
        return new SharpDX.Vector2(wr.Left + wr.Width * 0.5f, wr.Top + wr.Height * 0.5f);
    }

    private SharpDX.Vector2 AdjustAtlasPanAnchorToSafeZone(SharpDX.Vector2 anchor)
    {
        var windowRect = GameController?.Window?.GetWindowRectangle();
        if (!windowRect.HasValue || windowRect.Value.Width <= 0 || windowRect.Value.Height <= 0)
        {
            return anchor;
        }

        var rect = windowRect.Value;
        var normalizedX = (anchor.X - rect.Left) / rect.Width;
        var normalizedY = (anchor.Y - rect.Top) / rect.Height;
        normalizedX = Math.Clamp(normalizedX, 0.16f, 0.84f);
        normalizedY = Math.Clamp(normalizedY, 0.18f, 0.76f);

        return new SharpDX.Vector2(
            rect.Left + normalizedX * rect.Width,
            rect.Top + normalizedY * rect.Height);
    }

    private bool IsAtlasHoverPositionUsable(SharpDX.Vector2 position)
    {
        return IsPointInsideGameWindow(position) && !IsPointInAtlasHudBlockedZone(position);
    }

    private bool IsPointInsideGameWindow(SharpDX.Vector2 position)
    {
        var windowRect = GameController?.Window?.GetWindowRectangle();
        if (!windowRect.HasValue || windowRect.Value.Width <= 0 || windowRect.Value.Height <= 0)
        {
            return false;
        }

        var rect = windowRect.Value;
        return position.X >= rect.Left && position.X <= rect.Right &&
               position.Y >= rect.Top && position.Y <= rect.Bottom;
    }

    private bool IsPointInAtlasHudBlockedZone(SharpDX.Vector2 position)
    {
        var windowRect = GameController?.Window?.GetWindowRectangle();
        if (!windowRect.HasValue || windowRect.Value.Width <= 0 || windowRect.Value.Height <= 0)
        {
            return false;
        }

        var rect = windowRect.Value;
        var normalizedX = (position.X - rect.Left) / rect.Width;
        var normalizedY = (position.Y - rect.Top) / rect.Height;

        return IsPointInAtlasHudBlockedZoneNormalized(normalizedX, normalizedY);
    }

    private static bool IsPointInAtlasHudBlockedZoneNormalized(float normalizedX, float normalizedY)
    {
        return IsPointInNormalizedRect(normalizedX, normalizedY, AtlasHudTopLeftMinX, AtlasHudTopLeftMinY, AtlasHudTopLeftMaxX, AtlasHudTopLeftMaxY) ||
               IsPointInNormalizedRect(normalizedX, normalizedY, AtlasHudTopCenterMinX, AtlasHudTopCenterMinY, AtlasHudTopCenterMaxX, AtlasHudTopCenterMaxY) ||
               IsPointInNormalizedRect(normalizedX, normalizedY, AtlasHudBottomLeftGlobeMinX, AtlasHudBottomLeftGlobeMinY, AtlasHudBottomLeftGlobeMaxX, AtlasHudBottomLeftGlobeMaxY) ||
               IsPointInNormalizedRect(normalizedX, normalizedY, AtlasHudBottomRightGlobeMinX, AtlasHudBottomRightGlobeMinY, AtlasHudBottomRightGlobeMaxX, AtlasHudBottomRightGlobeMaxY) ||
               IsPointInNormalizedRect(normalizedX, normalizedY, AtlasHudBottomLeftBarMinX, AtlasHudBottomLeftBarMinY, AtlasHudBottomLeftBarMaxX, AtlasHudBottomLeftBarMaxY) ||
               IsPointInNormalizedRect(normalizedX, normalizedY, AtlasHudBottomRightBarMinX, AtlasHudBottomRightBarMinY, AtlasHudBottomRightBarMaxX, AtlasHudBottomRightBarMaxY);
    }

    private static bool IsPointInNormalizedRect(float x, float y, float minX, float minY, float maxX, float maxY)
    {
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private object TryGetCurrentUiHoverTooltip()
    {
        var ingameState = GameController?.IngameState;
        var uiHover = TryGetPropertyValue<object>(ingameState, "UIHover");
        return TryGetPropertyValue<object>(uiHover, "Tooltip");
    }

    private bool IsSpecificMapSelectionConfigured()
    {
        var selectedMap = GetConfiguredMapSelection();
        return !selectedMap.EqualsIgnoreCase(OpenMapSelectionValue);
    }

    private string GetConfiguredMapSelection()
    {
        var configuredValue = Settings?.StashAutomation?.SelectedMapToRun?.Value;
        return NormalizeMapSelectionValue(configuredValue);
    }

    private static string NormalizeMapSelectionValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OpenMapSelectionValue;
        }

        var trimmed = value.Trim();
        if (trimmed.EqualsIgnoreCase(OpenMapSelectionValue) ||
            trimmed.EqualsIgnoreCase("open Map (Default)"))
        {
            return OpenMapSelectionValue;
        }

        var normalized = NormalizeAtlasMapName(trimmed);
        return string.IsNullOrWhiteSpace(normalized) ? OpenMapSelectionValue : normalized;
    }

    private static string NormalizeAtlasMapName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().TrimStart('\u2605', ' ', '\u00a0').Trim();
    }

    private string GetMapDeviceWindowTitleText()
    {
        var mapDeviceWindow = GameController?.IngameState?.IngameUi?.MapDeviceWindow;
        if (mapDeviceWindow?.IsVisible != true)
        {
            return null;
        }

        var titleElement = TryGetPropertyValue<object>(mapDeviceWindow, "Title");
        if (titleElement == null)
        {
            return null;
        }

        var titleText = TryGetAutomationElementText(titleElement);

        return string.IsNullOrWhiteSpace(titleText) ? null : titleText;
    }

    private static bool DoesMapDeviceWindowTitleMatchSelectedMap(string titleText, string selectedMap)
    {
        if (string.IsNullOrWhiteSpace(titleText) || string.IsNullOrWhiteSpace(selectedMap))
        {
            return false;
        }

        return titleText.IndexOf(selectedMap, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static object TryGetChildAtIndex(object parent, int index)
    {
        if (parent == null || index < 0)
        {
            return null;
        }

        try
        {
            return parent.GetType().GetMethod("GetChildAtIndex")?.Invoke(parent, [index]);
        }
        catch
        {
            return null;
        }
    }

    private static int TryGetElementChildCount(object element)
    {
        if (element == null)
        {
            return 0;
        }

        try
        {
            var childCountValue = element.GetType().GetProperty("ChildCount")?.GetValue(element);
            if (childCountValue is int childCount)
            {
                return childCount;
            }

            if (element.GetType().GetProperty("Children")?.GetValue(element) is ICollection collection)
            {
                return collection.Count;
            }
        }
        catch
        {
        }

        return 0;
    }

    private static bool TryGetElementIsVisible(object element)
    {
        try
        {
            if (element == null)
            {
                return false;
            }

            return element.GetType().GetProperty("IsVisible")?.GetValue(element) is bool isVisible && isVisible;
        }
        catch
        {
            return false;
        }
    }

    private static SharpDX.Vector2? TryGetElementCenter(object element)
    {
        if (element == null)
        {
            return null;
        }

        try
        {
            var rect = element.GetType().GetMethod("GetClientRect")?.Invoke(element, null);
            var center = rect?.GetType().GetProperty("Center")?.GetValue(rect);
            if (center is SharpDX.Vector2 sharpDxCenter)
            {
                return sharpDxCenter;
            }

            if (center == null)
            {
                return null;
            }

            var xValue = center.GetType().GetProperty("X")?.GetValue(center);
            var yValue = center.GetType().GetProperty("Y")?.GetValue(center);
            if (xValue == null || yValue == null)
            {
                return null;
            }

            return new SharpDX.Vector2(Convert.ToSingle(xValue), Convert.ToSingle(yValue));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetElementRect(object element, out float left, out float top, out float right, out float bottom)
    {
        left = 0;
        top = 0;
        right = 0;
        bottom = 0;
        if (element == null)
        {
            return false;
        }

        try
        {
            var rect = element.GetType().GetMethod("GetClientRect")?.Invoke(element, null);
            if (rect == null)
            {
                return false;
            }

            var leftValue = TryReadNumericMember(rect, "Left");
            var topValue = TryReadNumericMember(rect, "Top");
            var rightValue = TryReadNumericMember(rect, "Right");
            var bottomValue = TryReadNumericMember(rect, "Bottom");
            if (leftValue.HasValue && topValue.HasValue && rightValue.HasValue && bottomValue.HasValue)
            {
                left = leftValue.Value;
                top = topValue.Value;
                right = rightValue.Value;
                bottom = bottomValue.Value;
                return right > left && bottom > top;
            }

            var xValue = TryReadNumericMember(rect, "X");
            var yValue = TryReadNumericMember(rect, "Y");
            var widthValue = TryReadNumericMember(rect, "Width");
            var heightValue = TryReadNumericMember(rect, "Height");
            if (!xValue.HasValue || !yValue.HasValue || !widthValue.HasValue || !heightValue.HasValue)
            {
                return false;
            }

            left = xValue.Value;
            top = yValue.Value;
            right = xValue.Value + widthValue.Value;
            bottom = yValue.Value + heightValue.Value;
            return right > left && bottom > top;
        }
        catch
        {
            return false;
        }
    }

    private static float? TryReadNumericMember(object instance, string memberName)
    {
        if (instance == null || string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        try
        {
            var type = instance.GetType();
            var propertyValue = type.GetProperty(memberName)?.GetValue(instance);
            if (propertyValue != null)
            {
                return Convert.ToSingle(propertyValue);
            }

            var fieldValue = type.GetField(memberName)?.GetValue(instance);
            if (fieldValue != null)
            {
                return Convert.ToSingle(fieldValue);
            }
        }
        catch
        {
        }

        return null;
    }

    private static float? TryReadNestedNumericMember(object instance, string parentMemberName, string childMemberName)
    {
        if (instance == null || string.IsNullOrWhiteSpace(parentMemberName) || string.IsNullOrWhiteSpace(childMemberName))
        {
            return null;
        }

        try
        {
            var type = instance.GetType();
            var parent = type.GetProperty(parentMemberName)?.GetValue(instance)
                         ?? type.GetField(parentMemberName)?.GetValue(instance);
            return parent == null ? null : TryReadNumericMember(parent, childMemberName);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ClickMapDeviceEntityAsync(Entity mapDeviceEntity, string statusMessage)
    {
        var timing = AutomationTiming;
        return await TryInteractWithWorldEntityAsync(
            mapDeviceEntity,
            "Map Device",
            statusMessage,
            "Could not find a clickable Map Device position.",
            "Could not hover the Map Device.",
            MouseButtons.Left,
            timing.OpenStashPostClickDelayMs);
    }

}
