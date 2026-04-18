using System.Linq;
using ExileCore.PoEMemory.MemoryObjects;

namespace BeastsV2;

public partial class Main
{
    private void GetOverlayVisibility(out bool shouldRenderCounterAndMessage, out bool shouldRenderAnalytics)
    {
        shouldRenderCounterAndMessage = false;
        shouldRenderAnalytics = false;

        var ingameUi = GameController?.IngameState?.IngameUi;
        if (ingameUi == null) return;

        var visibility = Settings.Visibility;
        var isHideoutLikeArea = IsHideoutLikeArea(GameController.Area?.CurrentArea);
        var isInMirage = IsinMirage();
        var counterWindow = Settings.CounterWindow;
        if (counterWindow.CompletedStyle.ShowWhileNotComplete.Value ||
            counterWindow.CompletedMessage.ShowWhileNotComplete.Value ||
            counterWindow.TrackedCompletionMessage.ShowWhileNotComplete.Value)
        {
            shouldRenderCounterAndMessage = !isInMirage;
            shouldRenderAnalytics = !visibility.HideAnalyticsInHideout.Value || !isHideoutLikeArea;
            return;
        }

        if (visibility.HideOnFullscreenPanels.Value && ingameUi.FullscreenPanels.Any(p => p.IsVisible)) return;

        shouldRenderAnalytics = !IsConfiguredSidePanelOpen(
            ingameUi,
            visibility.HideAnalyticsOnOpenLeftPanel.Value,
            visibility.HideAnalyticsOnOpenRightPanel.Value);

        if (visibility.HideAnalyticsInHideout.Value && isHideoutLikeArea)
        {
            shouldRenderAnalytics = false;
        }

        if (visibility.HideInHideout.Value && isHideoutLikeArea) return;

        shouldRenderCounterAndMessage = !isInMirage && !IsConfiguredSidePanelOpen(
            ingameUi,
            visibility.HideOnOpenLeftPanel.Value,
            visibility.HideOnOpenRightPanel.Value);
    }

    private static bool IsConfiguredSidePanelOpen(IngameUIElements ingameUi, bool checkLeft, bool checkRight) =>
        checkLeft && ingameUi.OpenLeftPanel?.IsVisible == true ||
        checkRight && ingameUi.OpenRightPanel?.IsVisible == true;

    private bool IsBestiaryTabVisible() => IsBestiaryCapturedBeastsTabVisible();
}

