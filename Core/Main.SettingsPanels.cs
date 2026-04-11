using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace BeastsV2;

public partial class Main
{
    private static readonly Vector4 SummaryAccentColor = new(0.95f, 0.74f, 0.26f, 1f);
    private static readonly Vector4 SummaryOkColor = new(0.47f, 0.90f, 0.56f, 1f);
    private static readonly Vector4 SummaryWarnColor = new(0.99f, 0.77f, 0.28f, 1f);
    private static readonly Vector4 SummaryAlertColor = new(0.95f, 0.36f, 0.33f, 1f);
    private static readonly Vector4 SummaryMutedColor = new(0.63f, 0.66f, 0.72f, 1f);
    private static readonly Vector4 SummaryHeroBackgroundColor = new(0.10f, 0.08f, 0.07f, 0.97f);
    private static readonly Vector4 SummaryHeroBorderColor = new(0.68f, 0.47f, 0.16f, 1f);
    private static readonly Vector4 SummaryHeroGlowColor = new(0.36f, 0.24f, 0.08f, 0.90f);
    private static readonly Vector4 SummaryCardBackgroundColor = new(0.09f, 0.10f, 0.12f, 0.88f);
    private static readonly Vector4 SummaryCardBorderColor = new(0.45f, 0.34f, 0.17f, 1f);
    private static readonly Vector4 SummaryActionButtonColor = new(0.23f, 0.18f, 0.08f, 0.96f);
    private static readonly Vector4 SummaryActionButtonHoverColor = new(0.31f, 0.24f, 0.10f, 0.98f);
    private static readonly Vector4 SummaryActionButtonActiveColor = new(0.41f, 0.31f, 0.12f, 1f);
    private const ImGuiTableFlags SummaryTableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg;
    private const ImGuiTableFlags SummaryListTableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg;
    private const ImGuiTableFlags SummaryCardTableFlags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings;

    private void DrawSettingsOverviewPanel()
    {
        var settings = Settings;
        if (settings == null)
        {
            ImGui.TextDisabled("Settings unavailable.");
            return;
        }

        var stashReady = CountConfiguredTargetTabs(settings.StashAutomation) >= CountEnabledTargets(settings.StashAutomation);
        var merchantReady = !string.IsNullOrWhiteSpace(settings.MerchantAutomation.SelectedFaustusShopTabName?.Value);

        DrawHeroBanner(
            "OverviewHeroBanner",
            "OVERVIEW",
            "Beasts V2 Overview",
            "Review your core automation, pricing, and overlay configuration from one summary panel.",
            ("State", settings.Enable.Value ? "Live" : "Offline", GetStateColor(settings.Enable.Value)),
            ("Tracked beasts", settings.BeastPrices.EnabledBeasts.Count.ToString(CultureInfo.InvariantCulture), SummaryAccentColor),
            ("Map-device tabs", FormatCoverageSummary(settings.StashAutomation), stashReady ? SummaryOkColor : SummaryWarnColor),
            ("Faustus sell tab", FormatText(settings.MerchantAutomation.SelectedFaustusShopTabName?.Value, "Missing"), merchantReady ? SummaryOkColor : SummaryWarnColor));

        DrawSectionLabel("Supporting details", "Additional plugin-wide values that are not already shown in the banner.");
        if (ImGui.BeginTable("##BeastsV2DashboardSummary", 2, SummaryTableFlags))
        {
            DrawSummaryRow("Configured league", FormatText(settings.BeastPrices.League?.Value, "Not set"));
            DrawSummaryRow("Overlay tracking mode", settings.MapRender.ShowEnabledOnly.Value ? "Only tracked beasts" : "All rare beasts", GetStateColor(settings.MapRender.ShowEnabledOnly.Value));
            DrawSummaryRow("Price refresh cadence", FormatRefreshInterval(settings.BeastPrices.AutoRefreshMinutes.Value));
            DrawSummaryRow("Configured atlas map", FormatMapSelectionSummary(settings.StashAutomation));
            DrawSummaryRow("Active map-device slots", CountEnabledTargets(settings.StashAutomation).ToString(CultureInfo.InvariantCulture));
            DrawSummaryRow("Full-sequence hotkey", FormatHotkey(settings.FullSequenceAutomation.FullSequenceHotkey));
            ImGui.EndTable();
        }

        ImGui.Spacing();
        DrawActionButtonsRow(
            "OverviewActions",
            ("Refresh Prices", QueuePriceFetch),
            ("Copy Web URL", CopyAnalyticsWebServerUrlToClipboard),
            ("Open Web Dashboard", OpenAnalyticsWebServerInBrowser));

        DrawHintCallout("OverviewHint", "Live editing", "Open stash, Bestiary, or Faustus in-game to populate live selectors and verify the summary cards against current UI state.", SummaryAccentColor);
    }

