using System;

namespace BeastsV2;

public sealed class SessionCurrentResponseV2
{
    public DateTime GeneratedAtUtc { get; set; }
    public bool IsCurrentAreaTrackable { get; set; }
    public bool IsPaused { get; set; }
    public string ActiveAreaHash { get; set; } = string.Empty;
    public string ActiveAreaName { get; set; } = string.Empty;

    public double CurrentMapDurationSeconds { get; set; }
    public double AverageMapDurationSeconds { get; set; }
    public double SessionDurationSeconds { get; set; }

    public int CompletedMapCount { get; set; }
    public int SessionBeastsFound { get; set; }
    public int SessionRedBeastsFound { get; set; }
    public int CurrentMapBeastsFound { get; set; }
    public int CurrentMapRedBeastsFound { get; set; }

    public double CurrentMapCapturedChaos { get; set; }
    public double CurrentMapCostChaos { get; set; }
    public double CurrentMapNetChaos { get; set; }
    public bool CurrentMapUsesDuplicatingScarab { get; set; }
    public double? CurrentMapFirstRedSeenSeconds { get; set; }
    public MapCostItem[] CurrentMapCostBreakdown { get; set; } = [];
    public MapReplayEvent[] CurrentMapReplayEvents { get; set; } = [];

    public double SessionCapturedChaos { get; set; }
    public double SessionCostChaos { get; set; }
    public double SessionNetChaos { get; set; }
    public double SessionCapturedPerHourChaos { get; set; }
    public double SessionNetPerHourChaos { get; set; }
    public double AverageCapturedPerMapChaos { get; set; }
    public double AverageNetPerMapChaos { get; set; }

    public RollingStatsV2 Rolling { get; set; } = new();
    public FamilyTotalV2[] FamilyTotals { get; set; } = [];
    public BeastTotalV2[] BeastTotals { get; set; } = [];
    public string[] TrackedBeastNames { get; set; } = [];
}

public sealed class RollingStatsV2
{
    public int WindowMapCount { get; set; }
    public double AvgCapturedChaos { get; set; }
    public double AvgNetChaos { get; set; }
    public double AvgRedsPerMap { get; set; }
    public double AvgDurationSeconds { get; set; }
    public double MedianCapturedChaos { get; set; }
    public double P90CapturedChaos { get; set; }
    public double P95CapturedChaos { get; set; }
    public double VarianceCapturedChaos { get; set; }
    public double StdDevCapturedChaos { get; set; }
    public double BestCapturedChaos { get; set; }
    public double WorstCapturedChaos { get; set; }
    public string BestAreaName { get; set; } = string.Empty;
    public string WorstAreaName { get; set; } = string.Empty;
}

public sealed class MapListResponseV2
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public MapListItemV2[] Items { get; set; } = [];
}

public sealed class MapListItemV2
{
    public string MapId { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public string CompletedAtDisplay { get; set; } = string.Empty;
    public string AreaHash { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int BeastsFound { get; set; }
    public int RedBeastsFound { get; set; }
    public double CapturedChaos { get; set; }
    public double CostChaos { get; set; }
    public double NetChaos { get; set; }
    public bool UsedBestiaryScarabOfDuplicating { get; set; }
    public double? FirstRedSeenSeconds { get; set; }
    public MapBeastStat[] BeastBreakdown { get; set; } = [];
    public MapCostItem[] CostBreakdown { get; set; } = [];
    public MapReplayEvent[] ReplayEvents { get; set; } = [];
}

public sealed class SessionSaveListItemV2
{
    public string SessionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; }
    public string SavedAtDisplay { get; set; } = string.Empty;
    public bool IsAutoSave { get; set; }
    public SavedSessionTagsV2 Tags { get; set; } = new();
    public SavedSessionSummaryV2 Summary { get; set; } = new();
    public bool AlreadyLoaded { get; set; }
}

public sealed class SessionSaveDetailV2
{
    public SavedSessionDataV2 Session { get; set; }
}

public sealed class CreateSessionSaveRequestV2
{
    public string Name { get; set; } = string.Empty;
    public string StrategyTag { get; set; } = string.Empty;
    public string ScarabTag { get; set; } = string.Empty;
    public string AtlasTag { get; set; } = string.Empty;
    public string MapPoolTag { get; set; } = string.Empty;
    public bool IsAutoSave { get; set; }
}

public sealed class CompareSessionsRequestV2
{
    public string SessionAId { get; set; } = string.Empty;
    public string SessionBId { get; set; } = string.Empty;
    public bool MatchAreas { get; set; }
    public int TrimPercent { get; set; }
    public int MinMaps { get; set; } = 30;
}

public sealed class CompareSessionsResponseV2
{
    public bool Success { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool SampleOk { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public CompareSessionMetricsV2 SessionA { get; set; } = new();
    public CompareSessionMetricsV2 SessionB { get; set; } = new();
    public CompareSessionMetricsV2 Delta { get; set; } = new();
}

public sealed class CompareSessionMetricsV2
{
    public int Count { get; set; }
    public double DurationSeconds { get; set; }
    public double CapturedChaos { get; set; }
    public double CostChaos { get; set; }
    public double NetChaos { get; set; }
    public double Reds { get; set; }
    public double NetPerMinuteChaos { get; set; }
    public double CapturedPerMinuteChaos { get; set; }
    public double NetPerMapChaos { get; set; }
    public double CapturedPerMapChaos { get; set; }
    public double CostPerMapChaos { get; set; }
    public double RedsPerMap { get; set; }
}

public sealed class ApiActionResponseV2
{
    public bool Success { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object Details { get; set; }

    public static ApiActionResponseV2 Ok(string code, string message, object details = null)
        => new() { Success = true, Code = code, Message = message, Details = details };

    public static ApiActionResponseV2 Fail(string code, string message, object details = null)
        => new() { Success = false, Code = code, Message = message, Details = details };
}

public sealed class ApiErrorResponseV2
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public object Details { get; set; }
}
