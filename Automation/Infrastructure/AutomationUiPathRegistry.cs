namespace BeastsV2.Automation.Infrastructure;

internal static class AutomationUiPathRegistry
{
    public static int[] BestiaryPanelPath { get; } = [2, 0, 1, 1, 15];
    public static int[] BestiaryCapturedBeastsTabPath { get; } = [2, 0, 1, 1, 15, 0, 18];
    public static int[] BestiarySearchRegexTextPath { get; } = [2, 0, 1, 1, 15, 0, 18, 0, 0, 1, 0];
    public static int[] BestiaryCapturedBeastsButtonContainerPath { get; } = [2, 0, 1, 1, 15, 0, 19];
    public static int[] BestiaryChallengesEntriesRootPath { get; } = [2, 0, 1, 0];
    public static int[] BestiaryChallengesEntryTextPath { get; } = [0, 1];
    public static int[] BestiaryDeleteButtonPathFromBeastRow { get; } = [3];
    public static int[] BestiaryDeleteConfirmationWindowPath { get; } = [0];
    public static int[] BestiaryDeleteConfirmationOkayButtonPath { get; } = [0, 0, 3, 0];
    public static int[] CurrencyShiftClickMenuPath { get; } = [0];
    public static int[] CurrencyShiftClickMenuConfirmButtonPath { get; } = [0, 1];
    public static int[] CurrencyShiftClickMenuQuantityTextPath { get; } = [0, 0, 1];
    public static int[] FragmentStashScarabTabPath { get; } = [2, 0, 0, 1, 1, 1, 0, 5, 0, 1];
    public static int[] MapStashTierOneToNineTabPath { get; } = [2, 0, 0, 1, 1, 3, 0, 0];
    public static int[] MapStashTierTenToSixteenTabPath { get; } = [2, 0, 0, 1, 1, 3, 0, 1];
    public static int[] MapStashPageTabPath { get; } = [2, 0, 0, 1, 1, 3, 0, 3, 0];
    public static int[] MapStashPageNumberPath { get; } = [0, 1];
    public static int[] MapStashPageContentPath { get; } = [2, 0, 0, 1, 1, 3, 0, 4];
}