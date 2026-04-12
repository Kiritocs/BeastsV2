using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2.Runtime.Analytics;

internal sealed record AnalyticsSnapshotCallbacks(
    Action BuildAnalyticsLines,
    Func<int> GetCompletedMapCount,
    Func<TimeSpan> GetCompletedMapsDuration,
    Func<IReadOnlyList<MapAnalyticsRecord>> GetMapHistory,
    Func<double> ComputeCurrentMapCapturedChaos,
    Func<double> ComputePerMapCostChaos,
    Func<MapCostItem[]> ComputePerMapCostBreakdown,
    Func<bool, MapReplayEvent[]> BuildCurrentMapReplayEvents,
    Func<DateTime, TimeSpan> GetCurrentMapTime,
    Func<DateTime, TimeSpan> GetTotalSessionTime,
    Func<bool, (BeastTotalV2[] BeastTotals, FamilyTotalV2[] FamilyTotals)> BuildSessionTotals,
    Func<bool> GetIsCurrentAreaTrackable,
    Func<bool> GetIsPaused,
    Func<string> GetActiveAreaHash,
    Func<string> GetActiveAreaName,
    Func<int> GetSessionBeastsFound,
    Func<int> GetTotalRedBeastsSession,
    Func<int> GetCurrentMapBeastsFound,
    Func<int> GetCurrentMapRedBeastsFound,
    Func<bool> GetCurrentMapUsedDuplicatingScarab,
    Func<IReadOnlyList<MapCostItem>> GetCurrentMapCostBreakdown,
    Func<double?> GetCurrentMapFirstRedSeenSeconds,
    Func<string, bool> IsDuplicatingScarabItemName,
    Func<int> GetRollingStatsWindowMaps,
    Func<string[]> GetTrackedBeastNames,
    Func<MapAnalyticsRecord, MapAnalyticsRecord> CloneMapRecord,
    int MaxMapHistoryEntries);

internal sealed class AnalyticsSnapshotService
{
    private readonly AnalyticsSnapshotCallbacks _callbacks;

    public AnalyticsSnapshotService(AnalyticsSnapshotCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public SessionCurrentResponseV2 BuildWebSnapshot(DateTime now)
    {
        _callbacks.BuildAnalyticsLines();

        var completedMapCount = _callbacks.GetCompletedMapCount();
        var mapHistory = _callbacks.GetMapHistory();
        var averageMapTime = completedMapCount > 0
            ? TimeSpan.FromTicks(_callbacks.GetCompletedMapsDuration().Ticks / completedMapCount)
            : TimeSpan.Zero;

        var completedCaptured = mapHistory.Sum(x => x.CapturedChaos);
        var completedCost = mapHistory.Sum(x => x.CostChaos);
        var currentCaptured = _callbacks.ComputeCurrentMapCapturedChaos();
        var currentCost = _callbacks.GetIsCurrentAreaTrackable() ? _callbacks.ComputePerMapCostChaos() : 0d;

        var sessionCaptured = completedCaptured + currentCaptured;
        var sessionCost = completedCost + currentCost;
        var sessionNet = sessionCaptured - sessionCost;
        var sessionHours = Math.Max(_callbacks.GetTotalSessionTime(now).TotalHours, 1d / 3600d);

        var (beastTotals, familyTotals) = _callbacks.BuildSessionTotals(true);
        var currentMapCostBreakdown = _callbacks.GetCurrentMapCostBreakdown();

        return new SessionCurrentResponseV2
        {
            GeneratedAtUtc = now,
            IsCurrentAreaTrackable = _callbacks.GetIsCurrentAreaTrackable(),
            IsPaused = _callbacks.GetIsPaused(),
            ActiveAreaHash = _callbacks.GetActiveAreaHash() ?? string.Empty,
            ActiveAreaName = _callbacks.GetActiveAreaName() ?? string.Empty,

            CurrentMapDurationSeconds = _callbacks.GetCurrentMapTime(now).TotalSeconds,
            AverageMapDurationSeconds = averageMapTime.TotalSeconds,
            SessionDurationSeconds = _callbacks.GetTotalSessionTime(now).TotalSeconds,

            CompletedMapCount = completedMapCount,
            SessionBeastsFound = _callbacks.GetSessionBeastsFound(),
            SessionRedBeastsFound = _callbacks.GetTotalRedBeastsSession(),
            CurrentMapBeastsFound = _callbacks.GetCurrentMapBeastsFound(),
            CurrentMapRedBeastsFound = _callbacks.GetCurrentMapRedBeastsFound(),

            CurrentMapCapturedChaos = currentCaptured,
            CurrentMapCostChaos = currentCost,
            CurrentMapNetChaos = currentCaptured - currentCost,
            CurrentMapUsesDuplicatingScarab = _callbacks.GetIsCurrentAreaTrackable() &&
                                              (_callbacks.GetCurrentMapUsedDuplicatingScarab() || currentMapCostBreakdown.Any(x => _callbacks.IsDuplicatingScarabItemName(x?.ItemName))),
            CurrentMapFirstRedSeenSeconds = _callbacks.GetIsCurrentAreaTrackable() ? _callbacks.GetCurrentMapFirstRedSeenSeconds() : null,
            CurrentMapCostBreakdown = _callbacks.GetIsCurrentAreaTrackable() ? _callbacks.ComputePerMapCostBreakdown() : [],
            CurrentMapReplayEvents = _callbacks.GetIsCurrentAreaTrackable() ? _callbacks.BuildCurrentMapReplayEvents(false) : [],

            SessionCapturedChaos = sessionCaptured,
            SessionCostChaos = sessionCost,
            SessionNetChaos = sessionNet,
            SessionCapturedPerHourChaos = sessionCaptured / sessionHours,
            SessionNetPerHourChaos = sessionNet / sessionHours,
            AverageCapturedPerMapChaos = completedMapCount > 0 ? completedCaptured / completedMapCount : 0d,
            AverageNetPerMapChaos = completedMapCount > 0 ? mapHistory.Average(x => x.NetChaos) : 0d,

            Rolling = AnalyticsEngineV2.BuildRollingStats(mapHistory, Math.Max(1, _callbacks.GetRollingStatsWindowMaps())),
            FamilyTotals = familyTotals,
            BeastTotals = beastTotals,
            TrackedBeastNames = _callbacks.GetTrackedBeastNames(),
        };
    }

    public MapListResponseV2 BuildMapList(int offset, int limit)
    {
        var normalizedOffset = Math.Max(0, offset);
        var normalizedLimit = Math.Clamp(limit, 1, _callbacks.MaxMapHistoryEntries);

        var ordered = _callbacks.GetMapHistory().OrderByDescending(x => x.CompletedAtUtc).ToArray();
        return new MapListResponseV2
        {
            Total = ordered.Length,
            Offset = normalizedOffset,
            Limit = normalizedLimit,
            Items = ordered
                .Skip(normalizedOffset)
                .Take(normalizedLimit)
                .Select(_callbacks.CloneMapRecord)
                .Select(AnalyticsEngineV2.BuildMapListItem)
                .Where(item => item != null)
                .ToArray(),
        };
    }
}