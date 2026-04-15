using Vector2 = System.Numerics.Vector2;

namespace BeastsV2;

internal readonly record struct TrackedBeastMapMarkerInfo(long EntityId, Vector2 GridPos, string BeastName, BeastCaptureState CaptureState);