using System;
using BeastsV2.Runtime.State;
using ExileCore;

namespace BeastsV2.Runtime.Lifecycle;

internal enum AreaTransitionKind
{
    EnteredNonTrackableArea,
    ReenteredFinalizedMap,
    ReenteredActiveMap,
    EnteredNewTrackableMap,
}

internal sealed record AreaTransitionDecision(
    AreaTransitionKind Kind,
    string PreviousAreaHash,
    string PreviousAreaName,
    int PreviousAreaInstanceId,
    string NewAreaHash,
    string NewAreaName,
    int NewAreaInstanceId,
    bool ShouldFinalizePreviousMap);

internal sealed class AreaTransitionCoordinator
{
    private const string MenagerieAreaName = "The Menagerie";
    private readonly BeastsRuntimeState _state;

    public AreaTransitionCoordinator(BeastsRuntimeState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public AreaTransitionDecision Evaluate(AreaInstance area, DateTime now, bool hasCurrentMapProgress)
    {
        var map = _state.Map;
        var session = _state.Session;

        var previousAreaHash = map.ActiveMapAreaHash;
        var previousAreaName = map.ActiveMapAreaName;
        var previousAreaInstanceId = map.ActiveMapInstanceId;
        var newAreaHash = BeastsV2Helpers.TryGetAreaHashText(area) ?? string.Empty;
        var newAreaName = BeastsV2Helpers.TryGetAreaNameText(area) ?? string.Empty;
        var newAreaInstanceId = BeastsV2Helpers.TryGetAreaInstanceId(area);
        var newAreaTrackable = IsRunnableMapArea(area);

        if (!newAreaTrackable)
        {
            var shouldFinalizePreviousMap = map.CurrentMapWasComplete &&
                                            (session.CurrentMapElapsed > TimeSpan.Zero || hasCurrentMapProgress);

            map.IsCurrentAreaTrackable = false;
            map.ShouldRenderFinalizedMapCompletionOverlay = false;
            if (map.CurrentMapWasComplete)
            {
                map.MapWasFinalized = true;
                map.CurrentMapWasComplete = false;
            }

            return new AreaTransitionDecision(
                AreaTransitionKind.EnteredNonTrackableArea,
                previousAreaHash,
                previousAreaName,
                previousAreaInstanceId,
                newAreaHash,
                newAreaName,
                newAreaInstanceId,
                shouldFinalizePreviousMap);
        }

        var hashMatches = !string.IsNullOrWhiteSpace(previousAreaHash) &&
                          !string.IsNullOrWhiteSpace(newAreaHash) &&
                          string.Equals(newAreaHash, previousAreaHash, StringComparison.Ordinal);
        var instanceMatches = previousAreaInstanceId >= 0 && newAreaInstanceId >= 0 &&
                              newAreaInstanceId == previousAreaInstanceId;

        if (hashMatches || instanceMatches)
        {
            map.ActiveMapAreaHash = newAreaHash;
            map.ActiveMapAreaName = newAreaName;
            map.ActiveMapInstanceId = newAreaInstanceId;

            if (map.MapWasFinalized)
            {
                map.IsCurrentAreaTrackable = false;
                map.ShouldRenderFinalizedMapCompletionOverlay = true;
                session.CurrentMapStartUtc = null;

                return new AreaTransitionDecision(
                    AreaTransitionKind.ReenteredFinalizedMap,
                    previousAreaHash,
                    previousAreaName,
                    previousAreaInstanceId,
                    newAreaHash,
                    newAreaName,
                    newAreaInstanceId,
                    false);
            }

            map.IsCurrentAreaTrackable = true;
            map.ShouldRenderFinalizedMapCompletionOverlay = false;
            session.CurrentMapStartUtc = now;

            return new AreaTransitionDecision(
                AreaTransitionKind.ReenteredActiveMap,
                previousAreaHash,
                previousAreaName,
                previousAreaInstanceId,
                newAreaHash,
                newAreaName,
                newAreaInstanceId,
                false);
        }

        var shouldFinalizeTrackableMap = map.IsCurrentAreaTrackable ||
                                         session.CurrentMapElapsed > TimeSpan.Zero ||
                                         hasCurrentMapProgress;

        map.MapWasFinalized = false;
        map.ShouldRenderFinalizedMapCompletionOverlay = false;
        map.ActiveMapAreaHash = newAreaHash;
        map.ActiveMapAreaName = newAreaName;
        map.ActiveMapInstanceId = newAreaInstanceId;
        session.CurrentMapElapsed = TimeSpan.Zero;
        map.IsCurrentAreaTrackable = true;
        session.CurrentMapStartUtc = now;
        map.CurrentMapWasComplete = false;

        return new AreaTransitionDecision(
            AreaTransitionKind.EnteredNewTrackableMap,
            previousAreaHash,
            previousAreaName,
            previousAreaInstanceId,
            newAreaHash,
            newAreaName,
            newAreaInstanceId,
            shouldFinalizeTrackableMap);
    }

    private static bool IsHideoutLikeArea(AreaInstance area)
    {
        return area?.IsHideout == true ||
               area?.Name.EqualsIgnoreCase(MenagerieAreaName) == true;
    }

    private static bool IsRunnableMapArea(AreaInstance area)
    {
        return area is { IsTown: false } && !IsHideoutLikeArea(area);
    }
}