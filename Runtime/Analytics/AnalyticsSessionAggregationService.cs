using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2.Runtime.Analytics;

internal sealed record AnalyticsSessionAggregationCallbacks(
    Func<IReadOnlyList<MapAnalyticsRecord>> GetMapHistory,
    Func<MapAnalyticsRecord, MapAnalyticsRecord> CloneMapRecord,
    Func<double> ComputeCurrentMapCapturedChaos,
    Func<bool> GetIsCurrentAreaTrackable,
    Func<double> ComputePerMapCostChaos,
    Func<bool, (BeastTotalV2[] BeastTotals, FamilyTotalV2[] FamilyTotals)> BuildSessionTotals,
    Func<DateTime, TimeSpan> GetTotalSessionTime,
    Func<int> GetCompletedMapCount,
    Func<int> GetSessionBeastsFound,
    Func<int> GetTotalRedBeastsSession,
    Func<IReadOnlyList<MapCostItem>> GetPreparedMapCostBreakdown,
    Func<TimeSpan> GetCurrentMapElapsed,
    Func<int> GetCurrentMapBeastsFound,
    Func<int> GetCurrentMapRedBeastsFound,
    Func<IReadOnlyDictionary<string, int>> GetCurrentMapValuableBeastCounts,
    Func<IReadOnlyDictionary<string, int>> GetCurrentMapValuableBeastCapturedCounts,
    Func<IReadOnlyList<MapCostItem>> GetCurrentMapCostBreakdown,
    Func<IReadOnlyDictionary<string, float>> GetBeastPrices,
    Func<double?> GetCurrentMapFirstRedSeenSeconds,
    Func<MapReplayEvent[]> BuildFinalCurrentMapReplayEvents,
    Func<bool> GetCurrentMapUsedDuplicatingScarab,
    Func<string, bool> IsDuplicatingScarabItemName,
    Action<MapAnalyticsRecord> ApplyMapRecord,
    Action ResetCurrentMapAnalytics,
    Func<string, string> NormalizeTag);

internal sealed class AnalyticsSessionAggregationService
{
    private readonly AnalyticsSessionAggregationCallbacks _callbacks;

    public AnalyticsSessionAggregationService(AnalyticsSessionAggregationCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public SavedSessionDataV2 BuildSavedSessionData(DateTime now, CreateSessionSaveRequestV2 request)
    {
        var mapHistory = BuildOrderedMapHistory(out var completedCaptured, out var completedCost);

        var currentCaptured = _callbacks.ComputeCurrentMapCapturedChaos();
        var currentCost = _callbacks.GetIsCurrentAreaTrackable() ? _callbacks.ComputePerMapCostChaos() : 0d;

        var (beastTotals, familyTotals) = _callbacks.BuildSessionTotals(true);

        return new SavedSessionDataV2
        {
            SchemaVersion = 2,
            SessionId = Guid.NewGuid().ToString("N"),
            SavedAtUtc = now,
            IsAutoSave = request?.IsAutoSave == true,
            Name = AnalyticsEngineV2.BuildSessionDisplayName(request?.Name, now, request?.IsAutoSave == true),
            Tags = new SavedSessionTagsV2
            {
                Strategy = _callbacks.NormalizeTag(request?.StrategyTag),
                Scarab = _callbacks.NormalizeTag(request?.ScarabTag),
                Atlas = _callbacks.NormalizeTag(request?.AtlasTag),
                MapPool = _callbacks.NormalizeTag(request?.MapPoolTag),
            },
            Summary = new SavedSessionSummaryV2
            {
                DurationSeconds = _callbacks.GetTotalSessionTime(now).TotalSeconds,
                MapsCompleted = _callbacks.GetCompletedMapCount(),
                BeastsFound = _callbacks.GetSessionBeastsFound(),
                RedBeastsFound = _callbacks.GetTotalRedBeastsSession(),
                CapturedChaos = completedCaptured + currentCaptured,
                CostChaos = completedCost + currentCost,
                NetChaos = (completedCaptured + currentCaptured) - (completedCost + currentCost),
            },
            BeastTotals = beastTotals,
            FamilyTotals = familyTotals,
            MapHistory = mapHistory,
            CostDefaults = AnalyticsEngineV2.CloneCostBreakdown(_callbacks.GetPreparedMapCostBreakdown()).ToArray(),
        };
    }

    private MapAnalyticsRecord[] BuildOrderedMapHistory(out double completedCaptured, out double completedCost)
    {
        var source = _callbacks.GetMapHistory();
        if (source == null || source.Count <= 0)
        {
            completedCaptured = 0d;
            completedCost = 0d;
            return [];
        }

        completedCaptured = 0d;
        completedCost = 0d;
        var mapHistory = new List<MapAnalyticsRecord>(source.Count);
        foreach (var record in source.OrderByDescending(x => x.CompletedAtUtc))
        {
            var clone = _callbacks.CloneMapRecord(record);
            completedCaptured += clone.CapturedChaos;
            completedCost += clone.CostChaos;
            mapHistory.Add(clone);
        }

        return mapHistory.ToArray();
    }

    public void FinalizeCurrentMapAnalytics(string areaHash, string areaName, DateTime now)
    {
        if (_callbacks.GetCurrentMapBeastsFound() <= 0 && _callbacks.GetCurrentMapRedBeastsFound() <= 0 && _callbacks.GetCurrentMapElapsed() <= TimeSpan.Zero)
        {
            _callbacks.ResetCurrentMapAnalytics();
            return;
        }

        var currentMapCostBreakdown = _callbacks.GetCurrentMapCostBreakdown();
        var record = AnalyticsEngineV2.BuildMapRecord(
            now,
            areaHash,
            areaName,
            _callbacks.GetCurrentMapElapsed(),
            _callbacks.GetCurrentMapBeastsFound(),
            _callbacks.GetCurrentMapRedBeastsFound(),
            _callbacks.GetCurrentMapValuableBeastCounts(),
            _callbacks.GetCurrentMapValuableBeastCapturedCounts(),
            currentMapCostBreakdown,
            _callbacks.GetBeastPrices(),
            _callbacks.GetCurrentMapFirstRedSeenSeconds(),
            _callbacks.BuildFinalCurrentMapReplayEvents(),
            _callbacks.GetCurrentMapUsedDuplicatingScarab() || currentMapCostBreakdown.Any(x => _callbacks.IsDuplicatingScarabItemName(x?.ItemName)));

        _callbacks.ApplyMapRecord(record);
        _callbacks.ResetCurrentMapAnalytics();
    }
}