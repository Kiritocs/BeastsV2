using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2.Runtime.Features;

internal sealed record ExplorationRouteRefreshCallbacks(
    Func<bool> GetRouteNeedsRegen,
    Action<bool> SetRouteNeedsRegen,
    Func<bool?> GetLastEnabled,
    Action<bool?> SetLastEnabled,
    Func<int> GetLastRouteDetectionRadius,
    Action<int> SetLastRouteDetectionRadius,
    Func<bool?> GetLastPreferPerimeterFirstRoute,
    Action<bool?> SetLastPreferPerimeterFirstRoute,
    Func<bool?> GetLastVisitOuterShellLast,
    Action<bool?> SetLastVisitOuterShellLast,
    Func<bool?> GetLastFollowMapOutlineFirst,
    Action<bool?> SetLastFollowMapOutlineFirst,
    Func<string> GetLastExcludedEntityPathsSnapshot,
    Action<string> SetLastExcludedEntityPathsSnapshot,
    Func<int> GetLastEntityExclusionRadius,
    Action<int> SetLastEntityExclusionRadius,
    Func<int> GetDetectionRadius,
    Func<bool> GetEnabled,
    Func<bool> GetPreferPerimeterFirstRoute,
    Func<bool> GetVisitOuterShellLast,
    Func<bool> GetFollowMapOutlineFirst,
    Func<string> GetExcludedEntityPaths,
    Action<string> SetExcludedEntityPaths,
    Func<int> GetEntityExclusionRadius,
    Action CancelBeastPaths,
    Action ClearExplorationRouteState,
    Action GenerateExplorationRoute);

internal sealed class ExplorationRouteRefreshService
{
    private readonly ExplorationRouteRefreshCallbacks _callbacks;

    public ExplorationRouteRefreshService(ExplorationRouteRefreshCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void CommitExcludedEntityPaths(List<string> paths)
    {
        var joined = string.Join("\n", paths.Select(p => p?.Trim() ?? string.Empty));
        if (string.Equals(_callbacks.GetExcludedEntityPaths(), joined, StringComparison.Ordinal))
        {
            return;
        }

        _callbacks.SetExcludedEntityPaths(joined);
        _callbacks.SetRouteNeedsRegen(true);
    }

    public void EnsureCurrent()
    {
        var enabled = _callbacks.GetEnabled();
        if (_callbacks.GetLastEnabled() != enabled)
        {
            _callbacks.SetLastEnabled(enabled);
            if (!enabled)
            {
                ClearDisabledState();
                return;
            }

            MarkDirty();
        }

        if (!enabled)
        {
            return;
        }

        var detectionRadius = _callbacks.GetDetectionRadius();
        if (_callbacks.GetLastRouteDetectionRadius() != detectionRadius)
        {
            _callbacks.SetLastRouteDetectionRadius(detectionRadius);
            MarkDirty();
        }

        var preferPerimeter = _callbacks.GetPreferPerimeterFirstRoute();
        if (_callbacks.GetLastPreferPerimeterFirstRoute() != preferPerimeter)
        {
            _callbacks.SetLastPreferPerimeterFirstRoute(preferPerimeter);
            MarkDirty();
        }

        var visitOuterLast = _callbacks.GetVisitOuterShellLast();
        if (_callbacks.GetLastVisitOuterShellLast() != visitOuterLast)
        {
            _callbacks.SetLastVisitOuterShellLast(visitOuterLast);
            MarkDirty();
        }

        var followOutline = _callbacks.GetFollowMapOutlineFirst();
        if (_callbacks.GetLastFollowMapOutlineFirst() != followOutline)
        {
            _callbacks.SetLastFollowMapOutlineFirst(followOutline);
            MarkDirty();
        }

        var excludedPaths = _callbacks.GetExcludedEntityPaths() ?? string.Empty;
        var lastExcludedPaths = _callbacks.GetLastExcludedEntityPathsSnapshot();
        if (lastExcludedPaths == null)
        {
            _callbacks.SetLastExcludedEntityPathsSnapshot(excludedPaths);
        }
        else if (!string.Equals(lastExcludedPaths, excludedPaths, StringComparison.Ordinal))
        {
            _callbacks.SetLastExcludedEntityPathsSnapshot(excludedPaths);
            MarkDirty();
        }

        var entityExclusionRadius = _callbacks.GetEntityExclusionRadius();
        var lastEntityExclusionRadius = _callbacks.GetLastEntityExclusionRadius();
        if (lastEntityExclusionRadius < 0)
        {
            _callbacks.SetLastEntityExclusionRadius(entityExclusionRadius);
        }
        else if (lastEntityExclusionRadius != entityExclusionRadius)
        {
            _callbacks.SetLastEntityExclusionRadius(entityExclusionRadius);
            MarkDirty();
        }

        if (_callbacks.GetRouteNeedsRegen())
        {
            _callbacks.SetRouteNeedsRegen(false);
            _callbacks.GenerateExplorationRoute();
        }
    }

    public void RequestRegen()
    {
        if (!_callbacks.GetEnabled())
        {
            ClearDisabledState();
            return;
        }

        _callbacks.SetRouteNeedsRegen(true);
        _callbacks.CancelBeastPaths();
    }

    private void ClearDisabledState()
    {
        _callbacks.CancelBeastPaths();
        _callbacks.ClearExplorationRouteState();
        _callbacks.SetRouteNeedsRegen(false);
    }

    private void MarkDirty()
    {
        _callbacks.SetRouteNeedsRegen(true);
        _callbacks.CancelBeastPaths();
    }
}