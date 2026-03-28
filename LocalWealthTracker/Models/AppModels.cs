using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalWealthTracker.Models;

public sealed class PricedItem
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string? Icon { get; init; }
    public string TabName { get; init; } = "";
    public int TabIndex { get; init; } = -1;
    public int Quantity { get; init; } = 1;
    public double UnitPriceChaos { get; init; }
    public double TotalPriceChaos { get; init; }
    public double TotalPriceDivine { get; init; }
    public double DivinePrice { get; init; }

    // ── Sparkline (not serialized into snapshots) ───────────────

    [JsonIgnore]
    public List<double?>? SparklineData { get; set; }

    [JsonIgnore]
    public double SparklineTrend { get; set; }

    [JsonIgnore]
    public bool IsSparklineUp => SparklineTrend >= 0;

    [JsonIgnore]
    public bool HasSparkline =>
        SparklineData is { Count: >= 2 };

    [JsonIgnore]
    public string SparklineText =>
HasSparkline
    ? SparklineTrend >= 0
        ? $"+{SparklineTrend:N1}%"
        : $"{SparklineTrend:N1}%"
    : "";

    // ── Display ─────────────────────────────────────────────────

    public string UnitDisplay =>
        DivinePrice > 0 && UnitPriceChaos >= DivinePrice
            ? $"{(UnitPriceChaos / DivinePrice).ToString("N1", CultureInfo.InvariantCulture)} div"
            : $"{UnitPriceChaos.ToString("N1", CultureInfo.InvariantCulture)}c";



}

/// <summary>An item that couldn't be matched to a poe.ninja price.</summary>
public sealed class UnpricedItem
{
    public string Name { get; init; } = "";
    public string? Icon { get; init; }
    public string TabName { get; init; } = "";
    public int Quantity { get; init; } = 1;
    public int FrameType { get; init; }
    public string Category { get; init; } = "";
}

public sealed class TabSummary : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Name { get; init; } = "";
    public int Index { get; init; }
    public string Type { get; init; } = "";
    public Color Color { get; init; }
    public List<PricedItem> Items { get; set; } = [];

    // ── Modifier profile ────────────────────────────────────────
    public string? ModifierProfileId { get; set; }
    public string? ModifierProfileName { get; set; }
    public List<ModCheckedItem> ModItems { get; set; } = [];
    public bool IsModCheckerTab => ModifierProfileId != null;

    private double _totalChaos;
    public double TotalChaos
    {
        get => _totalChaos;
        set { if (SetProperty(ref _totalChaos, value)) OnPropertyChanged(nameof(Summary)); }
    }

    private double _totalDivine;
    public double TotalDivine
    {
        get => _totalDivine;
        set { if (SetProperty(ref _totalDivine, value)) OnPropertyChanged(nameof(Summary)); }
    }

    private int _itemCount;
    public int ItemCount
    {
        get => _itemCount;
        set { if (SetProperty(ref _itemCount, value)) OnPropertyChanged(nameof(Summary)); }
    }

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    public string Summary =>
        $"{TotalDivine:N1} div  ({TotalChaos:N0}c)  •  {ItemCount} items";
}

public sealed class WealthSnapshot
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public double TotalChaos { get; set; }
    public double TotalDivine { get; set; }
    public string League { get; set; } = "";
    public string Note { get; set; } = "";
    public List<PricedItem> Items { get; set; } = [];

    [JsonIgnore]
    public bool HasPrevious { get; set; }

    [JsonIgnore]
    public double PercentChange { get; set; }

    [JsonIgnore]
    public bool IsUp => PercentChange >= 0;

    [JsonIgnore]
    public string PercentChangeText =>
        !HasPrevious ? "" :
        PercentChange >= 0 ? $"▲+{PercentChange:N1}%" : $"▼{PercentChange:N1}%";

    [JsonIgnore]
    public string Display =>
        $"{Timestamp:g}   {TotalDivine:N1} div  ({TotalChaos:N0}c)";
}

