using System.Text.Json.Serialization;

namespace LocalWealthTracker.Models;

// ── PoE Trade Stats API (/api/trade/data/stats) ──────────────────────────────

public sealed class TradeStatsResponse
{
    [JsonPropertyName("result")]
    public List<TradeStatGroup> Result { get; set; } = [];
}

public sealed class TradeStatGroup
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("entries")]
    public List<TradeStatEntry> Entries { get; set; } = [];
}

public sealed class TradeStatEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    // Populated after deserialization from the parent group's Label
    public string GroupLabel { get; set; } = "";
}

// ── PoE Trade Items API (/api/trade/data/items) ───────────────────────────────

public sealed class TradeItemsResponse
{
    [JsonPropertyName("result")]
    public List<TradeItemGroup> Result { get; set; } = [];
}

public sealed class TradeItemGroup
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("entries")]
    public List<TradeItemEntry> Entries { get; set; } = [];
}

public sealed class TradeItemEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    // Populated after deserialization
    public string GroupLabel { get; set; } = "";
}
