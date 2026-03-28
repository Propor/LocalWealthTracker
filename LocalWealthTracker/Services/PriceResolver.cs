using System.Text.RegularExpressions;
using LocalWealthTracker.Models;

namespace LocalWealthTracker.Services;

public sealed class PriceResolver(PriceService prices)
{
    /// <summary>
    /// Prices every item in a tab.
    /// Returns priced items, unpriced items, and the unpriced count.
    /// </summary>
    public (List<PricedItem> Priced, List<UnpricedItem> Unpriced) PriceTab(
        IEnumerable<StashItem> items, double divinePrice,
        double minValueChaos = 0, string tabName = "", int tabIndex = -1)
    {
        var result = new List<PricedItem>();
        var unpriced = new List<UnpricedItem>();

        foreach (var item in items)
        {
            var unitPrice = Resolve(item);

            if (unitPrice is null or <= 0)
            {
                // Only track as unpriced if it looks like a real item
                if (item.FrameType is 0 or 5 or 6)
                {
                    unpriced.Add(new UnpricedItem
                    {
                        Name = item.TypeLine,
                        Icon = item.Icon,
                        TabName = tabName,
                        Quantity = item.StackSize ?? 1,
                        FrameType = item.FrameType,
                        Category = GetCategory(item.FrameType)
                    });
                }
                continue;
            }

            int qty = item.StackSize ?? 1;
            double totalChaos = unitPrice.Value * qty;
            if (totalChaos < minValueChaos) continue;

            var (sparkData, sparkTrend) = prices.GetSparkline(item.TypeLine);

            result.Add(new PricedItem
            {
                Name = item.TypeLine,
                Category = GetCategory(item.FrameType),
                Icon = item.Icon,
                TabName = tabName,
                TabIndex = tabIndex,
                Quantity = qty,
                UnitPriceChaos = Math.Round(unitPrice.Value, 2),
                TotalPriceChaos = Math.Round(totalChaos, 2),
                TotalPriceDivine = divinePrice > 0
                    ? Math.Round(totalChaos / divinePrice, 2) : 0,
                DivinePrice = divinePrice,
                SparklineData = sparkData,
                SparklineTrend = sparkTrend
            });
        }

        return (result.OrderByDescending(x => x.TotalPriceChaos).ToList(), unpriced);
    }

