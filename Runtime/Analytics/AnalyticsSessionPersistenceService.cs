using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2.Runtime.Analytics;

internal sealed record AnalyticsSessionPersistenceCallbacks(
    Func<DateTime, CreateSessionSaveRequestV2, SavedSessionDataV2> BuildSavedSessionData,
    Func<DateTime, SavedSessionDataV2> BuildCurrentLiveSessionData,
    Func<SessionStoreV2> GetSessionStore,
    Func<SessionStoreV2> GetAutoSaveSessionStore,
    Func<HashSet<string>> GetLoadedSessionIds,
    Func<Dictionary<string, SavedSessionDataV2>> GetLoadedSessionCacheById,
    Action<SavedSessionDataV2> ApplyLoadedSessionData,
    Action<SavedSessionDataV2> RemoveLoadedSessionData);

internal sealed class AnalyticsSessionPersistenceService
{
    private readonly AnalyticsSessionPersistenceCallbacks _callbacks;

    public AnalyticsSessionPersistenceService(AnalyticsSessionPersistenceCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public bool SaveSessionSnapshot(CreateSessionSaveRequestV2 request)
    {
        try
        {
            var now = DateTime.UtcNow;
            var data = _callbacks.BuildSavedSessionData(now, request);
            return GetStoreForRequest(request).Save(data, data.Name, out _);
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<SessionSaveListItemV2> ListSavedSessions()
    {
        var items = new List<SessionSaveListItemV2>();
        var loadedSessionIds = _callbacks.GetLoadedSessionIds();
        foreach (var entry in ReadAllSessions())
        {
            var data = entry.Data;
            items.Add(new SessionSaveListItemV2
            {
                SessionId = data.SessionId,
                Name = data.Name ?? string.Empty,
                SavedAtUtc = data.SavedAtUtc,
                SavedAtDisplay = AnalyticsEngineV2.FormatUserLocalDateTime(data.SavedAtUtc),
                IsAutoSave = data.IsAutoSave,
                Tags = data.Tags ?? new SavedSessionTagsV2(),
                Summary = data.Summary ?? new SavedSessionSummaryV2(),
                AlreadyLoaded = loadedSessionIds.Contains(data.SessionId),
            });
        }

        return items;
    }

    public SessionSaveDetailV2 GetSavedSessionData(string sessionId)
    {
        var entry = ReadBySessionId(sessionId);
        return entry?.Data == null ? null : new SessionSaveDetailV2 { Session = entry.Data };
    }

    public ApiActionResponseV2 SaveSessionSnapshotV2(CreateSessionSaveRequestV2 request)
    {
        var success = SaveSessionSnapshot(request);
        return success
            ? ApiActionResponseV2.Ok("saved", "Session saved.")
            : ApiActionResponseV2.Fail("save_failed", "Failed to save session.");
    }

    public ApiActionResponseV2 LoadSavedSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return ApiActionResponseV2.Fail("invalid_id", "sessionId is required.");
        }

        var loadedSessionIds = _callbacks.GetLoadedSessionIds();
        if (loadedSessionIds.Contains(sessionId))
        {
            return ApiActionResponseV2.Fail("duplicate", "Session is already loaded.");
        }

        var entry = ReadBySessionId(sessionId);
        if (entry?.Data == null)
        {
            return ApiActionResponseV2.Fail("not_found", "Session not found.");
        }

        var data = entry.Data;
        var now = DateTime.UtcNow;
        var duplicateCheck = ValidateLoadAgainstExistingSessions(now, data);
        if (duplicateCheck != null)
        {
            return duplicateCheck;
        }

        _callbacks.ApplyLoadedSessionData(data);

        loadedSessionIds.Add(data.SessionId);
        _callbacks.GetLoadedSessionCacheById()[data.SessionId] = data;

        return ApiActionResponseV2.Ok("loaded", "Session loaded.");
    }

    public ApiActionResponseV2 UnloadSavedSession(string sessionId)
    {
        var loadedSessionIds = _callbacks.GetLoadedSessionIds();
        if (!loadedSessionIds.Contains(sessionId))
        {
            return ApiActionResponseV2.Fail("not_loaded", "Session is not loaded.");
        }

        var cache = _callbacks.GetLoadedSessionCacheById();
        if (!cache.TryGetValue(sessionId, out var data))
        {
            return ApiActionResponseV2.Fail("missing_cache", "Loaded session cache is missing.");
        }

        _callbacks.RemoveLoadedSessionData(data);

        loadedSessionIds.Remove(sessionId);
        cache.Remove(sessionId);

        return ApiActionResponseV2.Ok("unloaded", "Session unloaded.");
    }

    public ApiActionResponseV2 DeleteSavedSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return ApiActionResponseV2.Fail("invalid_id", "sessionId is required.");
        }

        if (_callbacks.GetLoadedSessionIds().Contains(sessionId))
        {
            var unload = UnloadSavedSession(sessionId);
            if (!unload.Success)
            {
                return unload;
            }
        }

