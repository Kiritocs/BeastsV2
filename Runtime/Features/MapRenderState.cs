using System.Collections.Generic;
using System.Threading;
using GameOffsets.Native;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace BeastsV2.Runtime.Features;

internal sealed class MapRenderState
{
    public double MapScale { get; set; }

    public RectangleF MapRect { get; set; }

    public ImDrawListPtr MapDrawList { get; set; }

    public Vector2[] WorldCircleScreenPoints { get; } = new Vector2[WorldCirclePointsLength];

    public CancellationTokenSource PathFindingCts { get; set; } = new();

    public List<Vector2i> ExplorationPath { get; set; }

    public int ExplorationPathForIdx { get; set; } = -1;

    public const int WorldCirclePointsLength = 15;
}