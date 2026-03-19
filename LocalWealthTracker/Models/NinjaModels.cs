using System.Text.Json.Serialization;

namespace LocalWealthTracker.Models;

/// <summary>
/// Root response from the poe.ninja exchange endpoint.
/// URL: https://poe.ninja/poe1/api/economy/exchange/current/overview?league={league}&amp;type={type}
/// 
/// Structure:
///   core   → divine/chaos ratio + base currency definitions
///   lines  → price data keyed by id (primaryValue = chaos price)
///   items  → metadata keyed by id (name, image, category)
/// 
/// We join lines + items on their shared "id" field.
/// </summary>
public sealed class ExchangeResponse
{
    [JsonPropertyName("core")]
    public ExchangeCore? Core { get; set; }

    [JsonPropertyName("lines")]
    public List<ExchangeLine> Lines { get; set; } = [];

    [JsonPropertyName("items")]
    public List<ExchangeItem> Items { get; set; } = [];
}

public sealed class ExchangeCore
{
    [JsonPropertyName("items")]
    public List<CoreItem> Items { get; set; } = [];

    [JsonPropertyName("rates")]
    public ExchangeRates? Rates { get; set; }

    [JsonPropertyName("primary")]
    public string Primary { get; set; } = "";       // "chaos"

    [JsonPropertyName("secondary")]
    public string Secondary { get; set; } = "";     // "divine"
}

public sealed class ExchangeRates
{
    /// <summary>
    /// Chaos-to-divine ratio. Example: 0.002983 means 1 chaos = 0.002983 divine,
    /// therefore 1 divine = 1 / 0.002983 ≈ 335 chaos.
    /// </summary>
    [JsonPropertyName("divine")]
    public double Divine { get; set; }
}

public sealed class CoreItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";
}

/// <summary>
/// A price entry. Joined with ExchangeItem by the "id" field.
/// </summary>
public sealed class ExchangeLine
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Price in chaos orbs.</summary>
    [JsonPropertyName("primaryValue")]
    public double PrimaryValue { get; set; }

    [JsonPropertyName("sparkline")]
    public Sparkline? Sparkline { get; set; }
}

public sealed class Sparkline
{
    [JsonPropertyName("totalChange")]
    public double TotalChange { get; set; }

    [JsonPropertyName("data")]
    public List<double?> Data { get; set; } = [];
}

/// <summary>
/// Item metadata. Joined with ExchangeLine by the "id" field.
/// </summary>
public sealed class ExchangeItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("detailsId")]
    public string DetailsId { get; set; } = "";
}