using System;
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

            // Selecting a talisman is enough to show its beast; call out that this does not make the
            // beast a capture target, since that is the difference from the Tracked Beasts list.
            if (isEnabled && IsTalismanOnlyTrackedBeast(talisman.BeastName))
            {
                ImGui.SameLine();
                DrawColoredTextUnformatted(SummaryAccentColor, "(kill only)");
                if (ImGui.IsItemHovered())
                {
                    DrawTooltipUnformatted(
                        $"{talisman.BeastName} is shown on overlays because you selected its talisman, " +
                        "using the talisman-only colors under Tracking: Markers & Prices.\n\n" +
                        "It is not in Tracked Beasts, so it stays out of the Bestiary regex, the tracked-completion " +
                        "check, and analytics. Tick it in Tracking: Price Data if you also want to capture it.");
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

            // A unique can come from any Spirit Beast, so selecting one shows all four sources rather
            // than a single beast.
            if (isEnabled)
            {
                ImGui.SameLine();
                DrawColoredTextUnformatted(SummaryAccentColor, "(Spirit Beasts)");
                if (ImGui.IsItemHovered())
                {
                    DrawTooltipUnformatted(BuildUniqueSourceTooltip());
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
            ("Select 15c+", SelectTalismansWorth15ChaosOrMore),
            ("Select All", SelectAllTalismans),
            ("Clear Selection", DeselectAllTalismans));

        var killOnlyCount = CountTalismanOnlyBeasts();
        if (killOnlyCount > 0)
        {
            DrawHintCallout("TalismanKillOnlyHint", "Kill-only beasts",
                $"{killOnlyCount} beast(s) are shown purely for their selected talisman. They use the talisman-only colors " +
                "under Tracking: Markers & Prices and are left out of the Bestiary regex, tracked completion, and analytics. " +
                "Tick a beast in Tracking: Price Data if you also want to capture it.",
                SummaryAccentColor);
        }

        DrawHintCallout("TalismanHint", "Pricing note", "Prices use uninfluenced bases with the deepest listing count, so they reflect what a dropped talisman actually sells for rather than an influenced high-roll.", SummaryAccentColor);
    }

    /// <summary>
    /// True when this beast drops something the user selected in the talisman lists: either its own
    /// talisman base, or - for the four Spirit Beasts - any selected unique talisman.
    /// </summary>
    private bool IsTalismanSelectedForBeast(string beastName)
    {
        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices?.Enable.Value != true || string.IsNullOrEmpty(beastName))
        {
            return false;
        }

        if (BeastsV2TalismanData.TryGetByBeast(beastName, out var talisman) &&
            talismanPrices.EnabledTalismans.Contains(talisman.TalismanName))
        {
            return true;
        }

        return talismanPrices.EnabledUniqueTalismans.Count > 0 &&
               BeastsV2TalismanData.IsUniqueTalismanSourceBeast(beastName);
    }

    /// <summary>
    /// A beast that is only interesting because of the talisman it drops: its talisman is selected but
    /// the beast itself is not in Tracked Beasts. It is drawn on overlays so it can be killed for the
    /// drop, yet it deliberately stays out of everything that treats a beast as capture loot - the
    /// Bestiary regex, the tracked-completion check, and analytics.
    /// </summary>
    private bool IsTalismanOnlyTrackedBeast(string beastName)
    {
        return !string.IsNullOrEmpty(beastName) &&
               !Settings.BeastPrices.EnabledBeasts.Contains(beastName) &&
               IsTalismanSelectedForBeast(beastName);
    }

    /// <summary>
    /// Whether a beast should be drawn while Show Enabled Only is on. Wider than Tracked Beasts, since
    /// talisman-only beasts are worth seeing without being worth capturing.
    /// </summary>
    private bool IsBeastShownWhileTrackedOnly(string beastName)
    {
        return Settings.BeastPrices.EnabledBeasts.Contains(beastName) ||
               IsTalismanSelectedForBeast(beastName);
    }

    /// <summary>
    /// Returns the beast price text used everywhere a beast price is displayed - world and large-map
    /// labels, the tracked beasts window, and the Tracked Beasts picker - with the associated talisman
    /// price appended when that talisman is tracked and either combining is enabled or the beast is
    /// only shown because of its talisman, where the talisman price is the whole point of the label.
    /// </summary>
    private string GetBeastDisplayPriceText(string beastName)
    {
        _beastPriceTexts.TryGetValue(beastName ?? string.Empty, out var beastPriceText);

        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices?.Enable.Value != true)
        {
            return beastPriceText;
        }

        if (!talismanPrices.CombineWithBeastPrice.Value && !IsTalismanOnlyTrackedBeast(beastName))
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
    /// Counts the beasts that are on overlays purely because of a selected talisman, so the summary can
    /// say how many are kill-only rather than capture targets.
    /// </summary>
    private int CountTalismanOnlyBeasts()
    {
        var talismanPrices = Settings?.TalismanPrices;
        if (talismanPrices?.Enable.Value != true)
        {
            return 0;
        }

        var trackedBeasts = Settings.BeastPrices.EnabledBeasts;
        var count = BeastsV2TalismanData.AllTalismans.Count(t =>
            talismanPrices.EnabledTalismans.Contains(t.TalismanName) &&
            !trackedBeasts.Contains(t.BeastName));

        if (talismanPrices.EnabledUniqueTalismans.Count > 0)
        {
            // Spirit Beasts already counted through their own talisman above must not count twice.
            count += BeastsV2TalismanData.UniqueTalismanSourceBeasts.Count(beastName =>
                !trackedBeasts.Contains(beastName) &&
                !(BeastsV2TalismanData.TryGetByBeast(beastName, out var talisman) &&
                  talismanPrices.EnabledTalismans.Contains(talisman.TalismanName)));
        }

        return count;
    }

    private static string BuildUniqueSourceTooltip()
    {
        return "Selecting this shows all four Spirit Beasts that can drop it:\n  " +
               string.Join("\n  ", BeastsV2TalismanData.UniqueTalismanSourceBeasts) +
               "\n\nThey are shown for the drop only. Tick them in Tracking: Price Data if you also want " +
               "them treated as capture targets.";
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
