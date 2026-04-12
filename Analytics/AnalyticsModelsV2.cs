using System;
using System.Collections.Generic;

namespace BeastsV2;

public sealed class MapCostItem
{
    public string ItemName { get; set; } = string.Empty;
    public double UnitPriceChaos { get; set; }
}

public sealed class MapBeastStat
{
    public string BeastName { get; set; } = string.Empty;
    public int Count { get; set; }
    public int CapturedCount { get; set; }
    public double UnitPriceChaos { get; set; }
    public double CapturedChaos => CapturedCount * UnitPriceChaos;
}

public sealed class MapReplayEvent
{
    public string BeastName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public double OffsetSeconds { get; set; }
    public double UnitPriceChaos { get; set; }
}

public sealed class MapAnalyticsRecord
{
    public string MapId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CompletedAtUtc { get; set; }
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

public sealed class SavedSessionTagsV2
{
    public string Strategy { get; set; } = string.Empty;
    public string Scarab { get; set; } = string.Empty;
    public string Atlas { get; set; } = string.Empty;
    public string MapPool { get; set; } = string.Empty;
}

public sealed class SavedSessionSummaryV2
{
    public double DurationSeconds { get; set; }
    public int MapsCompleted { get; set; }
    public int BeastsFound { get; set; }
    public int RedBeastsFound { get; set; }
    public double CapturedChaos { get; set; }
    public double CostChaos { get; set; }
    public double NetChaos { get; set; }
}

public sealed class BeastTotalV2
{
    public string BeastName { get; set; } = string.Empty;
    public int CapturedCount { get; set; }
    public double UnitPriceChaos { get; set; }
    public double CapturedChaos { get; set; }
}

public sealed class FamilyTotalV2
{
    public string FamilyName { get; set; } = string.Empty;
    public int CapturedCount { get; set; }
    public double CapturedChaos { get; set; }
}

public sealed class SavedSessionDataV2
{
    public int SchemaVersion { get; set; } = 2;
    public string SaveId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime SavedAtUtc { get; set; }
    public bool IsAutoSave { get; set; }
    public string Name { get; set; } = string.Empty;
    public SavedSessionTagsV2 Tags { get; set; } = new();
    public SavedSessionSummaryV2 Summary { get; set; } = new();
    public BeastTotalV2[] BeastTotals { get; set; } = [];
    public FamilyTotalV2[] FamilyTotals { get; set; } = [];
    public MapAnalyticsRecord[] MapHistory { get; set; } = [];
    public MapCostItem[] CostDefaults { get; set; } = [];
}
