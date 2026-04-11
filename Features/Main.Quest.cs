using System;
using System.Globalization;
using ExileCore.PoEMemory;

namespace BeastsV2;

public partial class Main
{
    private bool TryGetBeastQuestProgress(out int current, out int total)
    {
        current = 0;
        total = 0;

        var questTracker = GameController?.IngameState?.IngameUi?.QuestTracker;
        if (questTracker == null)
        {
            return false;
        }

        if (TryParseBeastQuestProgress(GetPrimaryQuestText(questTracker), out current, out total))
        {
            return true;
        }

        var questEntries = GetQuestEntriesContainer(questTracker)?.Children;
        if (questEntries == null)
        {
            return false;
        }

        foreach (var questEntry in questEntries)
        {
            if (questEntry?.IsVisible != true)
            {
                continue;
            }

            if (TryParseBeastQuestProgress(GetQuestEntryText(questEntry), out current, out total))
            {
                return true;
            }
        }

        return false;
    }

    private static Element GetQuestEntriesContainer(Element questTracker) => BeastsV2Helpers.GetChildAtIndices(questTracker, 0, 0);

    private static string GetPrimaryQuestText(Element questTracker) =>
        GetQuestEntryText(GetQuestEntriesContainer(questTracker)?.GetChildAtIndex(0));

    private static string GetQuestEntryText(Element questEntry) => BeastsV2Helpers.GetChildAtIndices(questEntry, 0, 1, 0, 1)?.Text;

    private static bool TryParseBeastQuestProgress(string questText, out int current, out int total)
    {
        current = 0;
        total = 0;

        if (string.IsNullOrWhiteSpace(questText) ||
            !questText.Contains("beast", StringComparison.OrdinalIgnoreCase) &&
            !questText.Contains("einhar", StringComparison.OrdinalIgnoreCase))
            return false;

        var match = QuestProgressRegex.Match(questText);
        if (!match.Success) return false;

        current = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        total = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return true;
    }

    internal bool IsBeastQuestMissionComplete()
    {
        var questTracker = GameController?.IngameState?.IngameUi?.QuestTracker;
        if (questTracker == null) return false;

        if (IsMissionCompleteQuestText(GetPrimaryQuestText(questTracker))) return true;

        var questEntries = GetQuestEntriesContainer(questTracker)?.Children;
        if (questEntries == null) return false;

        foreach (var questEntry in questEntries)
        {
            if (questEntry?.IsVisible != true) continue;
            if (IsMissionCompleteQuestText(GetQuestEntryText(questEntry))) return true;
        }

        return false;
    }

    private static bool IsMissionCompleteQuestText(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Contains("mission complete", StringComparison.OrdinalIgnoreCase);
}

