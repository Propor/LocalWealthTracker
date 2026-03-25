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