using System;
using System.Linq;
using ImGuiNET;

namespace BeastsV2;

public partial class Main
{
    private static readonly ChangelogEntry[] Changelog =
    [
        new(2026, 04, 18, 2,
            "Automation -> Stash & Map Device -> Atlas Map Selection now uses a dropdown list of Atlas maps instead of manual typing.",
            "Map Device Atlas map selection now uses AtlasNodes directly, so it no longer relies on hover discovery scans."),
        new(2026, 04, 18, 1,
            "Bestiary tracking now matches beasts using full metadata paths.",
            "Counter Window and beast map overlays now stay hidden inside Mirage maps."),
        new(2026, 04, 15, 1,
            "Automation -> Timing -> Lock User Input During Automation should no longer leave your mouse feeling slow after automation finishes.",
            "Automation -> Stash & Map Device restock is now better at finding and opening your stash.",
            "Automation -> Stash & Map Device restock is now better at finding and opening your map device.",
            "Automation is now better at clicking crowded hideout labels when several things are stacked on top of each other.",
            "Automation is now better at clicking hideout objects while your character is still finishing a move.",
            "Applying Bestiary Regex should now be more reliable."),
        new(2026, 04, 12, 6,
            "Analytics Web Server -> Map History, Saved Sessions, and Price Feed times now all follow Windows regional time settings so analytics timestamps stay consistent everywhere.",
            "Analytics Web Server -> Saved Sessions now keeps SessionId stable for current plugin run, gives each saved file its own SaveId for load/unload/delete/export/compare actions, and requires explicit SaveId metadata.",
            "Analytics Web Server -> Saved Sessions autosaves now replace older matching autosaves using map history plus stable summary counts, so map-complete and plugin-close autosaves no longer stack duplicate data when only live prices changed.",
            "Analytics -> Reset Session now starts fresh analytics SessionId and unloads any loaded saved sessions so reset returns tracker to clean new run and Saved Sessions matches live in-memory totals immediately."),
        new(2026, 04, 12, 5,
            "Counter Window -> Tracked Completion Message now finishes when Einhar quest text shows Mission Complete even if the quest progress line was never parsed first.",
            "Tracked completion now stays visible after plugin reloads inside an already-finished Einhar map and still stays visible when re-entering the same completed instance."),
        new(2026, 04, 12, 4,
            "Reduced idle render cost by throttling Analytics Web Server snapshot rebuilds to once per second under Analytics Web Server and by skipping large-map overlay setup while the Tab map is closed.",
            "Large-map beast labels now only allocate overlay draw work when the map is actually visible and there is something to draw.",
            "Counter/completion state is now computed once per render, Bestiary clipboard and price overlays now bail out unless the Challenges panel is visible, and hidden Map Device cost polling is throttled so hideout idling no longer re-reads Map Device state every frame.",
            "Overlays -> Visibility now separates Hide Counter & Message In Hideout from Hide Analytics In Hideout, so analytics can stay visible in hideout even when the counter and completion message are hidden."),
        new(2026, 04, 12, 3,
            "Added optional soft input locking during automation under Automation -> Timing -> Lock User Input During Automation so user mouse movement, clicks, scrolls, and unrelated key presses are suppressed while a run is active.",
            "Automation trigger hotkeys still pass through during the lock so an active run can still be stopped cleanly.",
            "Soft input locking now also temporarily whitelists Beasts V2's own key and mouse events so chat travel commands, Enter presses, and regex paste actions stay reliable while the lock is active."),
        new(2026, 04, 12, 2,
            "Bestiary startup now keeps inventory open during initial UI cleanup.",
            "Verbose automation diagnostics now only show in ExileApi logs when Diagnostics: Verbose Logging is enabled, while normal info/error logs still appear and all logs still append to BeastsV2.log."),
        new(2026, 04, 12, 1,
            "Added map-stash regex restock support under Automation -> Stash & Map Device -> Enable Map Regex Filter and Map Regex Pattern.",
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