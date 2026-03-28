using System.IO;
using System.Net.Http;
using System.Text.Json;
using LocalWealthTracker.Models;

namespace LocalWealthTracker.Services;

/// <summary>
/// Fetches and caches modifier lists and equipment base types from the PoE trade API.
/// Caches live in %APPDATA%/LocalWealthTracker/ and are valid for 24 hours.
/// </summary>
public sealed class ModDataService
{
    private static readonly string ModsCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LocalWealthTracker", "mods_cache.json");

    private static readonly string ItemsCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LocalWealthTracker", "items_cache.json");

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    // Only explicit and implicit mods are relevant for item value checking
    private static readonly HashSet<string> UsefulGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Explicit", "Implicit"
    };

    // Equipment categories from the trade items API that can carry valuable mods
    private static readonly HashSet<string> EquipmentGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Armour", "Weapons", "Accessories", "Jewels"
    };

    // ── Mods ─────────────────────────────────────────────────────

    public async Task<(List<TradeStatEntry> Mods, List<string> Categories, string? Error)>
        LoadModsAsync(CancellationToken ct = default)
    {
        try { return BuildModResult(await LoadGroupsAsync(ct)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ([], [], $"Could not load mod list: {ex.Message}"); }
    }

    public async Task<(List<TradeStatEntry> Mods, List<string> Categories, string? Error)>
        ReloadModsAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(ModsCachePath)) File.Delete(ModsCachePath);
            return BuildModResult(await LoadGroupsAsync(ct));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ([], [], $"Could not reload mod list: {ex.Message}"); }
    }

    // ── Base types ───────────────────────────────────────────────

    /// <summary>
    /// Returns a flat list of equipment base types grouped by category.
    /// Format: (BaseTypes, GroupedCategories, Error)
    /// </summary>
    public async Task<(List<TradeItemEntry> Items, List<string> Categories, string? Error)>
        LoadBaseTypesAsync(CancellationToken ct = default)
    {
        try { return BuildItemResult(await LoadItemGroupsAsync(ct)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ([], [], $"Could not load base types: {ex.Message}"); }
    }

    // ── Internals ────────────────────────────────────────────────

    private static (List<TradeStatEntry> Mods, List<string> Categories, string? Error)
        BuildModResult(List<TradeStatGroup> groups)
    {
        var useful = groups.Where(g => UsefulGroups.Contains(g.Label)).ToList();

        var mods = useful
            .SelectMany(g => g.Entries.Select(e => { e.GroupLabel = g.Label; return e; }))
            .ToList();

        var categories = new List<string> { "All" };
        categories.AddRange(useful.Select(g => g.Label));

        return (mods, categories, null);
    }

    // Desired category order in the BaseGroupCombo
    private static readonly List<string> CategoryOrder =
    [
        "Body Armour", "Helmets", "Gloves", "Boots", "Shields", "Quivers",
        "Weapons", "Accessories", "Jewels"
    ];

    private static (List<TradeItemEntry> Items, List<string> Categories, string? Error)
        BuildItemResult(List<TradeItemGroup> groups)
    {
        var useful = groups.Where(g => EquipmentGroups.Contains(g.Label)).ToList();

        // Classify each entry, deduplicate by (type, slot)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<TradeItemEntry>();

        foreach (var g in useful)
        {
            foreach (var e in g.Entries)
            {
                e.GroupLabel = g.Label == "Armour" ? ClassifyArmourBase(e.Type) : g.Label;
                if (seen.Add($"{e.GroupLabel}|{e.Type}"))
                    items.Add(e);
            }
        }

        var usedCategories = items.Select(e => e.GroupLabel).Distinct().ToHashSet();
        var categories = new List<string> { "Any Base" };
        categories.AddRange(CategoryOrder.Where(usedCategories.Contains));

        return (items, categories, null);
    }

    // Classify an armour base type by checking for well-known slot keywords.
    // Checked in order: more-specific / shorter keyword sets first so "Greaves"
    // doesn't accidentally match something before "Boots" does.
    private static string ClassifyArmourBase(string type)
    {
        if (ContainsAny(type, "Boots", "Greaves", "Treads", "Slippers", "Shoes",
                               "Sabatons", "Caligae", "Sollerets"))
            return "Boots";

        if (ContainsAny(type, "Gauntlets", "Gloves", "Mitts", "Bracers", "Gages"))
            return "Gloves";

        if (ContainsAny(type, "Shield", "Buckler", "Bundle"))
            return "Shields";

        if (ContainsAny(type, "Quiver"))
            return "Quivers";

        if (ContainsAny(type, "Helmet", "Burgonet", "Bascinet", "Crown", "Circlet",
                               "Mask", "Cap", "Hood", "Coif", "Sallet", "Helm",
                               "Tricorne", "Hat", "Pelt", "Cage", "Crest",
                               "Visage", "Wreath"))
            return "Helmets";

        return "Body Armour";
    }

    private static bool ContainsAny(string source, params string[] terms)
        => terms.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));

    private async Task<List<TradeStatGroup>> LoadGroupsAsync(CancellationToken ct)
    {
        if (File.Exists(ModsCachePath))
        {
            var info = new FileInfo(ModsCachePath);
            if (DateTime.UtcNow - info.LastWriteTimeUtc < CacheTtl)
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<List<TradeStatGroup>>(
                        await File.ReadAllTextAsync(ModsCachePath, ct));
                    if (cached is { Count: > 0 }) return cached;
                }
                catch { }
            }
        }

        using var http = CreateHttpClient();
        var json = await http.GetStringAsync("https://www.pathofexile.com/api/trade/data/stats", ct);
        var groups = JsonSerializer.Deserialize<TradeStatsResponse>(json)?.Result ?? [];

        Directory.CreateDirectory(Path.GetDirectoryName(ModsCachePath)!);
        await File.WriteAllTextAsync(ModsCachePath,
            JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = false }), ct);

        return groups;
    }

    private async Task<List<TradeItemGroup>> LoadItemGroupsAsync(CancellationToken ct)
    {
        if (File.Exists(ItemsCachePath))
        {
            var info = new FileInfo(ItemsCachePath);
            if (DateTime.UtcNow - info.LastWriteTimeUtc < CacheTtl)
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<List<TradeItemGroup>>(
                        await File.ReadAllTextAsync(ItemsCachePath, ct));
                    if (cached is { Count: > 0 }) return cached;
                }
                catch { }
            }
        }

        using var http = CreateHttpClient();
        var json = await http.GetStringAsync("https://www.pathofexile.com/api/trade/data/items", ct);
        var groups = JsonSerializer.Deserialize<TradeItemsResponse>(json)?.Result ?? [];

        Directory.CreateDirectory(Path.GetDirectoryName(ItemsCachePath)!);
        await File.WriteAllTextAsync(ItemsCachePath,
            JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = false }), ct);

        return groups;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalWealthTracker/1.0 (local-tool)");
        return http;
    }
}
