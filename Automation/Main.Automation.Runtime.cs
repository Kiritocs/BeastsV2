using System.Collections.Generic;
using BeastsV2.Automation.Infrastructure;
using ExileCore.PoEMemory;

namespace BeastsV2;

public partial class Main
{
    private static IReadOnlyList<int> BestiaryPanelPath => AutomationUiPathRegistry.BestiaryPanelPath;
    private static IReadOnlyList<int> BestiaryCapturedBeastsTabPath => AutomationUiPathRegistry.BestiaryCapturedBeastsTabPath;
    private static IReadOnlyList<int> BestiarySearchRegexTextPath => AutomationUiPathRegistry.BestiarySearchRegexTextPath;
    private static IReadOnlyList<int> BestiaryCapturedBeastsButtonContainerPath => AutomationUiPathRegistry.BestiaryCapturedBeastsButtonContainerPath;
    private static IReadOnlyList<int> BestiaryChallengesEntriesRootPath => AutomationUiPathRegistry.BestiaryChallengesEntriesRootPath;
    private static IReadOnlyList<int> BestiaryChallengesEntryTextPath => AutomationUiPathRegistry.BestiaryChallengesEntryTextPath;
    private static IReadOnlyList<int> BestiaryDeleteButtonPathFromBeastRow => AutomationUiPathRegistry.BestiaryDeleteButtonPathFromBeastRow;
    private static IReadOnlyList<int> BestiaryDeleteConfirmationWindowPath => AutomationUiPathRegistry.BestiaryDeleteConfirmationWindowPath;
    private static IReadOnlyList<int> BestiaryDeleteConfirmationOkayButtonPath => AutomationUiPathRegistry.BestiaryDeleteConfirmationOkayButtonPath;
    private static IReadOnlyList<int> CurrencyShiftClickMenuPath => AutomationUiPathRegistry.CurrencyShiftClickMenuPath;
    private static IReadOnlyList<int> CurrencyShiftClickMenuConfirmButtonPath => AutomationUiPathRegistry.CurrencyShiftClickMenuConfirmButtonPath;
    private static IReadOnlyList<int> CurrencyShiftClickMenuQuantityTextPath => AutomationUiPathRegistry.CurrencyShiftClickMenuQuantityTextPath;
    private static IReadOnlyList<int> FragmentStashScarabTabPath => AutomationUiPathRegistry.FragmentStashScarabTabPath;
    private static IReadOnlyList<int> MapStashTierOneToNineTabPath => AutomationUiPathRegistry.MapStashTierOneToNineTabPath;
    private static IReadOnlyList<int> MapStashTierTenToSixteenTabPath => AutomationUiPathRegistry.MapStashTierTenToSixteenTabPath;
    private static IReadOnlyList<int> MapStashPageTabPath => AutomationUiPathRegistry.MapStashPageTabPath;
    private static IReadOnlyList<int> MapStashPageNumberPath => AutomationUiPathRegistry.MapStashPageNumberPath;
    private static IReadOnlyList<int> MapStashPageContentPath => AutomationUiPathRegistry.MapStashPageContentPath;

    private int _lastAutomationFragmentScarabTabIndex
    {
        get => Runtime.State.Automation.UiCache.LastAutomationFragmentScarabTabIndex;
        set => Runtime.State.Automation.UiCache.LastAutomationFragmentScarabTabIndex = value;
    }

    private int _lastAutomationMapStashTierSelection
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashTierSelection;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashTierSelection = value;
    }

    private int _lastAutomationMapStashPageNumber
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashPageNumber;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashPageNumber = value;
    }

    private int _lastAutomationMapStashUiCacheKey
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashUiCacheKey;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashUiCacheKey = value;
    }

    private Element _lastAutomationMapStashTierGroupRoot
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashTierGroupRoot;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashTierGroupRoot = value;
    }

    private Element _lastAutomationMapStashPageTabContainer
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashPageTabContainer;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashPageTabContainer = value;
    }

    private Dictionary<int, Element> _lastAutomationMapStashPageTabsByNumber
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashPageTabsByNumber;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashPageTabsByNumber = value;
    }

    private Element _lastAutomationMapStashPageContentRoot
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashPageContentRoot;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashPageContentRoot = value;
    }

    private string _lastAutomationMapStashPageContentLogSignature
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashPageContentLogSignature;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashPageContentLogSignature = value;
    }

    private string _lastAutomationMapStashPageTabsLogSignature
    {
        get => Runtime.State.Automation.UiCache.LastAutomationMapStashPageTabsLogSignature;
        set => Runtime.State.Automation.UiCache.LastAutomationMapStashPageTabsLogSignature = value;
    }
}