using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalWealthTracker.Models;

// ── PoE Stash Tab API ───────────────────────────────────────────

public sealed class StashTabResponse
{
    [JsonPropertyName("numTabs")]
    public int NumTabs { get; set; }

    [JsonPropertyName("tabs")]
    public List<StashTab>? Tabs { get; set; }

    [JsonPropertyName("items")]
    public List<StashItem> Items { get; set; } = [];

    [JsonPropertyName("error")]
    public PoeApiError? Error { get; set; }
}

public sealed class PoeApiError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class StashTab
{
    [JsonPropertyName("n")]
    public string Name { get; set; } = "";

    [JsonPropertyName("i")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    [JsonPropertyName("colour")]
    public TabColour? Colour { get; set; }
}

public sealed class TabColour
{
    [JsonPropertyName("r")] public int R { get; set; }
    [JsonPropertyName("g")] public int G { get; set; }
    [JsonPropertyName("b")] public int B { get; set; }
}

public sealed class StashItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("typeLine")]
    public string TypeLine { get; set; } = "";

    [JsonPropertyName("baseType")]
    public string? BaseType { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("stackSize")]
    public int? StackSize { get; set; }

    /// <summary>
    /// 0=Normal, 1=Magic, 2=Rare, 3=Unique, 4=Gem,
    /// 5=Currency, 6=DivinationCard, 7=Quest, 8=Prophecy, 9=FoilUnique
    /// </summary>
    [JsonPropertyName("frameType")]
    public int FrameType { get; set; }

    [JsonPropertyName("identified")]
    public bool Identified { get; set; }

    [JsonPropertyName("corrupted")]
    public bool Corrupted { get; set; }

    [JsonPropertyName("properties")]
    public List<ItemProperty>? Properties { get; set; }

    [JsonPropertyName("sockets")]
    public List<ItemSocket>? Sockets { get; set; }
}

public sealed class ItemProperty
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("values")]
    public List<List<JsonElement>>? Values { get; set; }

    [JsonPropertyName("type")]
    public int? Type { get; set; }
}

public sealed class ItemSocket
{
    [JsonPropertyName("group")]
    public int Group { get; set; }

    [JsonPropertyName("attr")]
    public string? Attr { get; set; }

    [JsonPropertyName("sColour")]
    public string? Colour { get; set; }
}