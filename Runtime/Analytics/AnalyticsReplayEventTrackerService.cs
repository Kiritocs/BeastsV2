using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2.Runtime.Analytics;

internal sealed record AnalyticsReplayEventTrackerCallbacks(
    Func<bool> GetIsCurrentAreaTrackable,
    Func<DateTime, double> GetCurrentMapReplayOffsetSeconds,
    Func<string, double> GetTrackedBeastUnitPriceChaos,
    Func<IDictionary<long, AnalyticsBeastEncounterState>> GetCurrentMapBeastEncounters,
    Func<IList<MapReplayEvent>> GetCurrentMapReplayEvents,
    Func<double?> GetCurrentMapFirstRedSeenSeconds,
    Action<double?> SetCurrentMapFirstRedSeenSeconds,
    Func<TimeSpan> GetCurrentMapElapsed,
    Func<string, int> GetReplayEventSortOrder);

internal sealed class AnalyticsReplayEventTrackerService
{
    private readonly AnalyticsReplayEventTrackerCallbacks _callbacks;

    public AnalyticsReplayEventTrackerService(AnalyticsReplayEventTrackerCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void RegisterSeen(long entityId, string beastName, DateTime now)
    {
        if (!_callbacks.GetIsCurrentAreaTrackable() || entityId <= 0 || string.IsNullOrWhiteSpace(beastName))
        {
            return;
        }

        var encounters = _callbacks.GetCurrentMapBeastEncounters();
        if (encounters.ContainsKey(entityId))
        {
            return;
        }

        var offsetSeconds = _callbacks.GetCurrentMapReplayOffsetSeconds(now);
        encounters[entityId] = new AnalyticsBeastEncounterState
        {
            BeastName = beastName,
            FirstSeenSeconds = offsetSeconds,
        };

        if (!_callbacks.GetCurrentMapFirstRedSeenSeconds().HasValue)
        {
            _callbacks.SetCurrentMapFirstRedSeenSeconds(offsetSeconds);
        }

        _callbacks.GetCurrentMapReplayEvents().Add(new MapReplayEvent
        {
            BeastName = beastName,
            EventType = "seen",
            OffsetSeconds = offsetSeconds,
            UnitPriceChaos = _callbacks.GetTrackedBeastUnitPriceChaos(beastName),
        });
    }

    public void RegisterCaptured(long entityId, string beastName, DateTime now)
    {
        if (!_callbacks.GetIsCurrentAreaTrackable() || entityId <= 0 || string.IsNullOrWhiteSpace(beastName))
        {
            return;
        }

        var encounters = _callbacks.GetCurrentMapBeastEncounters();
        if (!encounters.TryGetValue(entityId, out var encounter))
        {
            RegisterSeen(entityId, beastName, now);
            if (!encounters.TryGetValue(entityId, out encounter))
            {
                return;
            }
        }

        if (encounter.CapturedSeconds.HasValue)
        {
            return;
        }

        var offsetSeconds = _callbacks.GetCurrentMapReplayOffsetSeconds(now);
        encounter.CapturedSeconds = offsetSeconds;
        _callbacks.GetCurrentMapReplayEvents().Add(new MapReplayEvent
        {
            BeastName = beastName,
            EventType = "captured",
            OffsetSeconds = offsetSeconds,
            UnitPriceChaos = _callbacks.GetTrackedBeastUnitPriceChaos(beastName),
        });
    }

    public MapReplayEvent[] BuildReplayEvents(bool includeInferredMisses)
    {
        var replayEvents = AnalyticsEngineV2.CloneReplayEvents(_callbacks.GetCurrentMapReplayEvents());
        if (includeInferredMisses)
        {
            var finalOffsetSeconds = Math.Max(0d, _callbacks.GetCurrentMapElapsed().TotalSeconds);
            foreach (var encounter in _callbacks.GetCurrentMapBeastEncounters().Values)
            {
                if (encounter.CapturedSeconds.HasValue)
                {
                    continue;
                }

                replayEvents.Add(new MapReplayEvent
                {
                    BeastName = encounter.BeastName,
                    EventType = "missed",
                    OffsetSeconds = Math.Max(finalOffsetSeconds, encounter.FirstSeenSeconds),
                    UnitPriceChaos = _callbacks.GetTrackedBeastUnitPriceChaos(encounter.BeastName),
                });
            }
        }

        return replayEvents
            .OrderBy(x => x.OffsetSeconds)
            .ThenBy(x => _callbacks.GetReplayEventSortOrder(x.EventType))
            .ThenBy(x => x.BeastName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}