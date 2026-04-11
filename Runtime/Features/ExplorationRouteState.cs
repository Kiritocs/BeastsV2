using System.Collections.Generic;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2.Runtime.Features;

internal sealed class ExplorationRouteState
{
    public List<Vector2> Route { get; set; } = [];

    public HashSet<int> VisitedWaypointIndices { get; } = [];

    public bool RouteNeedsRegen { get; set; } = true;

    public int LastRouteDetectionRadius { get; set; } = -1;

    public bool? LastPreferPerimeterFirstRoute { get; set; }

    public bool? LastVisitOuterShellLast { get; set; }

    public bool? LastFollowMapOutlineFirst { get; set; }

    public bool? LastEnabled { get; set; }

    public string LastExcludedEntityPathsSnapshot { get; set; }

    public int LastEntityExclusionRadius { get; set; } = -1;

    public int ExplorationRouteCacheStep { get; set; } = 4;

    public int ExplorationRouteCacheMinWallDist { get; set; } = 6;

    public bool[][] DebugReachableMask { get; set; }

    public int[][] DebugDistanceField { get; set; }

    public int DebugMaxX { get; set; }

    public int DebugMaxY { get; set; }

    public List<Vector2> ExclusionZonePositions { get; } = [];
}