using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore.PoEMemory.MemoryObjects;

namespace BeastsV2;

public partial class Main
{
    private static readonly StringComparer AnalyticsTotalsComparer = StringComparer.OrdinalIgnoreCase;

    private HashSet<string> _loadedSaveIds => AnalyticsPersistenceRuntime.LoadedSaveIds;
    private Dictionary<string, SavedSessionDataV2> _loadedSaveCacheById => AnalyticsPersistenceRuntime.LoadedSaveCacheById;
    private SessionStoreV2 _sessionStore => AnalyticsPersistenceRuntime.SessionStore;
    private SessionStoreV2 _autoSaveSessionStore => AnalyticsPersistenceRuntime.AutoSaveSessionStore;

    private void SaveSessionSnapshotToFile() => SaveSessionSnapshotToFile((CreateSessionSaveRequestV2)null);

    private void AutoSaveSessionSnapshotToFile() => SaveSessionSnapshotToFile(new CreateSessionSaveRequestV2 { IsAutoSave = true });

    private bool SaveSessionSnapshotToFile(string customSessionName)
        => SaveSessionSnapshotToFile(new CreateSessionSaveRequestV2 { Name = customSessionName ?? string.Empty });

    private bool SaveSessionSnapshotToFile(CreateSessionSaveRequestV2 request) => AnalyticsSessions.SaveSessionSnapshot(request);

    private SavedSessionDataV2 BuildSavedSessionData(DateTime now, CreateSessionSaveRequestV2 request) =>
        AnalyticsSessionAggregation.BuildSavedSessionData(now, request);

    private SavedSessionDataV2 BuildCurrentLiveSessionDataForDuplicateCheck(DateTime now)
    {
        var snapshot = BuildSavedSessionData(now, null);
        if (snapshot == null)
            return null;

        foreach (var loaded in _loadedSaveCacheById.Values)
            SubtractLoadedSessionData(snapshot, loaded);

        snapshot.SaveId = string.Empty;
        snapshot.SessionId = string.Empty;
        snapshot.Name = string.Empty;
        snapshot.IsAutoSave = false;
        snapshot.SavedAtUtc = now;
        return snapshot;
    }

