using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using SharpDX;
using GridVector2 = System.Numerics.Vector2;

namespace BeastsV2;

public partial class Main
{
    private const int AutomationNavigationNodeSize = 18;
    private const int AutomationNavigationLookAheadIndex = 4;
    private const float AutomationNavigationMinMoveDistance = 12f;
    private const float AutomationNavigationClampRadius = 400f;

    private Entity FindNearestAutomationEntity(Func<Entity, bool> predicate, bool requireVisible)
    {
        var entities = GameController?.EntityListWrapper?.Entities;
        var playerPositioned = GameController?.Game?.IngameState?.Data?.LocalPlayer?.GetComponent<Positioned>();
        var camera = GameController?.Game?.IngameState?.Camera;
        var window = GameController?.Window;
        if (entities == null || playerPositioned == null)
        {
            return null;
        }

        var windowRect = window?.GetWindowRectangle();
        var playerGridPos = playerPositioned.GridPosNum;
        Entity nearestEntity = null;
        var nearestDistanceSquared = float.MaxValue;

        foreach (var entity in entities)
        {
            if (entity?.IsValid != true || !predicate(entity))
            {
                continue;
            }

            var positioned = entity.GetComponent<Positioned>();
            var render = entity.GetComponent<Render>();
            if (positioned == null || render == null)
            {
                continue;
            }

            if (requireVisible)
            {
                if (camera == null || windowRect == null)
                {
                    continue;
                }

                if (!IsScreenPositionVisible(camera.WorldToScreen(render.PosNum), windowRect.Value.Width, windowRect.Value.Height))
                {
                    continue;
                }
            }

            var distanceSquared = GridVector2.DistanceSquared(playerGridPos, positioned.GridPosNum);
            if (distanceSquared >= nearestDistanceSquared)
            {
                continue;
            }

            nearestDistanceSquared = distanceSquared;
            nearestEntity = entity;
        }

        return nearestEntity;
    }

    private async Task<bool> NavigateTowardsEntityAsync(Entity entity, string label, string statusMessage)
    {
        ThrowIfAutomationStopRequested();

        var playerPositioned = GameController?.Game?.IngameState?.Data?.LocalPlayer?.GetComponent<Positioned>();
        var targetPositioned = entity?.GetComponent<Positioned>();
        var navigator = Core.GetNavigator();
        if (playerPositioned == null || targetPositioned == null || navigator == null)
        {
            LogDebug($"Navigation to {label} unavailable. entity={DescribeEntity(entity)}");
            return false;
        }

        var path = navigator.FindPath(playerPositioned.GridPosNum, targetPositioned.GridPosNum, AutomationNavigationNodeSize);
        if (path == null || path.Count == 0)
        {
            LogDebug($"Navigation path to {label} could not be resolved. entity={DescribeEntity(entity)}");
            return false;
        }

        var destination = SelectNavigationDestination(path, playerPositioned.GridPosNum);
        if (!TryGetAbsoluteScreenPositionForGrid(destination, out var absoluteScreenPosition))
        {
            LogDebug($"Navigation click position for {label} could not be resolved. destination={destination}");
            return false;
        }

        UpdateAutomationStatus(statusMessage);
        await ClickAtAsync(
            absoluteScreenPosition,
            holdCtrl: false,
            preClickDelayMs: AutomationTiming.UiClickPreDelayMs,
            postClickDelayMs: Math.Max(AutomationTiming.FastPollDelayMs, GetConfiguredClickDelayMs()));
        return true;
    }

    private async Task<Entity> WaitForAutomationEntityAsync(
        Func<Entity> visibleResolver,
        Func<Entity> nearestResolver,
        string label,
        string statusMessage,
        int timeoutMs)
    {
        return await PollAutomationValueAsync(
            visibleResolver,
            visibleEntity => visibleEntity != null,
            timeoutMs,
            AutomationTiming.FastPollDelayMs,
            onPendingAsync: async _ =>
            {
                var entity = nearestResolver();
                if (entity != null)
                {
                    await NavigateTowardsEntityAsync(entity, label, statusMessage);
                }
            });
    }

