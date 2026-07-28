using System;
using System.Collections.Generic;
using System.Globalization;
using ExileCore.PoEMemory;

namespace BeastsV2;

public partial class Main
{
    private static readonly int[] TemporaryQuestTextPath = { 4, 0, 0, 0, 0, 0, 1, 0, 1 };
    
    private static Element GetPrimaryQuestEntry(Element questTracker) => GetQuestEntriesContainer(questTracker)?.GetChildAtIndex(0);

    private bool TryGetBeastQuestProgress(out int current, out int total)
    {
        current = 0;
        total = 0;

        foreach (var questText in GetQuestTextCandidates())
        {
            if (TryParseBeastQuestProgress(questText, out current, out total))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> GetQuestTextCandidates()
    {
        var questTracker = GameController?.IngameState?.IngameUi?.QuestTracker;
        if (questTracker != null)
        {
            yield return GetPrimaryQuestText(questTracker);

            var questEntries = GetQuestEntriesContainer(questTracker)?.Children;
            if (questEntries != null)
            {
                foreach (var questEntry in questEntries)
                {
                    if (questEntry?.IsVisible == true)
                    {
                        yield return GetQuestEntryText(questEntry);
                    }
                }
            }
        }

        // Temporary fallback until QuestTracker is available again in Exile.
        var fallbackQuestTextElement = GetTemporaryQuestTextElement();
        if (!string.IsNullOrWhiteSpace(fallbackQuestTextElement?.Text))
        {
            yield return fallbackQuestTextElement.Text;
        }
    }

    private Element GetTemporaryQuestTextElement() =>
        BeastsV2Helpers.GetChildAtIndices(GameController?.IngameState?.IngameUi, TemporaryQuestTextPath);

    private static Element GetQuestEntriesContainer(Element questTracker) => BeastsV2Helpers.GetChildAtIndices(questTracker, 0, 0);

    private static string GetPrimaryQuestText(Element questTracker) =>
        GetVisibleQuestEntryText(GetPrimaryQuestEntry(questTracker));

    private static string GetQuestEntryText(Element questEntry) => BeastsV2Helpers.GetChildAtIndices(questEntry, 0, 1, 0, 1)?.Text;

    private static string GetVisibleQuestEntryText(Element questEntry) =>
        questEntry?.IsVisible == true ? GetQuestEntryText(questEntry) : null;

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
        foreach (var questText in GetQuestTextCandidates())
        {
            if (IsMissionCompleteQuestText(questText)) return true;
        }

        return false;
    }

    private static bool IsMissionCompleteQuestText(string text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Contains("mission complete", StringComparison.OrdinalIgnoreCase);
}

