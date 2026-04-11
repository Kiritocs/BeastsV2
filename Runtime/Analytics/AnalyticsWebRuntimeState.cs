namespace BeastsV2.Runtime.Analytics;

internal sealed class AnalyticsWebRuntimeState
{
    public AnalyticsWebServer Server { get; set; }

    public SessionCurrentResponseV2 LatestSnapshot { get; set; } = new();
}