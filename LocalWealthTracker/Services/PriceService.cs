using System.Net.Http;
using System.Text.Json;
using LocalWealthTracker.Models;

namespace LocalWealthTracker.Services;

public sealed class PriceService
{
    private const string BaseUrl =
        "https://poe.ninja/poe1/api/economy/exchange/current/overview";

    private static readonly (string Type, string Label)[] Categories =
    [
        ("Currency",       "Currency"),
        ("Fragment",       "Fragments"),
        ("DivinationCard", "Divination Cards"),
        ("Fossil",         "Fossils"),
        ("Resonator",      "Resonators"),
        ("Scarab",         "Scarabs"),
        ("Oil",            "Oils"),
        ("Essence",        "Essences"),
        ("DeliriumOrb",    "Delirium Orbs"),
        ("Omen",           "Omens"),
        ("Tattoo",         "Tattoos"),
        ("Artifact",       "Artifacts"),
        ("DjinnCoin",       "Djinn Coins")

    ];

    private readonly HttpClient _http;

    private readonly Dictionary<string, PriceEntry> _prices =
        new(StringComparer.OrdinalIgnoreCase);

    public double DivinePrice { get; private set; } = 1;
    public int EntryCount => _prices.Count;
    public bool IsLoaded { get; private set; }
    public DateTime? LastLoadedAt { get; private set; }
    public string? LoadedLeague { get; private set; }

    public PriceService(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LocalWealthTracker/1.0 (local-tool)");
    }

    public bool IsCacheValid(string league, int cacheMinutes)
    {
        if (!IsLoaded || LastLoadedAt == null || cacheMinutes <= 0)
            return false;
        if (!string.Equals(LoadedLeague, league, StringComparison.OrdinalIgnoreCase))
            return false;
        return (DateTime.Now - LastLoadedAt.Value).TotalMinutes < cacheMinutes;
    }

    public int CacheRemainingMinutes(int cacheMinutes)
    {
        if (LastLoadedAt == null || cacheMinutes <= 0) return 0;
        var remaining = cacheMinutes - (DateTime.Now - LastLoadedAt.Value).TotalMinutes;
        return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
    }

    public async Task LoadAsync(string league,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        _prices.Clear();
        IsLoaded = false;
        DivinePrice = 1;

        _prices["Chaos Orb"] = new PriceEntry(1, null, 0);

        foreach (var (type, label) in Categories)
        {
            progress?.Report($"Loading {label}…");

            var response = await FetchAsync(league, type, ct);
            if (response == null)
            {
                progress?.Report($"⚠ Failed to load {label}");
                continue;
            }

            if (DivinePrice <= 1 && response.Core?.Rates?.Divine is > 0)
                DivinePrice = Math.Round(1.0 / response.Core.Rates.Divine, 1);

            var itemLookup = response.Items
                .ToDictionary(i => i.Id, i => i, StringComparer.OrdinalIgnoreCase);

            foreach (var line in response.Lines)
            {
                if (line.PrimaryValue <= 0) continue;
                if (itemLookup.TryGetValue(line.Id, out var item))
                {
                    _prices[item.Name] = new PriceEntry(
                        line.PrimaryValue,
                        line.Sparkline?.Data,
                        line.Sparkline?.TotalChange ?? 0);
                }
            }

            if (response.Core?.Items != null)
            {
                foreach (var coreItem in response.Core.Items)
                {
                    if (coreItem.Name.Equals("Divine Orb", StringComparison.OrdinalIgnoreCase))
                        _prices["Divine Orb"] = new PriceEntry(DivinePrice, null, 0);
                }
            }

            await Task.Delay(100, ct);
        }

        IsLoaded = true;
        LastLoadedAt = DateTime.Now;
        LoadedLeague = league;

        progress?.Report(
            $"✅ Loaded {_prices.Count:N0} prices  " +
            $"(1 Divine = {DivinePrice:N0} Chaos)");
    }

    public double? GetPrice(string itemName)
    {
        return _prices.TryGetValue(itemName, out var entry) ? entry.Price : null;
    }

    public (List<double?>? Data, double TotalChange) GetSparkline(string itemName)
    {
        if (_prices.TryGetValue(itemName, out var entry))
            return (entry.SparklineData, entry.TotalChange);
        return (null, 0);
    }

    private async Task<ExchangeResponse?> FetchAsync(
        string league, string type, CancellationToken ct)
    {
        try
        {
            var url = $"{BaseUrl}" +
                      $"?league={Uri.EscapeDataString(league)}&type={type}";
            var json = await _http.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize<ExchangeResponse>(json);
        }
        catch { return null; }
    }

    private sealed record PriceEntry(
        double Price,
        List<double?>? SparklineData,
        double TotalChange);
}