using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace BeastsV2;

public partial class Main
{
    private void DrawTalismanPickerPanel()
    {
        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices == null)
        {
            ImGui.TextDisabled("Talisman settings unavailable.");
            return;
        }

        if (!talismanPrices.Enable.Value)
        {
            ImGui.TextDisabled("Talisman price tracking is turned off. Enable it above to fetch prices.");
        }

        ImGui.Text($"Prices as of: {Settings.BeastPrices.LastUpdated}");
        ImGui.SameLine();
        ImGui.TextDisabled($"({BeastsV2TalismanData.AllTalismans.Length} talismans)");
        ImGui.Separator();

        if (!ImGui.BeginTable("##TalismanPickerTable", 5,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, 400)))
            return;

        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 24);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Beast", ImGuiTableColumnFlags.WidthStretch, 1.1f);
        ImGui.TableSetupColumn("Talisman", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Implicit", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var enabledTalismans = talismanPrices.EnabledTalismans;
        var trackedBeasts = Settings.BeastPrices.EnabledBeasts;

        foreach (var talisman in _sortedTalismansByPrice)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var isEnabled = enabledTalismans.Contains(talisman.TalismanName);
            if (ImGui.Checkbox($"##{talisman.TalismanName}_chk", ref isEnabled))
            {
                if (isEnabled) enabledTalismans.Add(talisman.TalismanName);
                else enabledTalismans.Remove(talisman.TalismanName);

                SavePersistedBeastPriceSettings();
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(TryGetTalismanPriceText(talisman.BeastName, out var priceText) ? priceText : "?");

            ImGui.TableNextColumn();
            if (isEnabled)
                DrawColoredTextUnformatted(EnabledBeastTextColor, talisman.BeastName);
            else
                DrawDisabledTextUnformatted(talisman.BeastName);

            // A selected talisman does nothing unless its beast is also tracked, so make that visible
            // rather than silently doing nothing.
            if (isEnabled && !trackedBeasts.Contains(talisman.BeastName))
            {
                ImGui.SameLine();
                DrawColoredTextUnformatted(SummaryWarnColor, "(!)");
                if (ImGui.IsItemHovered())
                {
                    DrawTooltipUnformatted(
                        $"{talisman.BeastName} is not in your Tracked Beasts list.\n" +
                        "This talisman's price will not show on overlays and the beast is left out of the Bestiary regex.\n\n" +
                        "Use \"Track Beasts For Selection\" to add it.");
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(talisman.TalismanName);
            if (ImGui.IsItemHovered())
            {
                DrawTooltipUnformatted(talisman.DropLevel > 0
                    ? $"{talisman.TalismanName}\nDrop level {talisman.DropLevel} (also the level needed to equip it)\n\n{talisman.Implicit}"
                    : $"{talisman.TalismanName}\nNo drop level restriction\n\n{talisman.Implicit}");
            }

            ImGui.TableNextColumn();
            DrawDisabledTextUnformatted(talisman.Implicit);
            if (ImGui.IsItemHovered())
            {
                DrawTooltipUnformatted(talisman.Implicit);
            }
        }

        ImGui.EndTable();

        DrawUniqueTalismanTable(talismanPrices);
    }

    private void DrawUniqueTalismanTable(TalismanPricesSettings talismanPrices)
    {
        ImGui.Spacing();
        DrawSectionLabel("Unique talismans",
            "These do not drop from the beast matching their base type. They drop from the four Spirit Beasts, or from the Beastcrafting recipe that creates a new Talisman, which requires The Black Morrigan.");

        if (!ImGui.BeginTable("##UniqueTalismanTable", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV |
                ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 24);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Unique", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Base", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableHeadersRow();

        var enabledUniques = talismanPrices.EnabledUniqueTalismans;
        var sourceCount = BeastsV2TalismanData.UniqueTalismanSourceBeasts.Length;
        var trackedSourceCount = CountTrackedUniqueSourceBeasts();

        foreach (var unique in _sortedUniqueTalismansByPrice)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var isEnabled = enabledUniques.Contains(unique.Name);
            if (ImGui.Checkbox($"##{unique.Name}_uchk", ref isEnabled))
            {
                if (isEnabled) enabledUniques.Add(unique.Name);
                else enabledUniques.Remove(unique.Name);

                SavePersistedBeastPriceSettings();
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(TryGetUniqueTalismanPriceText(unique.Name, out var priceText) ? priceText : "?");

            ImGui.TableNextColumn();
            if (isEnabled)
                DrawColoredTextUnformatted(EnabledBeastTextColor, unique.Name);
            else
                DrawDisabledTextUnformatted(unique.Name);

            // A unique can come from any Spirit Beast, so coverage is partial rather than on/off:
            // show how many of the sources are tracked whenever it is not all of them.
            if (isEnabled && trackedSourceCount < sourceCount)
            {
                ImGui.SameLine();
                DrawColoredTextUnformatted(SummaryWarnColor,
                    trackedSourceCount == 0 ? "(!)" : $"({trackedSourceCount}/{sourceCount})");
                if (ImGui.IsItemHovered())
                {
                    DrawTooltipUnformatted(BuildUniqueSourceCoverageTooltip(trackedSourceCount, sourceCount));
                }
            }

            ImGui.TableNextColumn();
            DrawDisabledTextUnformatted(unique.BaseTypeName);
            if (ImGui.IsItemHovered())
            {
                DrawTooltipUnformatted(
                    $"{unique.Name}\nBase: {unique.BaseTypeName}\nRequires Level {unique.RequiredLevel}\n\n" +
                    "Drops from Spirit Beasts, or from the Beastcrafting recipe requiring The Black Morrigan.");
            }
        }

        ImGui.EndTable();
    }

    // ImGui.Text/TextColored/TextDisabled/SetTooltip treat their argument as a printf format string.
    // Talisman implicits contain '%' (for example "+15% to Quality of all Skill Gems"), which the CRT
    // parses as a conversion specifier and aborts the process on. Always render this data unformatted.
    private static void DrawColoredTextUnformatted(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text ?? string.Empty);
        ImGui.PopStyleColor();
    }

    private static void DrawDisabledTextUnformatted(string text)
    {
        DrawColoredTextUnformatted(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], text);
    }

    private static void DrawTooltipUnformatted(string text)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text ?? string.Empty);
        ImGui.EndTooltip();
    }

    private void DrawTalismanPricesSummaryPanel()
    {
        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices == null)
        {
            ImGui.TextDisabled("Talisman settings unavailable.");
            return;
        }

        var pricedCount = _talismanPriceTextsByBeast.Count;

        DrawHeroBanner(
            "TalismanBanner",
            "TALISMANS",
            "Talisman Prices",
            "Each beast is paired with the talisman it drops, priced from the poe.ninja base-type feed.",
            ("Tracking", talismanPrices.Enable.Value ? "On" : "Off", GetStateColor(talismanPrices.Enable.Value)),
            ("Tracked talismans", talismanPrices.EnabledTalismans.Count.ToString(CultureInfo.InvariantCulture), SummaryAccentColor),
            ("Priced", $"{pricedCount}/{BeastsV2TalismanData.AllTalismans.Length}", pricedCount > 0 ? SummaryOkColor : SummaryWarnColor));

        DrawSectionLabel("Most valuable talismans", "The highest-priced talismans from the most recent refresh.");
        if (ImGui.BeginTable("##BeastsV2TalismanSummary", 2, SummaryTableFlags))
        {
            var top = _sortedTalismansByPrice
                .Where(t => _talismanPriceTextsByBeast.ContainsKey(t.BeastName))
                .Take(5)
                .ToArray();

            if (top.Length == 0)
            {
                DrawSummaryRow("No prices yet", "Refresh prices to populate", SummaryMutedColor);
            }
            else
            {
                foreach (var talisman in top)
                {
                    DrawSummaryRow(talisman.TalismanName, _talismanPriceTextsByBeast[talisman.BeastName]);
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        DrawActionButtonsRow(
            "TalismanActions",
            ("Refresh Prices", QueuePriceFetch),
            ("Track Beasts For Selection", TrackBeastsForSelectedTalismans),
            ("Select 15c+", SelectTalismansWorth15ChaosOrMore),
            ("Select All", SelectAllTalismans),
            ("Clear Selection", DeselectAllTalismans));

        var untrackedCount = CountSelectedTalismansWithUntrackedBeasts();
        var sourceCount = BeastsV2TalismanData.UniqueTalismanSourceBeasts.Length;
        var trackedSourceCount = CountTrackedUniqueSourceBeasts();
        var hasSelectedUniques = talismanPrices.EnabledUniqueTalismans.Count > 0;

        var warnings = new List<string>();
        if (untrackedCount > 0)
        {
            warnings.Add($"{untrackedCount} selected talisman(s) belong to beasts that are not tracked, so nothing will show for them.");
        }

        if (hasSelectedUniques && trackedSourceCount < sourceCount)
        {
            warnings.Add(trackedSourceCount == 0
                ? $"None of the {sourceCount} Spirit Beasts that drop unique talismans are tracked, so your selected uniques will never show."
                : $"Only {trackedSourceCount} of the {sourceCount} Spirit Beasts that drop unique talismans are tracked, so your selected uniques are only partly covered.");
        }

        if (warnings.Count > 0)
        {
            DrawHintCallout("TalismanUntrackedHint", "Action needed",
                $"{string.Join(" ", warnings)} Press \"Track Beasts For Selection\" to add the missing beasts.",
                SummaryWarnColor);
        }

        DrawHintCallout("TalismanHint", "Pricing note", "Prices use uninfluenced bases with the deepest listing count, so they reflect what a dropped talisman actually sells for rather than an influenced high-roll.", SummaryAccentColor);
    }

    /// <summary>
    /// Returns the beast price text used everywhere a beast price is displayed - world and large-map
    /// labels, the tracked beasts window, and the Tracked Beasts picker - with the associated talisman
    /// price appended when combining is enabled and that talisman is tracked.
    /// </summary>
    private string GetBeastDisplayPriceText(string beastName)
    {
        _beastPriceTexts.TryGetValue(beastName ?? string.Empty, out var beastPriceText);

        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices?.Enable.Value != true || !talismanPrices.CombineWithBeastPrice.Value)
        {
            return beastPriceText;
        }

        if (!BeastsV2TalismanData.TryGetByBeast(beastName, out var talisman) ||
            !talismanPrices.EnabledTalismans.Contains(talisman.TalismanName) ||
            !TryGetTalismanPriceText(beastName, out var talismanPriceText))
        {
            return beastPriceText;
        }

        return string.IsNullOrEmpty(beastPriceText)
            ? $"+{talismanPriceText}"
            : $"{beastPriceText} +{talismanPriceText}";
    }

    private void SelectAllTalismans()
    {
        SetEnabledTalismans(_ => true);
    }

    private void DeselectAllTalismans()
    {
        SetEnabledTalismans(_ => false);
    }

    private void SelectTalismansWorth15ChaosOrMore()
    {
        SetEnabledTalismans(talisman =>
            _talismanPricesByBeast.TryGetValue(talisman.BeastName, out var price) && price >= 15f);
    }

    /// <summary>
    /// Counts selected base talismans whose beast is not tracked, and therefore show nothing.
    /// </summary>
    private int CountSelectedTalismansWithUntrackedBeasts()
    {
        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices == null)
        {
            return 0;
        }

        var trackedBeasts = Settings.BeastPrices.EnabledBeasts;
        return BeastsV2TalismanData.AllTalismans.Count(t =>
            talismanPrices.EnabledTalismans.Contains(t.TalismanName) &&
            !trackedBeasts.Contains(t.BeastName));
    }

    private int CountTrackedUniqueSourceBeasts()
    {
        var trackedBeasts = Settings?.BeastPrices?.EnabledBeasts;
        return trackedBeasts == null
            ? 0
            : BeastsV2TalismanData.UniqueTalismanSourceBeasts.Count(trackedBeasts.Contains);
    }

    private string BuildUniqueSourceCoverageTooltip(int trackedSourceCount, int sourceCount)
    {
        var trackedBeasts = Settings.BeastPrices.EnabledBeasts;
        var untracked = BeastsV2TalismanData.UniqueTalismanSourceBeasts
            .Where(b => !trackedBeasts.Contains(b))
            .ToArray();

        var header = trackedSourceCount == 0
            ? $"None of the {sourceCount} Spirit Beasts that drop this are tracked, so it will never show up."
            : $"Only {trackedSourceCount} of the {sourceCount} Spirit Beasts that drop this are tracked, so you are covering part of its sources.";

        return $"{header}\n\nNot tracked:\n  {string.Join("\n  ", untracked)}\n\n" +
               "Use \"Track Beasts For Selection\" to add them.";
    }

    /// <summary>
    /// Adds every beast that drops a selected talisman to the tracked beasts list. Explicit rather
    /// than automatic, so selecting a talisman never silently rewrites the beast list curated in the
    /// Price Data panel.
    /// </summary>
    private void TrackBeastsForSelectedTalismans()
    {
        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices == null)
        {
            return;
        }

        var trackedBeasts = Settings.BeastPrices.EnabledBeasts;
        var addedBeasts = 0;

        foreach (var talisman in BeastsV2TalismanData.AllTalismans)
        {
            if (talismanPrices.EnabledTalismans.Contains(talisman.TalismanName) &&
                trackedBeasts.Add(talisman.BeastName))
            {
                addedBeasts++;
            }
        }

        // Any selected unique can come from any Spirit Beast, so there is no single beast to add -
        // add all of its possible sources.
        if (talismanPrices.EnabledUniqueTalismans.Count > 0)
        {
            foreach (var beastName in BeastsV2TalismanData.UniqueTalismanSourceBeasts)
            {
                if (trackedBeasts.Add(beastName))
                {
                    addedBeasts++;
                }
            }
        }

        SavePersistedBeastPriceSettings();
        Log(addedBeasts > 0
            ? $"Added {addedBeasts} beast(s) to Tracked Beasts for your selected talismans."
            : "All beasts for your selected talismans are already tracked.");
    }

    private void SetEnabledTalismans(Func<TalismanInfo, bool> predicate)
    {
        var enabledTalismans = Settings.TalismanPrices.EnabledTalismans;
        enabledTalismans.Clear();

        if (predicate != null)
        {
            enabledTalismans.UnionWith(
                BeastsV2TalismanData.AllTalismans.Where(predicate).Select(x => x.TalismanName));
        }

        SavePersistedBeastPriceSettings();
    }
}