    private void DrawBeastPricesSummaryPanel()
    {
        var prices = Settings?.BeastPrices;
        if (prices == null)
        {
            ImGui.TextDisabled("Price settings unavailable.");
            return;
        }

        DrawHeroBanner(
            "PriceBanner",
            "PRICE DATA",
            "Price Feed",
            "Tracked beasts drive overlays, analytics, and the generated Bestiary search regex.",
            ("Current league", FormatText(prices.League?.Value, "Not set"), SummaryAccentColor),
            ("Tracked beasts", prices.EnabledBeasts.Count.ToString(CultureInfo.InvariantCulture), SummaryAccentColor),
            ("Last price update", FormatText(prices.LastUpdated, "never"), SummaryAccentColor));

        DrawSectionLabel("Supporting details", "Refresh cadence and overlay dependencies that are not already shown in the banner.");
        if (ImGui.BeginTable("##BeastsV2PriceSummary", 2, SummaryTableFlags))
        {
            DrawSummaryRow("Price refresh cadence", FormatRefreshInterval(prices.AutoRefreshMinutes.Value));
            DrawSummaryRow("Overlay tracking mode", Settings.MapRender.ShowEnabledOnly.Value ? "Only tracked beasts" : "All rare beasts", GetStateColor(Settings.MapRender.ShowEnabledOnly.Value));
            ImGui.EndTable();
        }

        ImGui.Spacing();
        DrawActionButtonsRow(
            "PriceActions",
            ("Refresh Prices", QueuePriceFetch),
            ("Select 15c+", SelectPriceDataBeastsWorth15ChaosOrMore),
            ("Select All", SelectAllPriceDataBeasts),
            ("Clear Selection", DeselectAllPriceDataBeasts));

        DrawHintCallout("PriceHint", "Selection note", "Use the tracked-beast editor below for precise curation after broad actions like 15c+ or Select All.", SummaryAccentColor);
    }

