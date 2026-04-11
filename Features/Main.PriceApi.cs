using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using ExileCore;
using ImGuiNET;
using Newtonsoft.Json;

namespace BeastsV2;

public partial class Main
{
    private static readonly Vector4 EnabledBeastTextColor = new(0.4f, 1f, 0.4f, 1f);
    private static readonly HttpClient HttpClient = new();
    private static readonly string[] PoeNinjaItemOverviewTypes =
    [
        "Scarab",
        "Map",
        "Fragment",
        "Currency",
        "Invitation",
        "Oil",
    ];

    private Dictionary<string, float> _beastPrices = AllRedBeasts.ToDictionary(x => x.Name, _ => -1f);
    private Dictionary<string, float> _marketItemPrices = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, float> _mapTierAveragePrices = new();
    private Dictionary<string, string> _beastPriceTexts = new(StringComparer.OrdinalIgnoreCase);
    private TrackedBeast[] _sortedBeastsByPrice = AllRedBeasts;
    private bool _isFetchingPrices;
    private DateTime _lastPriceFetchAttempt = DateTime.MinValue;

    private void DrawBeastPickerPanel()
    {
        ImGui.Text($"Prices as of: {Settings.BeastPrices.LastUpdated}");
        ImGui.Separator();

        if (!ImGui.BeginTable("##BeastPickerTable", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersV |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                new Vector2(0, 400)))
            return;

        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 24);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var enabledBeasts = Settings.BeastPrices.EnabledBeasts;

        foreach (var beast in _sortedBeastsByPrice)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var isEnabled = enabledBeasts.Contains(beast.Name);
            if (ImGui.Checkbox($"##{beast.Name}_chk", ref isEnabled))
            {
                if (isEnabled) enabledBeasts.Add(beast.Name);
                else enabledBeasts.Remove(beast.Name);

                SavePersistedBeastPriceSettings();
            }

            ImGui.TableNextColumn();
            ImGui.Text(TryGetBeastPriceText(beast.Name, out var priceText) ? priceText : "?");

