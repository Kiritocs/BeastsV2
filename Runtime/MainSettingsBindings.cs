using System;

namespace BeastsV2.Runtime;

internal sealed record MainSettingsBindingTargets(
    Action ResetSessionAnalytics,
    Action SaveSessionToFile,
    Action ResetMapAverageAnalytics,
    Action CopyAnalyticsWebServerUrlToClipboard,
    Action OpenAnalyticsWebServerInBrowser,
    Action DrawSettingsOverviewPanel,
    Action DrawChangelogPanel,
    Action QueuePriceFetch,
    Action SelectAllPriceDataBeasts,
    Action DeselectAllPriceDataBeasts,
    Action SelectPriceDataBeastsWorth15ChaosOrMore,
    Action DrawBeastPricesSummaryPanel,
    Action DrawBeastPickerPanel,
    Action DrawStashAutomationSummaryPanel,
    Action DrawBestiaryAutomationSummaryPanel,
    Action DrawMerchantAutomationSummaryPanel,
    Action DrawFullSequenceAutomationSummaryPanel,
    Action DrawExcludedEntityPathsListPanel,
    Action RequestExplorationRouteRegen,
    Action BindAutomationSettingsUi);

internal static class MainSettingsBindings
{
    public static void Bind(Settings settings, MainSettingsBindingTargets targets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(targets);

        var analyticsWindow = settings.AnalyticsWindow;
        analyticsWindow.ResetSession.OnPressed = targets.ResetSessionAnalytics;
        analyticsWindow.SaveSessionToFile.OnPressed = targets.SaveSessionToFile;
        analyticsWindow.ResetMapAverage.OnPressed = targets.ResetMapAverageAnalytics;

        var analyticsWebServer = settings.AnalyticsWebServer;
        analyticsWebServer.CopyUrl.OnPressed = targets.CopyAnalyticsWebServerUrlToClipboard;
        analyticsWebServer.OpenInBrowser.OnPressed = targets.OpenAnalyticsWebServerInBrowser;

        settings.Overview.SetupSummaryPanel.DrawDelegate = targets.DrawSettingsOverviewPanel;
        settings.Changelog.UpdateHistoryPanel.DrawDelegate = targets.DrawChangelogPanel;

        var beastPrices = settings.BeastPrices;
        beastPrices.FetchPrices.OnPressed = targets.QueuePriceFetch;
        beastPrices.SelectAllBeasts.OnPressed = targets.SelectAllPriceDataBeasts;
        beastPrices.DeselectAllBeasts.OnPressed = targets.DeselectAllPriceDataBeasts;
        beastPrices.SelectBeastsWorth15ChaosOrMore.OnPressed = targets.SelectPriceDataBeastsWorth15ChaosOrMore;
        beastPrices.SummaryPanel.DrawDelegate = targets.DrawBeastPricesSummaryPanel;
        beastPrices.BeastPickerPanel.DrawDelegate = targets.DrawBeastPickerPanel;

        settings.StashAutomation.SetupSummaryPanel.DrawDelegate = targets.DrawStashAutomationSummaryPanel;
        settings.BestiaryAutomation.SetupSummaryPanel.DrawDelegate = targets.DrawBestiaryAutomationSummaryPanel;
        settings.MerchantAutomation.SetupSummaryPanel.DrawDelegate = targets.DrawMerchantAutomationSummaryPanel;
        settings.FullSequenceAutomation.SetupSummaryPanel.DrawDelegate = targets.DrawFullSequenceAutomationSummaryPanel;

        settings.MapRender.ExplorationRoute.ExcludedEntityPathsList.DrawDelegate = targets.DrawExcludedEntityPathsListPanel;
        settings.MapRender.ExplorationRoute.RecalculateExplorationRoute.OnPressed = targets.RequestExplorationRouteRegen;

        targets.BindAutomationSettingsUi();
    }
}