        return DeleteBySessionId(sessionId)
            ? ApiActionResponseV2.Ok("deleted", "Session deleted.")
            : ApiActionResponseV2.Fail("delete_failed", "Failed to delete session.");
    }

    public CompareSessionsResponseV2 CompareSavedSessions(CompareSessionsRequestV2 request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.SessionAId) || string.IsNullOrWhiteSpace(request.SessionBId))
        {
            return new CompareSessionsResponseV2
            {
                Success = false,
                Code = "invalid_request",
                Message = "sessionAId and sessionBId are required.",
            };
        }

        var a = ReadBySessionId(request.SessionAId)?.Data;
        var b = ReadBySessionId(request.SessionBId)?.Data;
        return AnalyticsEngineV2.CompareSessions(a, b, request);
    }

    private SessionStoreV2 GetStoreForRequest(CreateSessionSaveRequestV2 request)
        => request?.IsAutoSave == true
            ? _callbacks.GetAutoSaveSessionStore() ?? _callbacks.GetSessionStore()
            : _callbacks.GetSessionStore();

    private IEnumerable<SessionStoreV2> GetAllStores()
    {
        var primary = _callbacks.GetSessionStore();
        if (primary != null)
            yield return primary;

        var autoSave = _callbacks.GetAutoSaveSessionStore();
        if (autoSave != null && !string.Equals(autoSave.DirectoryPath, primary?.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            yield return autoSave;
    }

    private IReadOnlyList<SessionFileEntryV2> ReadAllSessions()
        => GetAllStores()
            .SelectMany(store => store.ReadAll())
            .GroupBy(entry => entry.Data.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.Data.SavedAtUtc).First())
            .OrderByDescending(entry => entry.Data.SavedAtUtc)
            .ToArray();

    private SessionFileEntryV2 ReadBySessionId(string sessionId)
    {
        foreach (var store in GetAllStores())
        {
            var entry = store.ReadBySessionId(sessionId);
            if (entry != null)
                return entry;
        }

        return null;
    }

    private bool DeleteBySessionId(string sessionId)
    {
        foreach (var store in GetAllStores())
        {
            if (store.DeleteBySessionId(sessionId))
                return true;
        }

        return false;
    }

    private ApiActionResponseV2 ValidateLoadAgainstExistingSessions(DateTime now, SavedSessionDataV2 candidate)
    {
        var currentLive = _callbacks.BuildCurrentLiveSessionData(now);
        if (HaveOverlappingMapIds(candidate, currentLive) || AreEquivalentSessionContents(candidate, currentLive))
        {
            return ApiActionResponseV2.Fail("matches_current", "Session matches current live analytics state.");
        }

        foreach (var loaded in _callbacks.GetLoadedSessionCacheById().Values)
        {
            if (loaded == null)
                continue;

            if (AreEquivalentSessionContents(candidate, loaded))
            {
                return ApiActionResponseV2.Fail("duplicate_content", "Matching session data is already loaded.");
            }

            if (HaveOverlappingMapIds(candidate, loaded))
            {
                return ApiActionResponseV2.Fail("overlapping_maps", "Session shares map records with an already loaded session.");
            }
        }

        return null;
    }

    private static bool HaveOverlappingMapIds(SavedSessionDataV2 a, SavedSessionDataV2 b)
    {
        var aIds = (a?.MapHistory ?? [])
            .Select(map => map?.MapId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (aIds.Count == 0)
            return false;

        return (b?.MapHistory ?? [])
            .Select(map => map?.MapId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Any(aIds.Contains);
    }

    private static bool AreEquivalentSessionContents(SavedSessionDataV2 a, SavedSessionDataV2 b)
    {
        if (a == null || b == null)
            return false;

        var aSummary = a.Summary ?? new SavedSessionSummaryV2();
        var bSummary = b.Summary ?? new SavedSessionSummaryV2();
        if (aSummary.MapsCompleted != bSummary.MapsCompleted ||
            aSummary.BeastsFound != bSummary.BeastsFound ||
            aSummary.RedBeastsFound != bSummary.RedBeastsFound ||
            !DoubleEquals(aSummary.CapturedChaos, bSummary.CapturedChaos) ||
            !DoubleEquals(aSummary.CostChaos, bSummary.CostChaos) ||
            !DoubleEquals(aSummary.NetChaos, bSummary.NetChaos))
        {
            return false;
        }

        if (!SequenceEqualByKey(a.BeastTotals, b.BeastTotals, x => x?.BeastName, BeastTotalsEquivalent))
            return false;

        if (!SequenceEqualByKey(a.FamilyTotals, b.FamilyTotals, x => x?.FamilyName, FamilyTotalsEquivalent))
            return false;

        if (!SequenceEqualByKey(a.MapHistory, b.MapHistory, x => x?.MapId, MapRecordsEquivalent))
            return false;

        return SequenceEqualByKey(a.CostDefaults, b.CostDefaults, x => x?.ItemName, CostItemsEquivalent);
    }

    private static bool SequenceEqualByKey<T>(IEnumerable<T> a, IEnumerable<T> b, Func<T, string> keySelector, Func<T, T, bool> comparer)
    {
        var left = (a ?? Enumerable.Empty<T>())
            .OrderBy(item => keySelector(item) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var right = (b ?? Enumerable.Empty<T>())
            .OrderBy(item => keySelector(item) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!comparer(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static bool BeastTotalsEquivalent(BeastTotalV2 a, BeastTotalV2 b)
        => string.Equals(a?.BeastName, b?.BeastName, StringComparison.OrdinalIgnoreCase) &&
           (a?.CapturedCount ?? 0) == (b?.CapturedCount ?? 0) &&
           DoubleEquals(a?.UnitPriceChaos ?? 0d, b?.UnitPriceChaos ?? 0d) &&
           DoubleEquals(a?.CapturedChaos ?? 0d, b?.CapturedChaos ?? 0d);

    private static bool FamilyTotalsEquivalent(FamilyTotalV2 a, FamilyTotalV2 b)
        => string.Equals(a?.FamilyName, b?.FamilyName, StringComparison.OrdinalIgnoreCase) &&
           (a?.CapturedCount ?? 0) == (b?.CapturedCount ?? 0) &&
           DoubleEquals(a?.CapturedChaos ?? 0d, b?.CapturedChaos ?? 0d);

    private static bool CostItemsEquivalent(MapCostItem a, MapCostItem b)
        => string.Equals(a?.ItemName, b?.ItemName, StringComparison.OrdinalIgnoreCase) &&
           DoubleEquals(a?.UnitPriceChaos ?? 0d, b?.UnitPriceChaos ?? 0d);

    private static bool MapRecordsEquivalent(MapAnalyticsRecord a, MapAnalyticsRecord b)
    {
        if (!string.Equals(a?.MapId, b?.MapId, StringComparison.OrdinalIgnoreCase) ||
            (a?.CompletedAtUtc ?? DateTime.MinValue) != (b?.CompletedAtUtc ?? DateTime.MinValue) ||
            !string.Equals(a?.AreaHash, b?.AreaHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(a?.AreaName, b?.AreaName, StringComparison.OrdinalIgnoreCase) ||
            !DoubleEquals(a?.DurationSeconds ?? 0d, b?.DurationSeconds ?? 0d) ||
            (a?.BeastsFound ?? 0) != (b?.BeastsFound ?? 0) ||
            (a?.RedBeastsFound ?? 0) != (b?.RedBeastsFound ?? 0) ||
            !DoubleEquals(a?.CapturedChaos ?? 0d, b?.CapturedChaos ?? 0d) ||
            !DoubleEquals(a?.CostChaos ?? 0d, b?.CostChaos ?? 0d) ||
            !DoubleEquals(a?.NetChaos ?? 0d, b?.NetChaos ?? 0d) ||
            (a?.UsedBestiaryScarabOfDuplicating ?? false) != (b?.UsedBestiaryScarabOfDuplicating ?? false) ||
            !DoubleEquals(a?.FirstRedSeenSeconds ?? 0d, b?.FirstRedSeenSeconds ?? 0d))
        {
            return false;
        }

        return SequenceEqualByKey(a?.BeastBreakdown, b?.BeastBreakdown, x => x?.BeastName, MapBeastStatsEquivalent) &&
               SequenceEqualByKey(a?.CostBreakdown, b?.CostBreakdown, x => x?.ItemName, CostItemsEquivalent) &&
               SequenceEqualByKey(a?.ReplayEvents, b?.ReplayEvents, x => $"{x?.EventType}|{x?.BeastName}|{x?.OffsetSeconds}|{x?.UnitPriceChaos}", ReplayEventsEquivalent);
    }

    private static bool MapBeastStatsEquivalent(MapBeastStat a, MapBeastStat b)
        => string.Equals(a?.BeastName, b?.BeastName, StringComparison.OrdinalIgnoreCase) &&
           (a?.Count ?? 0) == (b?.Count ?? 0) &&
           (a?.CapturedCount ?? 0) == (b?.CapturedCount ?? 0) &&
           DoubleEquals(a?.UnitPriceChaos ?? 0d, b?.UnitPriceChaos ?? 0d);

    private static bool ReplayEventsEquivalent(MapReplayEvent a, MapReplayEvent b)
        => string.Equals(a?.BeastName, b?.BeastName, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(a?.EventType, b?.EventType, StringComparison.OrdinalIgnoreCase) &&
           DoubleEquals(a?.OffsetSeconds ?? 0d, b?.OffsetSeconds ?? 0d) &&
           DoubleEquals(a?.UnitPriceChaos ?? 0d, b?.UnitPriceChaos ?? 0d);

    private static bool DoubleEquals(double a, double b)
        => Math.Abs(a - b) < 0.0001d;
}