    private void DrawStashAutomationSummaryPanel()
    {
        var automation = Settings?.StashAutomation;
        if (automation == null)
        {
            ImGui.TextDisabled("Stash automation settings unavailable.");
            return;
        }

        var configuredSlotCount = CountEnabledTargets(automation);
        var configuredTabCount = CountConfiguredTargetTabs(automation);
        var stashSetupReady = configuredSlotCount > 0 && configuredTabCount >= configuredSlotCount;
        var stashSetupState = stashSetupReady ? "Ready" : "Needs setup";

        DrawHeroBanner(
            "StashBanner",
            "STASH AND MAP DEVICE",
            "Stash & Map Device",
            "Configure restock targets, atlas map choice, and source-tab coverage for map preparation.",
            ("Setup state", stashSetupState, stashSetupReady ? SummaryOkColor : SummaryWarnColor),
            ("Atlas map", FormatMapSelectionSummary(automation), SummaryAccentColor),
            ("Map-device slots", configuredSlotCount.ToString(CultureInfo.InvariantCulture), SummaryAccentColor),
            ("Source-tab coverage", FormatCoverageSummary(automation), configuredTabCount >= configuredSlotCount ? SummaryOkColor : SummaryWarnColor));

        DrawSectionLabel("Operational details", "Hotkeys, helper toggles, and live stash context that are not already shown in the banner.");
        if (ImGui.BeginTable("##BeastsV2StashSetupSummary", 2, SummaryTableFlags))
        {
            DrawSummaryRow("Prepare-map-device hotkey", FormatHotkey(automation.LoadMapDeviceHotkey));
            DrawSummaryRow("Other hotkeys", "Automation > Hotkeys", SummaryMutedColor);
            DrawSummaryRow("Auto-restock missing", automation.AutoRestockMissingMapDeviceItems.Value ? "Enabled" : "Disabled", GetStateColor(automation.AutoRestockMissingMapDeviceItems.Value));
            DrawSummaryRow("Live stash UI", IsStashSelectorContextOpen() ? "Ready" : "Open stash to edit", IsStashSelectorContextOpen() ? SummaryOkColor : SummaryWarnColor);
            ImGui.EndTable();
        }

        DrawSectionLabel("Slot routing", "Per-slot source-tab and quantity configuration.");
        if (ImGui.BeginTable("##BeastsV2StashSlotSummary", 4, SummaryListTableFlags))
        {
            ImGui.TableSetupColumn("Slot");
            ImGui.TableSetupColumn("Item");
            ImGui.TableSetupColumn("Qty");
            ImGui.TableSetupColumn("Source tab");
            ImGui.TableHeadersRow();

            foreach (var (label, _, target) in GetAutomationTargets(automation))
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(label);

                ImGui.TableNextColumn();
                DrawSummaryValue(target.Enabled.Value ? FormatText(target.ItemName?.Value, "Not set") : "Disabled", target.Enabled.Value ? null : SummaryMutedColor);

                ImGui.TableNextColumn();
                DrawSummaryValue(target.Enabled.Value ? target.Quantity.Value.ToString(CultureInfo.InvariantCulture) : "-", target.Enabled.Value ? null : SummaryMutedColor);

                ImGui.TableNextColumn();
                DrawSummaryValue(target.Enabled.Value ? FormatText(target.SelectedTabName?.Value, "Not set") : "-", target.Enabled.Value ? null : SummaryMutedColor);
            }

            ImGui.EndTable();
        }
    }

    private void DrawBestiaryAutomationSummaryPanel()
    {
        var automation = Settings?.BestiaryAutomation;
        if (automation == null)
        {
            ImGui.TextDisabled("Bestiary automation settings unavailable.");
            return;
        }

        var itemizedTabConfigured = !string.IsNullOrWhiteSpace(automation.SelectedTabName?.Value);
        var bestiarySetupReady = !automation.RegexItemizeAutoStash.Value || itemizedTabConfigured;
        var bestiarySetupState = bestiarySetupReady ? "Ready" : "Needs setup";

        DrawHeroBanner(
            "BestiaryBanner",
            "BESTIARY AUTOMATION",
            "Bestiary Automation",
            "Configure the delete-beasts hotkey, itemize flow, and where itemized beasts should be stored.",
            ("Setup state", bestiarySetupState, bestiarySetupReady ? SummaryOkColor : SummaryWarnColor),
            ("Delete-beasts hotkey", FormatHotkey(automation.DeleteHotkey), SummaryAccentColor),
            ("Itemized-beast tab", FormatText(automation.SelectedTabName?.Value, "Not set"), itemizedTabConfigured ? SummaryOkColor : SummaryWarnColor),
            ("Auto-stash", automation.RegexItemizeAutoStash.Value ? "Enabled" : "Disabled", GetStateColor(automation.RegexItemizeAutoStash.Value)));

        DrawSectionLabel("Operational details", "Supporting hotkeys and helper toggles used during Bestiary delete and itemize actions.");
        if (ImGui.BeginTable("##BeastsV2BestiarySetupSummary", 2, SummaryTableFlags))
        {
            DrawSummaryRow("Other hotkeys", "Automation > Hotkeys", SummaryMutedColor);
            DrawSummaryRow("Auto-stash itemized beasts", automation.RegexItemizeAutoStash.Value ? "Enabled" : "Disabled", GetStateColor(automation.RegexItemizeAutoStash.Value));
            DrawSummaryRow("Configured red-beast tab", FormatText(automation.SelectedRedBeastTabName?.Value, "Uses itemized tab"));
            DrawSummaryRow("Bestiary quick buttons", automation.ShowBestiaryButtons.Value ? "Visible" : "Hidden", GetStateColor(automation.ShowBestiaryButtons.Value));
            DrawSummaryRow("Inventory quick button", automation.ShowInventoryButton.Value ? "Visible" : "Hidden", GetStateColor(automation.ShowInventoryButton.Value));
            ImGui.EndTable();
        }
    }

    private void DrawMerchantAutomationSummaryPanel()
    {
        var automation = Settings?.MerchantAutomation;
        if (automation == null)
        {
            ImGui.TextDisabled("Merchant automation settings unavailable.");
            return;
        }

        var merchantHotkeyConfigured = automation.FaustusListHotkey.Value.Key != Keys.None;
        var merchantTabConfigured = !string.IsNullOrWhiteSpace(automation.SelectedFaustusShopTabName?.Value);
        var merchantSetupReady = merchantHotkeyConfigured && merchantTabConfigured;
        var merchantSetupState = merchantSetupReady ? "Ready" : "Needs setup";

        DrawHeroBanner(
            "MerchantBanner",
            "MERCHANT AUTOMATION",
            "Merchant Automation",
            "Manage the Faustus listing trigger, pricing multiplier, and configured sell tab from one panel.",
            ("Setup state", merchantSetupState, merchantSetupReady ? SummaryOkColor : SummaryWarnColor),
            ("Price multiplier", automation.FaustusPriceMultiplier.Value.ToString("0.##x", CultureInfo.InvariantCulture), SummaryAccentColor),
            ("Configured sell tab", FormatText(automation.SelectedFaustusShopTabName?.Value, "Not set"), merchantTabConfigured ? SummaryOkColor : SummaryWarnColor),
            ("Faustus-list hotkey", merchantHotkeyConfigured ? "Automation > Hotkeys" : "Missing", merchantHotkeyConfigured ? SummaryMutedColor : SummaryWarnColor));
    }

    private void DrawFullSequenceAutomationSummaryPanel()
    {
        var fullSequence = Settings?.FullSequenceAutomation;
        var bestiary = Settings?.BestiaryAutomation;
        var merchant = Settings?.MerchantAutomation;
        if (fullSequence == null || bestiary == null || merchant == null)
        {
            ImGui.TextDisabled("Full-sequence settings unavailable.");
            return;
        }

        var ready = fullSequence.FullSequenceHotkey.Value.Key != Keys.None &&
                    !string.IsNullOrWhiteSpace(bestiary.SelectedTabName?.Value) &&
                    !string.IsNullOrWhiteSpace(merchant.SelectedFaustusShopTabName?.Value);
        var fullSequenceSetupState = ready ? "Ready" : "Needs setup";
        var itemizedTabConfigured = !string.IsNullOrWhiteSpace(bestiary.SelectedTabName?.Value);
        var merchantTabConfigured = !string.IsNullOrWhiteSpace(merchant.SelectedFaustusShopTabName?.Value);

        DrawHeroBanner(
            "FullSequenceBanner",
            "SELL SEQUENCE",
            "Sell Sequence",
            "One hotkey can itemize matching beasts and list them in Faustus when the required setup is in place.",
            ("Setup state", fullSequenceSetupState, ready ? SummaryOkColor : SummaryWarnColor),
            ("Sell-sequence hotkey", FormatHotkey(fullSequence.FullSequenceHotkey), SummaryAccentColor),
            ("Itemized-beast tab", FormatText(bestiary.SelectedTabName?.Value, "Not set"), itemizedTabConfigured ? SummaryOkColor : SummaryWarnColor),
            ("Faustus sell tab", FormatText(merchant.SelectedFaustusShopTabName?.Value, "Not set"), merchantTabConfigured ? SummaryOkColor : SummaryWarnColor));
    }

    private static void DrawHeroBanner(string scopeId, string eyebrow, string title, string description, params (string Label, string Value, Vector4 Accent)[] badges)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, SummaryHeroBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, SummaryHeroBorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 5f));

        if (ImGui.BeginChild($"##{scopeId}", Vector2.Zero, ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var drawList = ImGui.GetWindowDrawList();
            var rectMin = ImGui.GetWindowPos();
            var rectMax = rectMin + ImGui.GetWindowSize();
            drawList.AddRectFilled(rectMin, new Vector2(rectMax.X, rectMin.Y + 4f), ImGui.GetColorU32(SummaryHeroGlowColor), 10f, ImDrawFlags.RoundCornersTop);

            ImGui.TextColored(SummaryMutedColor, eyebrow);
            ImGui.TextColored(SummaryAccentColor, title);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.82f, 0.84f, 0.88f, 1f));
            ImGui.TextWrapped(description);
            ImGui.PopStyleColor();
            if (badges != null && badges.Length > 0)
            {
                ImGui.Spacing();
                for (var i = 0; i < badges.Length; i++)
                {
                    DrawBadge($"{scopeId}_badge_{i}", badges[i].Label, badges[i].Value, badges[i].Accent);
                    if (i < badges.Length - 1)
                    {
                        ImGui.SameLine();
                    }
                }
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);
        ImGui.Spacing();
    }

    private static void DrawAutomationHotkeySeparator()
    {
        ImGui.Dummy(new Vector2(0f, 2f));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 2f));
    }

    private static void DrawAutomationHotkeyHeader(string title, string description)
    {
        ImGui.Dummy(new Vector2(0f, 2f));
        ImGui.TextColored(SummaryAccentColor, title);
        if (!string.IsNullOrWhiteSpace(description))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(description);
        }
        ImGui.Dummy(new Vector2(0f, 1f));
    }

    private static void DrawSectionLabel(string title, string description)
    {
        ImGui.TextColored(SummaryAccentColor, title);
        if (!string.IsNullOrWhiteSpace(description))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(description);
        }
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 2f));
    }

    private static void DrawBadge(string id, string label, string value, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(0.86f, 0.87f, 0.90f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(accent.X * 0.24f, accent.Y * 0.24f, accent.Z * 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(accent.X * 0.24f, accent.Y * 0.24f, accent.Z * 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(accent.X * 0.24f, accent.Y * 0.24f, accent.Z * 0.24f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, accent);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6f);
        ImGui.BeginDisabled();
        ImGui.Button($"{label}: {value}##{id}");
        ImGui.EndDisabled();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(5);
    }

    private static void DrawActionButtonsRow(string scopeId, params (string Label, Action Action)[] actions)
    {
        if (actions == null || actions.Length == 0)
        {
            return;
        }

        if (!ImGui.BeginTable($"##{scopeId}", actions.Length, SummaryCardTableFlags))
        {
            return;
        }

        for (var i = 0; i < actions.Length; i++)
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Button, SummaryActionButtonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, SummaryActionButtonHoverColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, SummaryActionButtonActiveColor);
            ImGui.PushStyleColor(ImGuiCol.Border, SummaryAccentColor);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 7f);
            if (ImGui.Button($"{actions[i].Label}##{scopeId}_{i}", new Vector2(-1f, 0f)))
            {
                actions[i].Action();
            }
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(4);
        }

        ImGui.EndTable();
        ImGui.Spacing();
    }

    private static void DrawHintCallout(string scopeId, string title, string body, Vector4 accent)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, SummaryCardBackgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Border, SummaryCardBorderColor);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 9f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));

        if (ImGui.BeginChild($"##{scopeId}", Vector2.Zero, ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextColored(accent, title);
            ImGui.PushStyleColor(ImGuiCol.Text, SummaryMutedColor);
            ImGui.TextWrapped(body);
            ImGui.PopStyleColor();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
        ImGui.Spacing();
    }

    private static void DrawSummaryRow(string label, string value, Vector4? color = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TableNextColumn();
        DrawSummaryValue(value, color);
    }

    private static void DrawSummaryValue(string value, Vector4? color = null)
    {
        if (color.HasValue)
        {
            ImGui.TextColored(color.Value, value);
            return;
        }

        ImGui.TextUnformatted(value);
    }

    private static string FormatHotkey(HotkeyNodeV2 hotkey)
    {
        return hotkey?.Value.Key == Keys.None ? "Not set" : hotkey?.Value.ToString() ?? "Not set";
    }

    private static string FormatText(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string FormatRefreshInterval(int minutes)
    {
        return minutes <= 0 ? "Manual only" : $"Every {minutes} min";
    }

    private static Vector4 GetStateColor(bool state)
    {
        return state ? SummaryOkColor : SummaryWarnColor;
    }

    private static string FormatCoverageSummary(StashAutomationSettings automation)
    {
        var enabled = CountEnabledTargets(automation);
        if (enabled <= 0)
        {
            return "0 / 0 tabs";
        }

        return $"{CountConfiguredTargetTabs(automation)} / {enabled} tabs";
    }

    private static int CountEnabledTargets(StashAutomationSettings automation)
    {
        return GetAutomationTargets(automation).Count(x => x.Target.Enabled.Value);
    }

    private static int CountConfiguredTargetTabs(StashAutomationSettings automation)
    {
        return GetAutomationTargets(automation).Count(x => x.Target.Enabled.Value && !string.IsNullOrWhiteSpace(x.Target.SelectedTabName?.Value));
    }

    private static string FormatMapSelectionSummary(StashAutomationSettings automation)
    {
        var selection = NormalizeMapSelectionValue(automation?.SelectedMapToRun?.Value);
        return selection.EqualsIgnoreCase(OpenMapSelectionValue)
            ? "Keep currently opened map"
            : selection;
    }

    private bool IsStashSelectorContextOpen()
    {
        return GameController?.IngameState?.IngameUi?.StashElement?.IsVisible == true;
    }

    private bool IsFaustusSelectorContextOpen()
    {
        return GetOfflineMerchantPanel()?.IsVisible == true;
    }
}