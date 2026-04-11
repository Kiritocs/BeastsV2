namespace BeastsV2.Runtime.Analytics;

internal sealed class AnalyticsBeastEncounterState
{
    public string BeastName { get; init; } = string.Empty;

    public double FirstSeenSeconds { get; init; }

    public double? CapturedSeconds { get; set; }
}