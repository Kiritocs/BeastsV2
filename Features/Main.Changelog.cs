using System;
using System.Linq;
using ImGuiNET;

namespace BeastsV2;

public partial class Main
{
    private static readonly ChangelogEntry[] Changelog =
    [
        new(2026, 04, 11, 2,
            "Added map-stash regex restock support with a stash setting toggle and regex text field.",
            "When enabled, restock pastes the configured regex into the map-stash search bar and only picks highlighted matching maps.",
            "Fragment and scarab restock behavior remains unchanged."),
        new(2026, 04, 11, 1,
            "Initial public release.",
            "Added real-time Bestiary tracking with world labels, large-map markers, tracked-beast overlays, and completion messages.",
            "Added hideout automation for Bestiary itemizing, beast deletion, stash restocking, Map Device loading, and Faustus listing.",
            "Added analytics overlay and local web dashboard with saved sessions, exports, and strategy comparison tools.")
    ];

    private static readonly ChangelogEntry[] SortedChangelog = Changelog
        .OrderByDescending(entry => entry.SortKey)
        .ToArray();

    private sealed record ChangelogEntry(int Year, int Month, int Day, int Revision, params string[] Changes)
    {
        public int SortKey => (Year * 1000000) + (Month * 10000) + (Day * 100) + Revision;

        public string Version => Revision <= 1
            ? $"{Year:0000}.{Month:00}.{Day:00}"
            : $"{Year:0000}.{Month:00}.{Day:00}-r{Revision}";
    }

    private void DrawChangelogPanel()
    {
        if (SortedChangelog.Length == 0)
        {
            ImGui.TextDisabled("No changelog entries.");
            ImGui.Spacing();
            return;
        }

        for (var i = 0; i < SortedChangelog.Length; i++)
        {
            var entry = SortedChangelog[i];
            if (!ImGui.CollapsingHeader($"{entry.Version}##ChangeLogVersion_{i}"))
            {
                continue;
            }

            foreach (var change in entry.Changes ?? [])
            {
                if (!string.IsNullOrWhiteSpace(change))
                {
                    ImGui.BulletText(change);
                }
            }
        }
    }
}