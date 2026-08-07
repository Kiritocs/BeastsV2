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
    private const string PoeNinjaItemOverviewEndpoint = "economy/stash/current/item/overview";
    private const string PoeNinjaExchangeOverviewEndpoint = "economy/exchange/current/overview";
    private static readonly string[] PoeNinjaItemOverviewTypes =
    [
        "Scarab",
        "Map",
        "Fragment",
        "Currency",
        "Invitation",
    ];
    private static readonly Dictionary<string, string> PoeNinjaOverviewEndpointByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Beast"] = PoeNinjaItemOverviewEndpoint,
        ["Scarab"] = PoeNinjaExchangeOverviewEndpoint,
        ["Map"] = PoeNinjaItemOverviewEndpoint,
        ["Fragment"] = PoeNinjaExchangeOverviewEndpoint,
        ["Currency"] = PoeNinjaExchangeOverviewEndpoint,
        ["Invitation"] = PoeNinjaItemOverviewEndpoint,
    };

    private Dictionary<string, float> _beastPrices = AllRedBeasts.ToDictionary(x => x.Name, _ => -1f);
    private Dictionary<string, float> _marketItemPrices = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, float> _mapTierAveragePrices = new();
    private Dictionary<string, string> _beastPriceTexts = new(StringComparer.OrdinalIgnoreCase);
    private TrackedBeast[] _sortedBeastsByPrice = AllRedBeasts;
    private bool _isFetchingPrices;

    // When a fetch was last *started*. Throttles the auto-refresh timer so a failing endpoint
    // is retried on the normal cadence instead of being hammered every frame.
    private DateTime _lastPriceFetchAttempt = DateTime.MinValue;

    // When a fetch last *succeeded* and actually replaced _beastPrices. This is what freshness
    // is measured against: an attempt that threw or came back empty must not make stale prices
    // look current, or the next listing run silently skips its refresh and sells on old data.
    private DateTime _lastPriceFetchSuccess = DateTime.MinValue;
    private string _beastPickerSearch = string.Empty;

    // How old the loaded beast prices are, or null if no fetch has ever succeeded.
    private double? BeastPriceAgeSeconds => _lastPriceFetchSuccess == DateTime.MinValue
        ? null
        : (DateTime.UtcNow - _lastPriceFetchSuccess).TotalSeconds;

    // Header text for any panel showing price age.
    private string DescribeBeastPriceTimestamp()
    {
        var lastUpdated = Settings?.BeastPrices?.LastUpdated;
        if (Settings?.BeastPrices?.HasFetchedPricesThisSession == true)
        {
            return $"Prices as of: {lastUpdated}";
        }

        return string.IsNullOrWhiteSpace(lastUpdated) || lastUpdated == "never"
            ? "Prices not loaded yet."
            : $"Prices not loaded yet (previous session: {lastUpdated}).";
    }

    // Where the currently loaded prices came from, for logs that need to be self-explanatory.
    private string DescribeBeastPriceProvenance()
    {
        var age = BeastPriceAgeSeconds;
        var league = Settings?.BeastPrices?.League?.Value?.Trim();
        return $"league='{(string.IsNullOrWhiteSpace(league) ? "<unset>" : league)}', priceAge={(age.HasValue ? $"{age.Value:0}s" : "never fetched")}";
    }

    private void DrawBeastPickerPanel()
    {
        ImGui.Text(DescribeBeastPriceTimestamp());

        var search = _beastPickerSearch;
        ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - 60f));
        if (ImGui.InputTextWithHint("##BeastPickerSearch", "Search by name or family...", ref search, 64u))
            _beastPickerSearch = search;

        ImGui.SameLine();
        if (ImGui.Button("Clear##BeastPickerSearch"))
            _beastPickerSearch = string.Empty;

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
        var filter = _beastPickerSearch.Trim();
        var matchCount = 0;

        foreach (var beast in _sortedBeastsByPrice)
        {
            if (!MatchesBeastPickerSearch(beast.Name, filter)) continue;
            matchCount++;

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

        if (matchCount == 0)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TextDisabled($"No beasts match \"{filter}\".");
        }

        ImGui.EndTable();
    }

    private static bool MatchesBeastPickerSearch(string beastName, string filter)
    {
        return filter.Length == 0 ||
               beastName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               BeastsV2BeastData.GetBeastFamily(beastName).Contains(filter, StringComparison.OrdinalIgnoreCase);
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

    // The fetch currently in flight, so callers that need to wait join it rather than
    // starting a second request against poe.ninja.
    private Task _inFlightPriceFetch;
    private readonly object _priceFetchSync = new();

    // How long a pre-listing refresh may run before the run gives up and uses what it has.
    private const int PriceRefreshTimeoutMs = 8000;

    // True while a fetch is running. Derived from the in-flight task under the same lock that
    // publishes it, so there is no separate flag for the render thread to observe out of order.
    private bool IsFetchingPrices
    {
        get
        {
            lock (_priceFetchSync)
            {
                return _inFlightPriceFetch is { IsCompleted: false };
            }
        }
    }

    // Every fetch goes through here so there is exactly one task to await.
    //
    // FetchBeastPricesAsync early-returns while a fetch is running, so calling it directly
    // during a background refresh hands back an already-completed task — which would let a
    // caller believe it had waited for prices it never waited for.
    internal Task StartOrJoinPriceFetch()
    {
        lock (_priceFetchSync)
        {
            if (_inFlightPriceFetch is { IsCompleted: false }) return _inFlightPriceFetch;

            _lastPriceFetchAttempt = DateTime.UtcNow;

            // Task.Run keeps the synchronous head of FetchBeastPricesAsync (league sync, which
            // can touch disk) off the render thread.
            return _inFlightPriceFetch = Task.Run(FetchBeastPricesAsync);
        }
    }

    // Refreshes prices and waits for them, unless they are already recent enough.
    //
    // Used before listing at Faustus, where a stale price is money rather than a cosmetic
    // label. Never throws: poe.ninja being slow or down must not abort a sell run.
    internal async Task<bool> EnsureBeastPricesFreshAsync(TimeSpan maxAge, int timeoutMs)
    {
        // Measured from the last *successful* fetch. A failed attempt leaves this untouched, so
        // a poe.ninja outage forces a real retry here instead of being papered over.
        var age = DateTime.UtcNow - _lastPriceFetchSuccess;
        if (_lastPriceFetchSuccess != DateTime.MinValue && age < maxAge)
        {
            LogDebug($"Prices are {age.TotalSeconds:0}s old, within the {maxAge.TotalSeconds:0}s window. No refresh needed. {DescribeBeastPriceProvenance()}");
            return true;
        }

        var fetch = StartOrJoinPriceFetch();

        try
        {
            var completed = await Task.WhenAny(fetch, Task.Delay(timeoutMs));
            if (completed != fetch)
            {
                LogDebug($"Price refresh did not finish within {timeoutMs}ms. Continuing with prices from {Settings.BeastPrices.LastUpdated}.");
                return false;
            }

            // Surfaces a fetch that faulted rather than timed out.
            await fetch;
            return true;
        }
        catch (Exception ex)
        {
            LogDebug($"Price refresh failed ({ex.GetType().Name}: {ex.Message}). Continuing with prices from {Settings.BeastPrices.LastUpdated}.");
            return false;
        }
    }

    // Entry point used by the Faustus listing workflow, honouring the settings toggle.
    private async Task RefreshBeastPricesBeforeListingAsync()
    {
        var merchant = Settings?.MerchantAutomation;
        if (merchant?.RefreshPricesBeforeListing?.Value != true) return;

        var maxAge = TimeSpan.FromSeconds(Math.Max(1, merchant.MaxPriceAgeBeforeListingSeconds.Value));

        UpdateAutomationStatus("Refreshing poe.ninja prices...");
        var fresh = await EnsureBeastPricesFreshAsync(maxAge, PriceRefreshTimeoutMs);

        // Not fatal: listing on slightly old prices beats refusing to sell because a website
        // is down. EnsureBeastPricesFreshAsync has already logged why.
        if (!fresh)
            UpdateAutomationStatus("Could not refresh prices - listing with the prices already loaded.");

        LogDebug($"Faustus listing will price against: {DescribeBeastPriceProvenance()}, multiplier={Math.Clamp(merchant.FaustusPriceMultiplier?.Value ?? 1f, 0.5f, 1.5f):0.##}x.");
    }

    // Only ever invoked by StartOrJoinPriceFetch, which has already claimed the in-flight slot
    // and stamped _lastPriceFetchAttempt under its lock. Nothing here re-checks or re-stamps.
    private async Task FetchBeastPricesAsync()
    {
        try
        {
            SyncBeastPriceLeagueSettingFromServerData();
            Log("Fetching beast prices from poe.ninja...");
            var league = Uri.EscapeDataString(Settings.BeastPrices.League.Value?.Trim() ?? "Mirage");

            var beastUrl = BuildPoeNinjaOverviewUrl(league, "Beast");
                
            var beastJson = await HttpClient.GetStringAsync(beastUrl);
            var beastResponse = JsonConvert.DeserializeObject<PoeNinjaOverviewResponse>(beastJson);

            if (beastResponse?.Lines == null)
            {
                LogError($"poe.ninja returned no beast price lines for league '{league}'. Keeping the previously loaded prices.");
                return;
            }

            var lookup = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var beastNamesById = BuildPoeNinjaItemNameById(beastResponse);
            foreach (var line in beastResponse.Lines)
            {
                var lineName = GetPoeNinjaLineName(line, beastNamesById);
                var chaosValue = GetPoeNinjaLineChaosValue(line);
                if (string.IsNullOrWhiteSpace(lineName) || chaosValue <= 0)
                {
                    continue;
                }

                if (!lookup.TryGetValue(lineName, out var existingPrice) || chaosValue > existingPrice)
                {
                    lookup[lineName] = chaosValue;
                }
            }

            var updated = AllRedBeasts.ToDictionary(
                b => b.Name,
                b => lookup.TryGetValue(b.Name, out var price) ? price : -1f,
                StringComparer.OrdinalIgnoreCase);

            var pricedBeastCount = updated.Values.Count(price => price > 0);
            if (pricedBeastCount == 0)
            {
                LogError($"poe.ninja returned {beastResponse.Lines.Count} beast lines for league '{league}' but none matched a tracked beast. Keeping the previously loaded prices.");
                return;
            }

            _beastPrices = updated;
            RebuildPriceCaches(updated);
            _lastPriceFetchSuccess = DateTime.UtcNow;

            var marketItemPrices = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var mapTierBuckets = new Dictionary<int, List<float>>();

            foreach (var type in PoeNinjaItemOverviewTypes)
            {
                try
                {
                    var url = BuildPoeNinjaOverviewUrl(league, type);
                    var json = await HttpClient.GetStringAsync(url);
                    var response = JsonConvert.DeserializeObject<PoeNinjaOverviewResponse>(json);
                    if (response?.Lines == null)
                    {
                        continue;
                    }

                    var namesById = BuildPoeNinjaItemNameById(response);

                    foreach (var line in response.Lines)
                    {
                        var lineName = GetPoeNinjaLineName(line, namesById);
                        var chaosValue = GetPoeNinjaLineChaosValue(line);
                        if (string.IsNullOrWhiteSpace(lineName) || chaosValue <= 0)
                        {
                            continue;
                        }

                        marketItemPrices[lineName] = chaosValue;

                        var mapTier = GetPoeNinjaLineMapTier(line, lineName);
                        if (mapTier.HasValue)
                        {
                            if (!mapTierBuckets.TryGetValue(mapTier.Value, out var bucket))
                            {
                                bucket = new List<float>();
                                mapTierBuckets[mapTier.Value] = bucket;
                            }

                            bucket.Add(chaosValue);
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

            Settings.BeastPrices.LastUpdated = AnalyticsEngineV2.FormatUserLocalTime(DateTime.Now);
            Settings.BeastPrices.HasFetchedPricesThisSession = true;
            SavePersistedBeastPriceSettings();
            Log($"Beast + item prices updated ({Settings.BeastPrices.LastUpdated}). league='{league}', pricedBeasts={pricedBeastCount}/{AllRedBeasts.Length}, marketItems={marketItemPrices.Count}.");
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

    private static string BuildPoeNinjaOverviewUrl(string escapedLeague, string type)
    {
        if (!PoeNinjaOverviewEndpointByType.TryGetValue(type ?? string.Empty, out var endpoint))
        {
            endpoint = PoeNinjaItemOverviewEndpoint;
        }

        return $"https://poe.ninja/poe1/api/{endpoint}?league={escapedLeague}&type={Uri.EscapeDataString(type ?? string.Empty)}";
    }

    private static Dictionary<string, string> BuildPoeNinjaItemNameById(PoeNinjaOverviewResponse response)
    {
        var namesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response?.Items == null)
        {
            return namesById;
        }

        foreach (var item in response.Items)
        {
            if (string.IsNullOrWhiteSpace(item?.Id) || string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            namesById[item.Id] = item.Name;
        }

        return namesById;
    }

    private static string GetPoeNinjaLineName(PoeNinjaOverviewLine line, IReadOnlyDictionary<string, string> namesById)
    {
        if (!string.IsNullOrWhiteSpace(line?.Id) && namesById != null && namesById.TryGetValue(line.Id, out var nameById))
        {
            return nameById;
        }

        if (!string.IsNullOrWhiteSpace(line?.Name))
        {
            return line.Name;
        }

        return !string.IsNullOrWhiteSpace(line?.CurrencyTypeName)
            ? line.CurrencyTypeName
            : string.Empty;
    }

    private static float GetPoeNinjaLineChaosValue(PoeNinjaOverviewLine line)
    {
        if (line == null)
        {
            return -1f;
        }

        if (line.PrimaryValue > 0)
        {
            return line.PrimaryValue.Value;
        }

        if (line.ChaosValue > 0)
        {
            return line.ChaosValue.Value;
        }

        if (line.ChaosEquivalent > 0)
        {
            return line.ChaosEquivalent.Value;
        }

        return -1f;
    }

    private static int? GetPoeNinjaLineMapTier(PoeNinjaOverviewLine line, string lineName)
    {
        if (line?.MapTier > 0)
        {
            return line.MapTier;
        }

        if (string.IsNullOrWhiteSpace(lineName))
        {
            return null;
        }

        var tierMatch = System.Text.RegularExpressions.Regex.Match(
            lineName,
            @"\(\s*Tier\s*(\d+)\s*\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!tierMatch.Success || !int.TryParse(tierMatch.Groups[1].Value, out var parsedTier) || parsedTier <= 0)
        {
            return null;
        }

        return parsedTier;
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

    private class PoeNinjaOverviewResponse
    {
        [JsonProperty("items")] public List<PoeNinjaOverviewItem> Items { get; set; }
        [JsonProperty("lines")] public List<PoeNinjaOverviewLine> Lines { get; set; }
    }

    private class PoeNinjaOverviewItem
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
    }

    private class PoeNinjaOverviewLine
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("currencyTypeName")] public string CurrencyTypeName { get; set; }
        [JsonProperty("primaryValue")] public float? PrimaryValue { get; set; }
        [JsonProperty("chaosValue")] public float? ChaosValue { get; set; }
        [JsonProperty("chaosEquivalent")] public float? ChaosEquivalent { get; set; }
        [JsonProperty("mapTier")] public int? MapTier { get; set; }
    }
}

