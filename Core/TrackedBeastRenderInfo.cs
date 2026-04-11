using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;

namespace BeastsV2;

internal readonly record struct TrackedBeastRenderInfo(Entity Entity, Positioned Positioned, string BeastName, BeastCaptureState CaptureState);