    private static void SubtractLoadedSessionData(SavedSessionDataV2 target, SavedSessionDataV2 loaded)
    {
        if (target == null || loaded == null)
            return;

        var targetSummary = target.Summary ??= new SavedSessionSummaryV2();
        var loadedSummary = loaded.Summary ?? new SavedSessionSummaryV2();
        targetSummary.DurationSeconds = Math.Max(0d, targetSummary.DurationSeconds - loadedSummary.DurationSeconds);
        targetSummary.MapsCompleted = Math.Max(0, targetSummary.MapsCompleted - loadedSummary.MapsCompleted);
        targetSummary.BeastsFound = Math.Max(0, targetSummary.BeastsFound - loadedSummary.BeastsFound);
        targetSummary.RedBeastsFound = Math.Max(0, targetSummary.RedBeastsFound - loadedSummary.RedBeastsFound);
        targetSummary.CapturedChaos = Math.Max(0d, targetSummary.CapturedChaos - loadedSummary.CapturedChaos);
        targetSummary.CostChaos = Math.Max(0d, targetSummary.CostChaos - loadedSummary.CostChaos);
        targetSummary.NetChaos -= loadedSummary.NetChaos;

        target.BeastTotals = SubtractBeastTotals(target.BeastTotals, loaded.BeastTotals);
        target.FamilyTotals = SubtractFamilyTotals(target.FamilyTotals, loaded.FamilyTotals);

        var loadedMapIds = (loaded.MapHistory ?? [])
            .Select(map => map?.MapId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.MapHistory = (target.MapHistory ?? [])
            .Where(map => map != null && !loadedMapIds.Contains(map.MapId))
            .ToArray();
    }

    private static BeastTotalV2[] SubtractBeastTotals(IEnumerable<BeastTotalV2> target, IEnumerable<BeastTotalV2> loaded)
        => SubtractTotals(
            target,
            loaded,
            item => item.BeastName,
            item => new BeastTotalV2
            {
                BeastName = item.BeastName ?? string.Empty,
                CapturedCount = item.CapturedCount,
                UnitPriceChaos = item.UnitPriceChaos,
                CapturedChaos = item.CapturedChaos,
            },
            (existing, item) =>
            {
                existing.CapturedCount = Math.Max(0, existing.CapturedCount - item.CapturedCount);
                existing.CapturedChaos = Math.Max(0d, existing.CapturedChaos - item.CapturedChaos);
            },
            existing => existing.CapturedCount == 0 && existing.CapturedChaos <= 0d);

    private static FamilyTotalV2[] SubtractFamilyTotals(IEnumerable<FamilyTotalV2> target, IEnumerable<FamilyTotalV2> loaded)
        => SubtractTotals(
            target,
            loaded,
            item => item.FamilyName,
            item => new FamilyTotalV2
            {
                FamilyName = item.FamilyName ?? string.Empty,
                CapturedCount = item.CapturedCount,
                CapturedChaos = item.CapturedChaos,
            },
            (existing, item) =>
            {
                existing.CapturedCount = Math.Max(0, existing.CapturedCount - item.CapturedCount);
                existing.CapturedChaos = Math.Max(0d, existing.CapturedChaos - item.CapturedChaos);
            },
            existing => existing.CapturedCount == 0 && existing.CapturedChaos <= 0d);

    private static TTotal[] SubtractTotals<TTotal>(
        IEnumerable<TTotal> target,
        IEnumerable<TTotal> loaded,
        Func<TTotal, string> keySelector,
        Func<TTotal, TTotal> clone,
        Action<TTotal, TTotal> subtract,
        Func<TTotal, bool> shouldRemove)
    {
        var totals = new Dictionary<string, TTotal>(AnalyticsTotalsComparer);
        foreach (var item in target ?? [])
        {
            totals.Add(keySelector(item) ?? string.Empty, clone(item));
        }

        foreach (var item in loaded ?? [])
        {
            var key = keySelector(item) ?? string.Empty;
            if (!totals.TryGetValue(key, out var existing))
                continue;

            subtract(existing, item);
            if (shouldRemove(existing))
                totals.Remove(key);
        }

        return totals
            .OrderBy(entry => entry.Key, AnalyticsTotalsComparer)
            .Select(entry => entry.Value)
            .ToArray();
    }

    private static MapAnalyticsRecord CloneMapRecord(MapAnalyticsRecord source)
    {
        if (source == null)
            return null;

        return new MapAnalyticsRecord
        {
            MapId = source.MapId,
            CompletedAtUtc = source.CompletedAtUtc,
            AreaHash = source.AreaHash,
            AreaName = source.AreaName,
            DurationSeconds = source.DurationSeconds,
            BeastsFound = source.BeastsFound,
            RedBeastsFound = source.RedBeastsFound,
            CapturedChaos = source.CapturedChaos,
            CostChaos = source.CostChaos,
            NetChaos = source.NetChaos,
            UsedBestiaryScarabOfDuplicating = source.UsedBestiaryScarabOfDuplicating,
            FirstRedSeenSeconds = source.FirstRedSeenSeconds,
            BeastBreakdown = (source.BeastBreakdown ?? []).Select(x => new MapBeastStat
            {
                BeastName = x.BeastName,
                Count = x.Count,
                CapturedCount = x.CapturedCount,
                UnitPriceChaos = x.UnitPriceChaos,
            }).ToArray(),
            CostBreakdown = AnalyticsEngineV2.CloneCostBreakdown(source.CostBreakdown).ToArray(),
            ReplayEvents = AnalyticsEngineV2.CloneReplayEvents(source.ReplayEvents).ToArray(),
        };
    }

    private static int GetReplayEventSortOrder(string eventType)
    {
        return eventType?.Trim().ToLowerInvariant() switch
        {
            "seen" => 0,
            "captured" => 1,
            "missed" => 2,
            _ => 9,
        };
    }

    private double GetCurrentMapReplayOffsetSeconds(DateTime now)
        => Math.Max(0d, GetCurrentMapTime(now).TotalSeconds);

    private double GetTrackedBeastUnitPriceChaos(string beastName)
    {
        return !string.IsNullOrWhiteSpace(beastName) &&
               _beastPrices.TryGetValue(beastName, out var priceChaos) && priceChaos > 0
            ? priceChaos
            : 0d;
    }

    private void RegisterCurrentMapReplaySeen(long entityId, string beastName, DateTime now) =>
        AnalyticsReplayEvents.RegisterSeen(entityId, beastName, now);

    private void RegisterCurrentMapReplayCaptured(long entityId, string beastName, DateTime now) =>
        AnalyticsReplayEvents.RegisterCaptured(entityId, beastName, now);

    private MapReplayEvent[] BuildCurrentMapReplayEvents(bool includeInferredMisses) =>
        AnalyticsReplayEvents.BuildReplayEvents(includeInferredMisses);

    private void RegisterSessionRareBeast(Entity entity)
    {
        _sessionBeastsFound++;
        _currentMapBeastsFound++;

        if (!TryGetTrackedBeastNameCached(entity.Metadata, out var beastName))
            return;

        _totalRedBeastsSession++;
        _currentMapRedBeastsFound++;
        _valuableBeastCounts[beastName]++;

        RegisterCurrentMapReplaySeen(entity.Id, beastName, DateTime.UtcNow);

        _currentMapValuableBeastCounts[beastName] =
            _currentMapValuableBeastCounts.TryGetValue(beastName, out var count) ? count + 1 : 1;
    }

    private bool TryGetTrackedBeastNameCached(string metadata, out string beastName)
    {
        beastName = null;
        if (string.IsNullOrWhiteSpace(metadata))
            return false;

        if (_trackedBeastNameCache.TryGetValue(metadata, out var cached))
        {
            if (cached == MissingTrackedBeastName)
                return false;

            beastName = cached;
            return true;
        }

        foreach (var tracked in AllRedBeasts)
        {
            if (tracked.MetadataPatterns.Any(pattern => string.Equals(metadata, pattern, StringComparison.OrdinalIgnoreCase)))
            {
                beastName = tracked.Name;
                _trackedBeastNameCache[metadata] = beastName;
                return true;
            }
        }

        _trackedBeastNameCache[metadata] = MissingTrackedBeastName;
        return false;
    }

    private TimeSpan GetCurrentMapTime(DateTime now)
    {
        var elapsed = _currentMapElapsed;
        if (_isCurrentAreaTrackable && _currentMapStartUtc.HasValue)
            elapsed += now - _currentMapStartUtc.Value;

        return elapsed;
    }

    private TimeSpan GetTotalSessionTime(DateTime now)
    {
        var pause = _sessionPausedDuration + (_pauseMenuSessionStartUtc.HasValue ? now - _pauseMenuSessionStartUtc.Value : TimeSpan.Zero);
        var duration = now - _sessionStartUtc - pause + _loadedSessionsDuration;
        return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
    }

    private void ResetCurrentMapAnalytics()
    {
        _currentMapBeastsFound = 0;
        _currentMapRedBeastsFound = 0;
        _currentMapUsedDuplicatingScarab = false;
        _currentMapFirstRedSeenSeconds = null;
        _currentMapValuableBeastCounts.Clear();
        _currentMapValuableBeastCapturedCounts.Clear();
        _currentMapBeastEncounters.Clear();
        _currentMapCostBreakdown.Clear();
        _currentMapReplayEvents.Clear();
    }

    private void SetPreparedMapCostBreakdown(IEnumerable<MapCostItem> items, bool? usedDuplicatingScarab = null) =>
        AnalyticsMapCosts.SetPreparedMapCostBreakdown(items, usedDuplicatingScarab);

    private void BeginCurrentMapCostTrackingFromPrepared() => AnalyticsMapCosts.BeginCurrentMapCostTrackingFromPrepared();

    private static bool IsDuplicatingScarabItemName(string itemName) =>
        !string.IsNullOrWhiteSpace(itemName) &&
        itemName.IndexOf(BestiaryScarabOfDuplicatingName, StringComparison.OrdinalIgnoreCase) >= 0;

    private double ComputePerMapCostChaos() => AnalyticsMapCosts.ComputePerMapCostChaos();

    private MapCostItem[] ComputePerMapCostBreakdown() => AnalyticsMapCosts.ComputePerMapCostBreakdown();

    private double ComputeCurrentMapCapturedChaos()
        => AnalyticsEngineV2.ComputeCapturedChaos(_currentMapValuableBeastCapturedCounts, _beastPrices);

    private void FinalizeCurrentMapAnalytics(string areaHash, string areaName, DateTime now) =>
        AnalyticsSessionAggregation.FinalizeCurrentMapAnalytics(areaHash, areaName, now);

    private SessionCurrentResponseV2 BuildAnalyticsWebSnapshot(DateTime now) => AnalyticsSnapshots.BuildWebSnapshot(now);

    private (BeastTotalV2[] BeastTotals, FamilyTotalV2[] FamilyTotals) BuildSessionTotals(bool includeCurrentMap)
    {
        var current = includeCurrentMap ? _currentMapValuableBeastCapturedCounts : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return AnalyticsEngineV2.BuildTotals(
            AllRedBeasts.Select(x => x.Name),
            _mapHistory,
            current,
            _beastPrices);
    }

    private MapListResponseV2 BuildMapListResponseV2(int offset, int limit) => AnalyticsSnapshots.BuildMapList(offset, limit);

    private IReadOnlyList<SessionSaveListItemV2> ListSavedSessionsV2() => AnalyticsSessions.ListSavedSessions();

    private SessionSaveDetailV2 GetSavedSessionDataV2(string saveId) => AnalyticsSessions.GetSavedSessionData(saveId);

    private ApiActionResponseV2 SaveSessionSnapshotToFileV2(CreateSessionSaveRequestV2 request) => AnalyticsSessions.SaveSessionSnapshotV2(request);

    private ApiActionResponseV2 LoadSavedSessionV2(string saveId) => AnalyticsSessions.LoadSavedSession(saveId);

    private ApiActionResponseV2 UnloadSavedSessionV2(string saveId) => AnalyticsSessions.UnloadSavedSession(saveId);

    private ApiActionResponseV2 DeleteSavedSessionV2(string saveId) => AnalyticsSessions.DeleteSavedSession(saveId);

    private CompareSessionsResponseV2 CompareSavedSessionsV2(CompareSessionsRequestV2 request) => AnalyticsSessions.CompareSavedSessions(request);

    private void ApplyLoadedSessionAnalytics(SavedSessionDataV2 data)
    {
        if (data == null)
        {
            return;
        }

        _mapHistory.AddRange(data.MapHistory ?? []);
        _mapHistory.Sort((a, b) => b.CompletedAtUtc.CompareTo(a.CompletedAtUtc));
        if (_mapHistory.Count > MaxMapHistoryEntries)
        {
            _mapHistory.RemoveRange(MaxMapHistoryEntries, _mapHistory.Count - MaxMapHistoryEntries);
        }

        _sessionBeastsFound += data.Summary?.BeastsFound ?? 0;
        _totalRedBeastsSession += data.Summary?.RedBeastsFound ?? 0;
        _completedMapCount += data.Summary?.MapsCompleted ?? 0;
    _completedMapsDuration += GetLoadedCompletedMapDuration(data);
        _loadedSessionsDuration += TimeSpan.FromSeconds(data.Summary?.DurationSeconds ?? 0d);

        foreach (var total in data.BeastTotals ?? [])
        {
            if (_valuableBeastCounts.ContainsKey(total.BeastName))
            {
                _valuableBeastCounts[total.BeastName] += total.CapturedCount;
            }
        }
    }

    private void RemoveLoadedSessionAnalytics(SavedSessionDataV2 data)
    {
        if (data == null)
        {
            return;
        }

        AnalyticsEngineV2.RemoveMapRecords(_mapHistory, (data.MapHistory ?? []).Select(x => x.MapId));

        _sessionBeastsFound = Math.Max(0, _sessionBeastsFound - (data.Summary?.BeastsFound ?? 0));
        _totalRedBeastsSession = Math.Max(0, _totalRedBeastsSession - (data.Summary?.RedBeastsFound ?? 0));
        _completedMapCount = Math.Max(0, _completedMapCount - (data.Summary?.MapsCompleted ?? 0));

        var completedMapDuration = GetLoadedCompletedMapDuration(data);
        _completedMapsDuration = _completedMapsDuration > completedMapDuration
            ? _completedMapsDuration - completedMapDuration
            : TimeSpan.Zero;

        var duration = TimeSpan.FromSeconds(data.Summary?.DurationSeconds ?? 0d);
        _loadedSessionsDuration = _loadedSessionsDuration > duration
            ? _loadedSessionsDuration - duration
            : TimeSpan.Zero;

        foreach (var total in data.BeastTotals ?? [])
        {
            if (_valuableBeastCounts.ContainsKey(total.BeastName))
            {
                _valuableBeastCounts[total.BeastName] = Math.Max(0, _valuableBeastCounts[total.BeastName] - total.CapturedCount);
            }
        }
    }

    private static TimeSpan GetLoadedCompletedMapDuration(SavedSessionDataV2 data)
        => TimeSpan.FromSeconds((data?.MapHistory ?? []).Sum(map => Math.Max(0d, map?.DurationSeconds ?? 0d)));

    private void BuildAnalyticsLines(List<string> lines, bool includeBeastBreakdown = true)
    {
        lines.Clear();

        var now = DateTime.UtcNow;
        lines.Add($"Map Time: {BeastsV2Helpers.FormatDuration(GetCurrentMapTime(now))}");
        if (!includeBeastBreakdown)
            return;

        var avg = _completedMapCount > 0
            ? TimeSpan.FromTicks(_completedMapsDuration.Ticks / _completedMapCount)
            : TimeSpan.Zero;

        lines.Add($"Avg Map: {(_completedMapCount > 0 ? BeastsV2Helpers.FormatDuration(avg) : "n/a")} ({_completedMapCount} maps)");
        lines.Add($"Session: {BeastsV2Helpers.FormatDuration(GetTotalSessionTime(now))}");
    }

    private static string NormalizeTag(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