    private async Task<bool> TryAdvanceWorldEntityOpenStepAsync(
        Func<Entity> findVisibleEntity,
        Func<Entity> findNearestEntity,
        string moveEntityLabel,
        string openStatusLabel,
        string missingEntityStatus,
        string navigateFailureStatus,
        Func<Entity, string, Task<bool>> interactAsync)
    {
        var visibleEntity = findVisibleEntity?.Invoke();
        if (visibleEntity == null)
        {
            var nearestEntity = findNearestEntity?.Invoke();
            if (nearestEntity == null)
            {
                if (!string.IsNullOrWhiteSpace(missingEntityStatus))
                {
                    UpdateAutomationStatus(missingEntityStatus, forceLog: true);
                }

                return false;
            }

            if (!await NavigateTowardsEntityAsync(nearestEntity, moveEntityLabel, $"Moving to {moveEntityLabel}..."))
            {
                if (!string.IsNullOrWhiteSpace(navigateFailureStatus))
                {
                    UpdateAutomationStatus(navigateFailureStatus, forceLog: true);
                }

                return false;
            }

            return true;
        }

        var distance = GetPlayerDistanceToEntity(visibleEntity);
        var statusMessage = distance.HasValue && distance.Value <= AutomationTiming.StashInteractionDistance
            ? $"Opening {openStatusLabel}..."
            : $"Moving to {moveEntityLabel}...";
        return await interactAsync(visibleEntity, statusMessage);
    }

    private static GridVector2 SelectNavigationDestination(IReadOnlyList<GridVector2> path, GridVector2 playerGridPos)
    {
        var maxIndex = Math.Min(path.Count - 1, AutomationNavigationLookAheadIndex);
        for (var index = maxIndex; index >= 0; index--)
        {
            if (GridVector2.Distance(path[index], playerGridPos) >= AutomationNavigationMinMoveDistance)
            {
                return path[index];
            }
        }

        return path[^1];
    }

    private bool TryGetAbsoluteScreenPositionForGrid(GridVector2 gridPos, out Vector2 position)
    {
        position = default;

        var data = GameController?.Game?.IngameState?.Data;
        var camera = GameController?.Game?.IngameState?.Camera;
        var window = GameController?.Window;
        if (data == null || camera == null || window == null)
        {
            return false;
        }

        var relativePosition = camera.WorldToScreen(data.ToWorldWithTerrainHeight(gridPos));
        if (!IsFiniteScreenPosition(relativePosition))
        {
            return false;
        }

        var windowRect = window.GetWindowRectangle();
        var clampedRelativePosition = ClampRelativeScreenPosition(relativePosition, windowRect.Width, windowRect.Height);
        position = new Vector2(windowRect.X + clampedRelativePosition.X, windowRect.Y + clampedRelativePosition.Y);
        return true;
    }

    private static bool IsFiniteScreenPosition(GridVector2 position)
    {
        return !float.IsNaN(position.X) && !float.IsNaN(position.Y) &&
               !float.IsInfinity(position.X) && !float.IsInfinity(position.Y);
    }

    private static GridVector2 ClampRelativeScreenPosition(GridVector2 position, float width, float height)
    {
        const float left = 10f;
        const float top = 10f;
        var right = Math.Max(20f, width - 20f);
        var bottom = Math.Max(20f, height - 130f);
        if (position.X >= left && position.X <= right && position.Y >= top && position.Y <= bottom)
        {
            return position;
        }

        var center = new GridVector2(width / 2f, bottom / 2f);
        var delta = position - center;
        if (delta.LengthSquared() < 0.001f)
        {
            return center;
        }

        var clamped = center + GridVector2.Normalize(delta) * AutomationNavigationClampRadius;
        return new GridVector2(
            Math.Clamp(clamped.X, left, right),
            Math.Clamp(clamped.Y, top, bottom));
    }
}