public partial class SelectableTab : ObservableObject
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public Color Color { get; init; }

    [ObservableProperty]
    private bool _isSynced;

    public string Display => $"{Name}  ({Type})";
}

public sealed class SavedTab
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public int ColorR { get; set; }
    public int ColorG { get; set; }
    public int ColorB { get; set; }
    public bool IsSynced { get; set; }
    public string? ModifierProfileId { get; set; }
}

public sealed class AppSettings
{
    public string League { get; set; } = "";
    public double MinItemValueChaos { get; set; } = 1.0;
    public int AutoRefreshMinutes { get; set; }
    public int PriceCacheMinutes { get; set; } = 10;
    public double DivineGoal { get; set; } = 0;
    public List<SavedTab> Tabs { get; set; } = [];
    public List<string> TabOrder { get; set; } = [];
    public List<ModifierProfile> ModifierProfiles { get; set; } = [];
}

// ── Modifier profiles ────────────────────────────────────────────────────────

public sealed class ProfileMod
{
    [JsonPropertyName("modText")]
    public string ModText { get; set; } = "";

    /// <summary>Exact base type (e.g. "Astral Plate"). Null means use BaseTypeGroup or any.</summary>
    [JsonPropertyName("baseType")]
    public string? BaseType { get; set; }

    /// <summary>When set, matches any item whose base type is in this group (e.g. all Armour bases).
    /// Populated at add-time from the trade items API so no external lookup is needed at match-time.</summary>
    [JsonPropertyName("baseTypeGroup")]
    public List<string>? BaseTypeGroup { get; set; }

    /// <summary>Display label for the group constraint (e.g. "Armour").</summary>
    [JsonPropertyName("baseGroupLabel")]
    public string? BaseGroupLabel { get; set; }

    [JsonIgnore]
    public bool HasBaseConstraint =>
        !string.IsNullOrEmpty(BaseType) || !string.IsNullOrEmpty(BaseGroupLabel);

    [JsonIgnore]
    public string BadgeText =>
        !string.IsNullOrEmpty(BaseType) ? BaseType! : $"Any {BaseGroupLabel}";

    [JsonIgnore]
    public string DisplayText =>
        !string.IsNullOrEmpty(BaseType)       ? $"[{BaseType}]  {ModText}" :
        !string.IsNullOrEmpty(BaseGroupLabel) ? $"[Any {BaseGroupLabel}]  {ModText}" :
        ModText;
}

public sealed class ModifierProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ProfileMod> Modifiers { get; set; } = [];
}

public sealed class ModCheckedItem
{
    public string Name { get; init; } = "";
    public string? Icon { get; init; }
    public int FrameType { get; init; }
    public bool IsMatch { get; init; }
    /// <summary>Matched mod lines joined with " | ".</summary>
    public string MatchedModsText { get; init; } = "";
    /// <summary>All mods joined with newline (used as tooltip).</summary>
    public string AllModsText { get; init; } = "";

    public string RarityLabel => FrameType switch
    {
        1 => "Magic",
        2 => "Rare",
        3 => "Unique",
        4 => "Gem",
        _ => "Normal"
    };

    public string StatusText => IsMatch ? $"✓  {MatchedModsText}" : "—";
}

public sealed class DiffItem
{
    public string Name { get; init; } = "";
    public string? Icon { get; init; }
    public int OldQty { get; init; }
    public int NewQty { get; init; }
    public int QtyChange => NewQty - OldQty;
    public double OldValueChaos { get; init; }
    public double NewValueChaos { get; init; }
    public double ValueChangeChaos => Math.Round(NewValueChaos - OldValueChaos, 1);
    public double OldValueDivine { get; init; }
    public double NewValueDivine { get; init; }
    public double ValueChangeDivine => Math.Round(NewValueDivine - OldValueDivine, 2);
    public bool IsGain => ValueChangeChaos >= 0;
    public string QtyText => $"{OldQty} → {NewQty}";
    public string ValueChangeText => ValueChangeDivine >= 0
        ? $"+{ValueChangeDivine:N2}d"
        : $"{ValueChangeDivine:N2}d";
}