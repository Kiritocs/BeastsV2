using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BeastsV2;

internal static class AnalyticsEngineV2
{
    public static string FormatUserLocalDateTime(DateTime value)
    {
        if (value == DateTime.MinValue)
            return string.Empty;

        var localValue = value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
        return localValue.ToString(CultureInfo.CurrentCulture);
    }

    public static string FormatUserLocalTime(DateTime value)
    {
        if (value == DateTime.MinValue)
            return string.Empty;

        var localValue = value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
        return localValue.ToString("T", CultureInfo.CurrentCulture);
    }

    public static MapListItemV2 BuildMapListItem(MapAnalyticsRecord source)
    {
        if (source == null)
            return null;

        return new MapListItemV2
        {
            MapId = source.MapId,
            CompletedAtUtc = source.CompletedAtUtc,
            CompletedAtDisplay = FormatUserLocalDateTime(source.CompletedAtUtc),
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
            BeastBreakdown = source.BeastBreakdown ?? [],
            CostBreakdown = source.CostBreakdown ?? [],
            ReplayEvents = source.ReplayEvents ?? [],
        };
    }

    public static List<MapCostItem> CloneCostBreakdown(IEnumerable<MapCostItem> items)
    {
        if (items == null)
            return [];

        return items
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ItemName))
            .Select(x => new MapCostItem { ItemName = x.ItemName, UnitPriceChaos = x.UnitPriceChaos })
            .ToList();
    }

    public static List<MapReplayEvent> CloneReplayEvents(IEnumerable<MapReplayEvent> events)
    {
        if (events == null)
            return [];

        return events
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.BeastName) && !string.IsNullOrWhiteSpace(x.EventType))
            .Select(x => new MapReplayEvent
            {
                BeastName = x.BeastName,
                EventType = x.EventType,
                OffsetSeconds = x.OffsetSeconds,
                UnitPriceChaos = x.UnitPriceChaos,
            })
            .ToList();
    }

    public static MapAnalyticsRecord BuildMapRecord(
        DateTime now,
        string areaHash,
        string areaName,
        TimeSpan elapsed,
        int beastsFound,
        int redBeastsFound,
        IReadOnlyDictionary<string, int> beastCounts,
        IReadOnlyDictionary<string, int> capturedCounts,
        IReadOnlyList<MapCostItem> costBreakdown,
        IReadOnlyDictionary<string, float> beastPrices,
        double? firstRedSeenSeconds,
        IReadOnlyList<MapReplayEvent> replayEvents,
        bool usedBestiaryScarabOfDuplicating = false)
    {
        var breakdown = (beastCounts ?? new Dictionary<string, int>())
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var captured = capturedCounts != null && capturedCounts.TryGetValue(x.Key, out var cap) ? cap : 0;
                var unitPrice = beastPrices != null && beastPrices.TryGetValue(x.Key, out var price) && price > 0 ? price : 0f;
                return new MapBeastStat
                {
                    BeastName = x.Key,
                    Count = x.Value,
                    CapturedCount = captured,
                    UnitPriceChaos = unitPrice,
                };
            })
            .ToArray();

        var cost = (costBreakdown ?? []).Sum(x => x.UnitPriceChaos);
        var capturedChaos = breakdown.Sum(x => x.CapturedChaos);

        return new MapAnalyticsRecord
        {
            MapId = Guid.NewGuid().ToString("N"),
            CompletedAtUtc = now,
            AreaHash = areaHash ?? string.Empty,
            AreaName = areaName ?? string.Empty,
            DurationSeconds = Math.Max(0d, elapsed.TotalSeconds),
            BeastsFound = Math.Max(0, beastsFound),
            RedBeastsFound = Math.Max(0, redBeastsFound),
            CapturedChaos = capturedChaos,
            CostChaos = cost,
            NetChaos = capturedChaos - cost,
            UsedBestiaryScarabOfDuplicating = usedBestiaryScarabOfDuplicating,
            FirstRedSeenSeconds = firstRedSeenSeconds,
            BeastBreakdown = breakdown,
            CostBreakdown = (costBreakdown ?? []).ToArray(),
            ReplayEvents = (replayEvents ?? []).ToArray(),
        };
    }

    public static void ApplyMapRecord(List<MapAnalyticsRecord> history, MapAnalyticsRecord record, int maxEntries)
    {
        if (history == null || record == null)
            return;

        history.Insert(0, record);
        if (history.Count > maxEntries)
            history.RemoveRange(maxEntries, history.Count - maxEntries);
    }

    public static bool RemoveMapRecords(List<MapAnalyticsRecord> history, IEnumerable<string> mapIds)
    {
        if (history == null || mapIds == null)
            return false;

        var idSet = new HashSet<string>(mapIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0)
            return false;

        return history.RemoveAll(x => idSet.Contains(x.MapId)) > 0;
    }

    public static double ComputeCapturedChaos(IReadOnlyDictionary<string, int> capturedCounts, IReadOnlyDictionary<string, float> beastPrices)
    {
        if (capturedCounts == null || beastPrices == null)
            return 0d;

        var total = 0d;
        foreach (var (name, count) in capturedCounts)
        {
            if (count <= 0)
                continue;

            if (beastPrices.TryGetValue(name, out var unit) && unit > 0)
                total += count * unit;
        }

        return total;
    }

    public static (BeastTotalV2[] BeastTotals, FamilyTotalV2[] FamilyTotals) BuildTotals(
        IEnumerable<string> trackedBeastNames,
        IReadOnlyList<MapAnalyticsRecord> history,
        IReadOnlyDictionary<string, int> currentCapturedCounts,
        IReadOnlyDictionary<string, float> livePrices)
    {
        var beastNames = new HashSet<string>(trackedBeastNames ?? [], StringComparer.OrdinalIgnoreCase);
        var capturedCounts = beastNames.ToDictionary(x => x, _ => 0, StringComparer.OrdinalIgnoreCase);
        var capturedChaos = beastNames.ToDictionary(x => x, _ => 0d, StringComparer.OrdinalIgnoreCase);
        var unitPrices = beastNames.ToDictionary(
            x => x,
            x => livePrices != null && livePrices.TryGetValue(x, out var price) && price > 0 ? (double)price : 0d,
            StringComparer.OrdinalIgnoreCase);

        foreach (var map in history ?? [])
        {
            foreach (var stat in map?.BeastBreakdown ?? [])
            {
                if (stat == null || !beastNames.Contains(stat.BeastName))
                    continue;

                var captured = Math.Max(0, stat.CapturedCount);
                capturedCounts[stat.BeastName] += captured;
                capturedChaos[stat.BeastName] += captured * Math.Max(0d, stat.UnitPriceChaos);

                if (stat.UnitPriceChaos > 0)
                    unitPrices[stat.BeastName] = stat.UnitPriceChaos;
            }
        }

        foreach (var (name, count) in currentCapturedCounts ?? new Dictionary<string, int>())
        {
            if (count <= 0 || !beastNames.Contains(name))
                continue;

            capturedCounts[name] += count;
            if (livePrices != null && livePrices.TryGetValue(name, out var unit) && unit > 0)
            {
                capturedChaos[name] += count * unit;
                unitPrices[name] = unit;
            }
        }

        var beastTotals = beastNames
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(name => new BeastTotalV2
            {
                BeastName = name,
                CapturedCount = capturedCounts[name],
                UnitPriceChaos = unitPrices[name],
                CapturedChaos = capturedChaos[name],
            })
            .ToArray();

        var familyTotals = beastTotals
            .GroupBy(x => BeastsV2BeastData.GetBeastFamily(x.BeastName), StringComparer.OrdinalIgnoreCase)
            .Select(g => new FamilyTotalV2
            {
                FamilyName = g.Key,
                CapturedCount = g.Sum(x => x.CapturedCount),
                CapturedChaos = g.Sum(x => x.CapturedChaos),
            })
            .OrderByDescending(x => x.CapturedChaos)
            .ThenBy(x => x.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (beastTotals, familyTotals);
    }

    public static RollingStatsV2 BuildRollingStats(IReadOnlyList<MapAnalyticsRecord> history, int window)
    {
        var slice = (history ?? [])
            .OrderByDescending(x => x.CompletedAtUtc)
            .Take(Math.Max(1, window))
            .ToArray();

        if (slice.Length == 0)
            return new RollingStatsV2();

        var capturedValues = slice.Select(x => x.CapturedChaos).OrderBy(x => x).ToArray();
        var avgCaptured = slice.Average(x => x.CapturedChaos);
        var variance = slice.Select(x =>
        {
            var diff = x.CapturedChaos - avgCaptured;
            return diff * diff;
        }).Average();

        var best = slice.OrderByDescending(x => x.CapturedChaos).First();
        var worst = slice.OrderBy(x => x.CapturedChaos).First();

        return new RollingStatsV2
        {
            WindowMapCount = slice.Length,
            AvgCapturedChaos = avgCaptured,
            AvgNetChaos = slice.Average(x => x.NetChaos),
            AvgRedsPerMap = slice.Average(x => x.RedBeastsFound),
            AvgDurationSeconds = slice.Average(x => x.DurationSeconds),
            MedianCapturedChaos = Percentile(capturedValues, 0.5d),
            P90CapturedChaos = Percentile(capturedValues, 0.9d),
            P95CapturedChaos = Percentile(capturedValues, 0.95d),
            VarianceCapturedChaos = variance,
            StdDevCapturedChaos = Math.Sqrt(variance),
            BestCapturedChaos = best.CapturedChaos,
            WorstCapturedChaos = worst.CapturedChaos,
            BestAreaName = best.AreaName ?? string.Empty,
            WorstAreaName = worst.AreaName ?? string.Empty,
        };
    }

    public static CompareSessionsResponseV2 CompareSessions(
        SavedSessionDataV2 aData,
        SavedSessionDataV2 bData,
        CompareSessionsRequestV2 request)
    {
        if (aData == null || bData == null)
            return new CompareSessionsResponseV2 { Success = false, Code = "not_found", Message = "Session not found." };

        var aMaps = (aData.MapHistory ?? []).ToList();
        var bMaps = (bData.MapHistory ?? []).ToList();

        if (request?.MatchAreas == true)
        {
            var aAreas = new HashSet<string>(aMaps.Select(GetAreaKey), StringComparer.OrdinalIgnoreCase);
            var bAreas = new HashSet<string>(bMaps.Select(GetAreaKey), StringComparer.OrdinalIgnoreCase);
            aMaps = aMaps.Where(x => bAreas.Contains(GetAreaKey(x))).ToList();
            bMaps = bMaps.Where(x => aAreas.Contains(GetAreaKey(x))).ToList();
        }

        var trimPercent = Math.Clamp(request?.TrimPercent ?? 0, 0, 45);
        if (trimPercent > 0)
        {
            aMaps = TrimMapsByNet(aMaps, trimPercent);
            bMaps = TrimMapsByNet(bMaps, trimPercent);
        }

        var a = BuildCompareMetrics(aMaps);
        var b = BuildCompareMetrics(bMaps);
        var delta = new CompareSessionMetricsV2
        {
            Count = b.Count - a.Count,
            DurationSeconds = b.DurationSeconds - a.DurationSeconds,
            CapturedChaos = b.CapturedChaos - a.CapturedChaos,
            CostChaos = b.CostChaos - a.CostChaos,
            NetChaos = b.NetChaos - a.NetChaos,
            Reds = b.Reds - a.Reds,
            NetPerMinuteChaos = b.NetPerMinuteChaos - a.NetPerMinuteChaos,
            CapturedPerMinuteChaos = b.CapturedPerMinuteChaos - a.CapturedPerMinuteChaos,
            NetPerMapChaos = b.NetPerMapChaos - a.NetPerMapChaos,
            CapturedPerMapChaos = b.CapturedPerMapChaos - a.CapturedPerMapChaos,
            CostPerMapChaos = b.CostPerMapChaos - a.CostPerMapChaos,
            RedsPerMap = b.RedsPerMap - a.RedsPerMap,
        };

        var minMaps = Math.Max(1, request?.MinMaps ?? 30);
        var sampleOk = a.Count >= minMaps && b.Count >= minMaps;
        var winner = delta.NetPerMinuteChaos >= 0 ? "B" : "A";

        return new CompareSessionsResponseV2
        {
            Success = true,
            Code = "ok",
            Message = sampleOk
                ? "Comparison complete."
                : $"Low sample size. Need at least {minMaps} maps per bucket.",
            SampleOk = sampleOk,
            Recommendation = $"Bucket {winner} has better net/min.",
            SessionA = a,
            SessionB = b,
            Delta = delta,
        };
    }

    private static CompareSessionMetricsV2 BuildCompareMetrics(IReadOnlyList<MapAnalyticsRecord> maps)
    {
        var count = maps?.Count ?? 0;
        var duration = maps?.Sum(x => x.DurationSeconds) ?? 0d;
        var captured = maps?.Sum(x => x.CapturedChaos) ?? 0d;
        var cost = maps?.Sum(x => x.CostChaos) ?? 0d;
        var net = maps?.Sum(x => x.NetChaos) ?? 0d;
        var reds = maps?.Sum(x => x.RedBeastsFound) ?? 0d;
        var minutes = duration / 60d;

        return new CompareSessionMetricsV2
        {
            Count = count,
            DurationSeconds = duration,
            CapturedChaos = captured,
            CostChaos = cost,
            NetChaos = net,
            Reds = reds,
            NetPerMinuteChaos = minutes > 0 ? net / minutes : 0d,
            CapturedPerMinuteChaos = minutes > 0 ? captured / minutes : 0d,
            NetPerMapChaos = count > 0 ? net / count : 0d,
            CapturedPerMapChaos = count > 0 ? captured / count : 0d,
            CostPerMapChaos = count > 0 ? cost / count : 0d,
            RedsPerMap = count > 0 ? reds / count : 0d,
        };
    }

    private static List<MapAnalyticsRecord> TrimMapsByNet(IReadOnlyList<MapAnalyticsRecord> maps, int trimPercent)
    {
        if (maps == null || maps.Count == 0 || trimPercent <= 0)
            return maps?.ToList() ?? [];

        var cut = (int)Math.Floor(maps.Count * trimPercent / 100d);
        if (cut <= 0)
            return maps.ToList();

        var sorted = maps.OrderBy(x => x.NetChaos).ToArray();
        var start = Math.Min(cut, sorted.Length - 1);
        var end = Math.Max(start + 1, sorted.Length - cut);

        return sorted
            .Skip(start)
            .Take(end - start)
            .ToList();
    }

    private static string GetAreaKey(MapAnalyticsRecord map)
    {
        var area = map?.AreaName;
        if (!string.IsNullOrWhiteSpace(area))
            return area.Trim().ToLowerInvariant();

        return (map?.AreaHash ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0)
            return 0d;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var rank = percentile * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
            return sortedValues[lower];

        var weight = rank - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * weight;
    }

    public static string BuildSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "auto";

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-');

        var raw = new string(chars.ToArray());
        while (raw.Contains("--", StringComparison.Ordinal))
            raw = raw.Replace("--", "-", StringComparison.Ordinal);

        return raw.Trim('-') is { Length: > 0 } slug ? slug : "auto";
    }

    public static string BuildSessionFileName(DateTime savedAtUtc, string slug)
        => $"{savedAtUtc.ToLocalTime():yyyy-MM-dd_HH-mm-ss}-{slug}.json";

    public static string BuildSessionDisplayName(string name, DateTime nowUtc, bool isAutoSave = false)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        return isAutoSave ? "AutoSave" : "Session";
    }
}
