using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastsV2.Runtime.Analytics;

internal sealed record AnalyticsMapCostTrackingCallbacks(
    Func<IList<MapCostItem>> GetPreparedMapCostBreakdown,
    Func<IList<MapCostItem>> GetCurrentMapCostBreakdown,
    Func<bool> GetPreparedMapUsedDuplicatingScarab,
    Action<bool> SetPreparedMapUsedDuplicatingScarab,
    Func<bool> GetCurrentMapUsedDuplicatingScarab,
    Action<bool> SetCurrentMapUsedDuplicatingScarab,
    Func<double> GetExtraCostPerMapChaos,
    Func<string, bool> IsDuplicatingScarabItemName);

internal sealed class AnalyticsMapCostTrackingService
{
    private readonly AnalyticsMapCostTrackingCallbacks _callbacks;

    public AnalyticsMapCostTrackingService(AnalyticsMapCostTrackingCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void SetPreparedMapCostBreakdown(IEnumerable<MapCostItem> items, bool? usedDuplicatingScarab = null)
    {
        var preparedBreakdown = _callbacks.GetPreparedMapCostBreakdown();
        preparedBreakdown.Clear();
        foreach (var item in AnalyticsEngineV2.CloneCostBreakdown(items))
        {
            preparedBreakdown.Add(item);
        }

        _callbacks.SetPreparedMapUsedDuplicatingScarab(usedDuplicatingScarab ?? preparedBreakdown.Any(x => _callbacks.IsDuplicatingScarabItemName(x?.ItemName)));

        var extra = _callbacks.GetExtraCostPerMapChaos();
        if (extra > 0)
        {
            preparedBreakdown.Add(new MapCostItem { ItemName = "Extra (Manual)", UnitPriceChaos = extra });
        }
    }

    public void BeginCurrentMapCostTrackingFromPrepared()
    {
        var currentBreakdown = _callbacks.GetCurrentMapCostBreakdown();
        currentBreakdown.Clear();
        foreach (var item in AnalyticsEngineV2.CloneCostBreakdown(_callbacks.GetPreparedMapCostBreakdown()))
        {
            currentBreakdown.Add(item);
        }

        _callbacks.SetCurrentMapUsedDuplicatingScarab(
            _callbacks.GetPreparedMapUsedDuplicatingScarab() || currentBreakdown.Any(x => _callbacks.IsDuplicatingScarabItemName(x?.ItemName)));

        _callbacks.GetPreparedMapCostBreakdown().Clear();
        _callbacks.SetPreparedMapUsedDuplicatingScarab(false);
    }

    public double ComputePerMapCostChaos()
    {
        return _callbacks.GetCurrentMapCostBreakdown().Sum(x => x.UnitPriceChaos);
    }

    public MapCostItem[] ComputePerMapCostBreakdown()
    {
        return AnalyticsEngineV2.CloneCostBreakdown(_callbacks.GetCurrentMapCostBreakdown()).ToArray();
    }
}