    /// <summary>
    /// Combines duplicate items across multiple tabs.
    /// </summary>
    public static List<PricedItem> CombineDuplicates(IEnumerable<PricedItem> items)
    {
        return items
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                int totalQty = g.Sum(i => i.Quantity);
                double totalChaos = g.Sum(i => i.TotalPriceChaos);

                return new PricedItem
                {
                    Name = first.Name,
                    Category = first.Category,
                    Icon = first.Icon,
                    TabName = g.Select(i => i.TabName).Distinct().Count() > 1
                        ? $"{g.Select(i => i.TabName).Distinct().Count()} tabs"
                        : first.TabName,
                    Quantity = totalQty,
                    UnitPriceChaos = first.UnitPriceChaos,
                    TotalPriceChaos = Math.Round(totalChaos, 2),
                    TotalPriceDivine = first.DivinePrice > 0
                        ? Math.Round(totalChaos / first.DivinePrice, 2) : 0,
                    DivinePrice = first.DivinePrice,
                    SparklineData = first.SparklineData,
                    SparklineTrend = first.SparklineTrend
                };
            })
            .OrderByDescending(x => x.TotalPriceChaos)
            .ToList();
    }

    /// <summary>
    /// Combines duplicate unpriced items across multiple tabs.
    /// </summary>
    public static List<UnpricedItem> CombineUnpriced(IEnumerable<UnpricedItem> items)
    {
        return items
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new UnpricedItem
                {
                    Name = first.Name,
                    Icon = first.Icon,
                    TabName = g.Select(i => i.TabName).Distinct().Count() > 1
                        ? $"{g.Select(i => i.TabName).Distinct().Count()} tabs"
                        : first.TabName,
                    Quantity = g.Sum(i => i.Quantity),
                    FrameType = first.FrameType,
                    Category = first.Category
                };
            })
            .OrderByDescending(x => x.Quantity)
            .ToList();
    }

    /// <summary>
    /// Checks all items in a tab against a modifier profile.
    /// Only includes items that can carry mods (magic/rare/unique or any item with mods).
    /// Matches are sorted first, then alphabetically.
    /// <para>
    /// Profile modifier strings may contain <c>#</c> as a numeric placeholder
    /// (as returned by the PoE trade API), e.g. <c>Adds # to # Cold Damage</c>.
    /// These are converted to a regex pattern that matches the rolled value.
    /// Plain strings without <c>#</c> are matched via case-insensitive Contains.
    /// </para>
    /// </summary>
    public static List<ModCheckedItem> CheckMods(
        IEnumerable<StashItem> items, ModifierProfile profile)
    {
        // Pre-compile patterns once per call, pairing each with its base type constraint
        var patterns = profile.Modifiers
            .Where(p => !string.IsNullOrWhiteSpace(p.ModText))
            .Select(p => (
                ProfileMod: p,
                Regex: p.ModText.Contains('#') ? BuildModPattern(p.ModText) : null
            ))
            .ToList();

        if (patterns.Count == 0)
            return [];

        var result = new List<ModCheckedItem>();

        foreach (var item in items)
        {
            var allMods = new List<string>();
            if (item.ImplicitMods != null) allMods.AddRange(item.ImplicitMods);
            if (item.ExplicitMods  != null) allMods.AddRange(item.ExplicitMods);
            if (item.CraftedMods   != null) allMods.AddRange(item.CraftedMods);

            if (allMods.Count == 0 && item.FrameType is not (1 or 2 or 3))
                continue;

            // An item matches a ProfileMod when:
            //   1. The base type constraint is met (null/empty = any base)
            //   2. At least one of the item's mod lines matches the mod text pattern
            var itemBase = item.BaseType ?? item.TypeLine;

            var matched = allMods
                .Where(mod => patterns.Any(p =>
                {
                    // Base type gate — specific base takes priority, then group, then any
                    if (!string.IsNullOrEmpty(p.ProfileMod.BaseType))
                    {
                        if (!itemBase.Equals(p.ProfileMod.BaseType, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    else if (p.ProfileMod.BaseTypeGroup is { Count: > 0 })
                    {
                        if (!p.ProfileMod.BaseTypeGroup.Any(bt =>
                                bt.Equals(itemBase, StringComparison.OrdinalIgnoreCase)))
                            return false;
                    }

                    // Mod text match
                    return p.Regex is not null
                        ? p.Regex.IsMatch(mod)
                        : mod.Contains(p.ProfileMod.ModText, StringComparison.OrdinalIgnoreCase);
                }))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string displayName = !string.IsNullOrEmpty(item.Name)
                ? $"{item.Name}  •  {item.TypeLine}"
                : item.TypeLine;

            result.Add(new ModCheckedItem
            {
                Name            = displayName,
                Icon            = item.Icon,
                FrameType       = item.FrameType,
                IsMatch         = matched.Count > 0,
                MatchedModsText = string.Join("  |  ", matched),
                AllModsText     = string.Join("\n", allMods)
            });
        }

        return result
            .OrderByDescending(x => x.IsMatch)
            .ThenBy(x => x.Name)
            .ToList();
    }

    /// <summary>
    /// Converts a trade-API mod text (e.g. <c>Adds # to # Cold Damage</c>) into
    /// a compiled <see cref="Regex"/> that matches the rolled value variant.
    /// <c>#</c> matches integers and decimals; leading/trailing whitespace is ignored.
    /// </summary>
    private static Regex BuildModPattern(string modText)
    {
        // Escape all regex special chars, then restore # as a number wildcard
        var escaped = Regex.Escape(modText).Replace(@"\#", @"[\d.,]+");
        return new Regex(escaped, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private double? Resolve(StashItem item)
    {
        return item.FrameType switch
        {
            5 => prices.GetPrice(item.TypeLine),
            6 => prices.GetPrice(item.TypeLine),
            0 => prices.GetPrice(item.TypeLine),
            _ => null
        };
    }

    private static string GetCategory(int frameType) => frameType switch
    {
        5 => "Currency",
        6 => "Divination Card",
        0 => "Fragment",
        _ => "Other"
    };
}