            ImGui.TableNextColumn();
            if (isEnabled)
                ImGui.TextColored(EnabledBeastTextColor, beast.Name);
            else
                ImGui.TextDisabled(beast.Name);
        }

        ImGui.EndTable();
    }

    private void SelectAllPriceDataBeasts()
    {
        SetAllPriceDataBeastsEnabled(true);
    }

    private void DeselectAllPriceDataBeasts()
    {
        SetAllPriceDataBeastsEnabled(false);
    }

    private void SelectPriceDataBeastsWorth15ChaosOrMore()
    {
        SetEnabledPriceDataBeasts(beast =>
            _beastPrices.TryGetValue(beast.Name, out var price) && price >= 15f);
    }

    private void SetAllPriceDataBeastsEnabled(bool isEnabled)
    {
        SetEnabledPriceDataBeasts(beast => isEnabled);
    }

    private void SetEnabledPriceDataBeasts(Func<TrackedBeast, bool> predicate)
    {
        var enabledBeasts = Settings.BeastPrices.EnabledBeasts;
        enabledBeasts.Clear();

        if (predicate != null)
        {
            enabledBeasts.UnionWith(AllRedBeasts.Where(predicate).Select(x => x.Name));
        }

        SavePersistedBeastPriceSettings();
    }

    private async Task FetchBeastPricesAsync()
    {
        if (_isFetchingPrices) return;
        _isFetchingPrices = true;
        _lastPriceFetchAttempt = DateTime.UtcNow;
        try
        {
            Log("Fetching beast prices from poe.ninja...");
            var league = Uri.EscapeDataString(Settings.BeastPrices.League.Value?.Trim() ?? "Mirage");

            var beastUrl = $"https://poe.ninja/api/data/itemoverview?league={league}&type=Beast";
            var beastJson = await HttpClient.GetStringAsync(beastUrl);
            var beastResponse = JsonConvert.DeserializeObject<PoeNinjaBeastsResponse>(beastJson);
            if (beastResponse?.Lines == null) return;

            var lookup = beastResponse.Lines
                .Where(l => l.Name != null)
                .ToDictionary(l => l.Name, l => l.ChaosValue, StringComparer.OrdinalIgnoreCase);

            var updated = AllRedBeasts.ToDictionary(
                b => b.Name,
                b => lookup.TryGetValue(b.Name, out var price) ? price : -1f,
                StringComparer.OrdinalIgnoreCase);

            _beastPrices = updated;
            RebuildPriceCaches(updated);

            var marketItemPrices = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var mapTierBuckets = new Dictionary<int, List<float>>();

            foreach (var type in PoeNinjaItemOverviewTypes)
            {
                try
                {
                    var url = $"https://poe.ninja/api/data/itemoverview?league={league}&type={Uri.EscapeDataString(type)}";
                    var json = await HttpClient.GetStringAsync(url);
                    var response = JsonConvert.DeserializeObject<PoeNinjaItemOverviewResponse>(json);
                    if (response?.Lines == null)
                    {
                        continue;
                    }

                    foreach (var line in response.Lines)
                    {
                        if (string.IsNullOrWhiteSpace(line.Name) || line.ChaosValue <= 0)
                        {
                            continue;
                        }

                        marketItemPrices[line.Name] = line.ChaosValue;

                        if (line.MapTier.HasValue && line.MapTier.Value > 0)
                        {
                            if (!mapTierBuckets.TryGetValue(line.MapTier.Value, out var bucket))
                            {
                                bucket = new List<float>();
                                mapTierBuckets[line.MapTier.Value] = bucket;
                            }

                            bucket.Add(line.ChaosValue);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Skipping poe.ninja {type} prices. {ex.GetType().Name}: {ex.Message}");
                }
            }

            _marketItemPrices = marketItemPrices;
            _mapTierAveragePrices = mapTierBuckets.ToDictionary(
                x => x.Key,
                x => x.Value.Count > 0 ? x.Value.Average() : 0f);

            Settings.BeastPrices.LastUpdated = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            SavePersistedBeastPriceSettings();
            Log($"Beast + item prices updated ({Settings.BeastPrices.LastUpdated}).");
        }
        catch (Exception ex)
        {
            LogError("Failed to fetch beast prices", ex);
        }
        finally
        {
            _isFetchingPrices = false;
        }
    }

    private void RebuildPriceCaches(Dictionary<string, float> prices)
    {
        _beastPriceTexts = AllRedBeasts
            .Where(b => prices.TryGetValue(b.Name, out var p) && p >= 0)
            .ToDictionary(b => b.Name, b => $"{prices[b.Name]:0}c", StringComparer.OrdinalIgnoreCase);

        _sortedBeastsByPrice = AllRedBeasts
            .OrderByDescending(b => prices.TryGetValue(b.Name, out var price) ? price : -1f)
            .ToArray();
    }

    private bool TryGetConfiguredItemPriceChaos(string configuredName, out double chaosValue)
    {
        chaosValue = 0d;
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            return false;
        }

        var normalized = configuredName.Trim();
        if (_marketItemPrices.TryGetValue(normalized, out var directPrice) && directPrice > 0)
        {
            chaosValue = directPrice;
            return true;
        }

        var mapTierMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"^Map \(Tier\s*(\d+)\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mapTierMatch.Success && int.TryParse(mapTierMatch.Groups[1].Value, out var tier) &&
            _mapTierAveragePrices.TryGetValue(tier, out var tierAvg) && tierAvg > 0)
        {
            chaosValue = tierAvg;
            return true;
        }

        return false;
    }

    private class PoeNinjaBeastsResponse
    {
        [JsonProperty("lines")] public List<PoeNinjaBeastLine> Lines { get; set; }
    }

    private class PoeNinjaBeastLine
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("chaosValue")] public float ChaosValue { get; set; }
    }

    private class PoeNinjaItemOverviewResponse
    {
        [JsonProperty("lines")] public List<PoeNinjaItemOverviewLine> Lines { get; set; }
    }

    private class PoeNinjaItemOverviewLine
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("chaosValue")] public float ChaosValue { get; set; }
        [JsonProperty("mapTier")] public int? MapTier { get; set; }
    }
}

