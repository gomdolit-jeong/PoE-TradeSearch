using System.Text.Json.Nodes;
using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.Core.Trade;

/// <summary>Minimum sockets per colour; null = no requirement for that colour.</summary>
public readonly record struct SocketColours(int? Red, int? Green, int? Blue, int? White)
{
    public bool Any => Red is not null || Green is not null || Blue is not null || White is not null;
}

public enum TradeMode
{
    /// <summary>Instant buyout from merchant tabs (gold fee) plus online sellers for in-person trade.</summary>
    InstantAndInPerson,
    /// <summary>Only merchant tab listings: buy without the seller, pay a gold fee.</summary>
    InstantOnly,
    /// <summary>Only sellers online right now, traded by whisper.</summary>
    InPersonOnly,
    /// <summary>Everything, including offline sellers.</summary>
    Any,
}

/// <summary>One stat row in the search window: enabled flag plus optional min/max.</summary>
public sealed class StatFilter
{
    public required string StatId { get; init; }
    public bool Enabled { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
}

/// <summary>Everything needed to build a trade search request for one item.</summary>
public sealed class TradeQueryOptions
{
    public required ParsedItem Item { get; init; }
    public List<StatFilter> Stats { get; } = [];

    /// <summary>Search only this exact base type (e.g. "Amethyst Ring"). Uniques always use name + base.</summary>
    public bool UseBaseType { get; set; }

    public int? ItemLevelMin { get; set; }
    /// <summary>Which listings to show: merchant-tab instant buyouts (cost gold), in-person trades, or both.</summary>
    public TradeMode Mode { get; set; } = TradeMode.InstantAndInPerson;

    /// <summary>null = any, true = corrupted only, false = not corrupted.</summary>
    public bool? Corrupted { get; set; }

    public bool FracturedItem { get; set; }
    public bool Synthesised { get; set; }
    public bool SearingExarch { get; set; }
    public bool EaterOfWorlds { get; set; }

    /// <summary>Required influences ("shaper", "elder", ...). Each one must be present on the listing.</summary>
    public List<string> Influences { get; } = [];

    public int? LinksMin { get; set; }

    /// <summary>Total socket count minimum; null = any.</summary>
    public int? SocketsMin { get; set; }

    /// <summary>Minimum number of sockets per colour (R, G, B, W). Used only when the socket filter is on.</summary>
    public SocketColours SocketColours { get; set; }

    public int? QualityMin { get; set; }
    public int? QualityMax { get; set; }
    public int? GemLevelMin { get; set; }
    public int? GemLevelMax { get; set; }

    /// <summary>For transfigured gems: the base gem and discriminator (see <see cref="GemCatalog"/>).</summary>
    public GemCatalog.GemType? TransfiguredGem { get; set; }

    /// <summary>
    /// Sensible defaults for an item: keep properties that change the price a lot
    /// (fractured, synthesised, influences, 5-6 links), and for uniques and gems match the corrupted state.
    /// Gems also keep their level and quality as minimums.
    /// </summary>
    public static TradeQueryOptions DefaultsFor(ParsedItem item, GemCatalog? gems = null)
    {
        bool gem = item.Rarity == ItemRarity.Gem;
        var options = new TradeQueryOptions
        {
            Item = item,
            UseBaseType = item.Rarity is not (ItemRarity.Rare or ItemRarity.Magic),
            FracturedItem = item.FracturedItem,
            Synthesised = item.Synthesised,
            SearingExarch = item.SearingExarch,
            EaterOfWorlds = item.EaterOfWorlds,
            LinksMin = item.MaxLinks >= 5 ? item.MaxLinks : null,
            // White sockets are rare and valuable (e.g. Dialla's Malefaction), so they are kept by default.
            SocketColours = item.SocketsOfColour('W') > 0 ? new SocketColours(null, null, null, item.SocketsOfColour('W')) : default,
            Corrupted = item.Rarity is ItemRarity.Unique or ItemRarity.Gem ? item.Corrupted : null,
            ItemLevelMin = ModDefaults.ClusterItemLevelMin(item),
            GemLevelMin = gem ? item.GemLevel : null,
            QualityMin = gem && item.Quality > 0 ? item.Quality : null,
            TransfiguredGem = gem ? gems?.FindTransfigured(item.BaseType) : null,
        };
        options.Influences.AddRange(item.Influences);
        return options;
    }
}

public static class TradeQueryBuilder
{
    /// <summary>Builds the JSON body for POST /api/trade/search/{league}, sorted by price ascending.</summary>
    public static JsonObject Build(TradeQueryOptions options)
    {
        var item = options.Item;
        var query = new JsonObject
        {
            ["status"] = new JsonObject { ["option"] = StatusOption(options.Mode) },
        };

        switch (item.Rarity)
        {
            case ItemRarity.Unique when item.Name is not null:
                query["name"] = item.Name;
                if (item.BaseType is not null) query["type"] = item.BaseType;
                break;

            case ItemRarity.Rare or ItemRarity.Normal:
                if (options.UseBaseType && item.BaseType is not null)
                    query["type"] = item.BaseType;
                break;

            case ItemRarity.Magic:
                // The magic item's name includes prefix/suffix words, so it can't be used as a base type.
                break;

            case ItemRarity.Gem when options.TransfiguredGem is { } transfigured:
                query["type"] = new JsonObject
                {
                    ["option"] = transfigured.Type,
                    ["discriminator"] = transfigured.Discriminator,
                };
                break;

            default:
                // Gems, currency, divination cards, unidentified uniques: the base type is the item.
                if (item.BaseType is not null) query["type"] = item.BaseType;
                break;
        }

        var filters = new JsonObject();

        var typeFilters = new JsonObject();
        var category = ItemCategories.For(item);
        if (category is not null && item.Rarity is not (ItemRarity.Unique or ItemRarity.Gem or ItemRarity.Currency or ItemRarity.DivinationCard))
            typeFilters["category"] = new JsonObject { ["option"] = category };
        if (typeFilters.Count > 0)
            filters["type_filters"] = new JsonObject { ["filters"] = typeFilters };

        var miscFilters = new JsonObject();
        if (options.ItemLevelMin is { } ilvl)
            miscFilters["ilvl"] = new JsonObject { ["min"] = ilvl };
        if (MinMax(options.GemLevelMin, options.GemLevelMax) is { } gemLevel)
            miscFilters["gem_level"] = gemLevel;
        if (MinMax(options.QualityMin, options.QualityMax) is { } quality)
            miscFilters["quality"] = quality;
        if (options.Corrupted is { } corrupted)
            miscFilters["corrupted"] = Option(corrupted);
        if (options.FracturedItem) miscFilters["fractured_item"] = Option(true);
        if (options.Synthesised) miscFilters["synthesised_item"] = Option(true);
        if (options.SearingExarch) miscFilters["searing_item"] = Option(true);
        if (options.EaterOfWorlds) miscFilters["tangled_item"] = Option(true);
        if (miscFilters.Count > 0)
            filters["misc_filters"] = new JsonObject { ["filters"] = miscFilters };

        var socketFilters = new JsonObject();
        if (options.SocketsMin is not null || options.SocketColours.Any)
        {
            var sockets = new JsonObject();
            if (options.SocketsMin is { } count) sockets["min"] = count;
            if (options.SocketColours.Red is { } r) sockets["r"] = r;
            if (options.SocketColours.Green is { } g) sockets["g"] = g;
            if (options.SocketColours.Blue is { } b) sockets["b"] = b;
            if (options.SocketColours.White is { } w) sockets["w"] = w;
            socketFilters["sockets"] = sockets;
        }
        if (options.LinksMin is { } links)
            socketFilters["links"] = new JsonObject { ["min"] = links };
        if (socketFilters.Count > 0)
            filters["socket_filters"] = new JsonObject { ["filters"] = socketFilters };

        if (filters.Count > 0)
            query["filters"] = filters;

        var statFilters = new JsonArray();
        foreach (var stat in options.Stats.Where(s => s.Enabled))
        {
            var filter = new JsonObject { ["id"] = stat.StatId };
            var value = new JsonObject();
            if (stat.Min is { } min) value["min"] = min;
            if (stat.Max is { } max) value["max"] = max;
            if (value.Count > 0) filter["value"] = value;
            statFilters.Add(filter);
        }
        // Influences are searched as pseudo stats without a value.
        foreach (var influence in options.Influences)
            statFilters.Add(new JsonObject { ["id"] = $"pseudo.pseudo_has_{influence}_influence" });
        query["stats"] = new JsonArray(new JsonObject { ["type"] = "and", ["filters"] = statFilters });

        return new JsonObject
        {
            ["query"] = query,
            ["sort"] = new JsonObject { ["price"] = "asc" },
        };
    }

    private static JsonObject Option(bool value) => new() { ["option"] = value ? "true" : "false" };

    /// <summary>The trade site's status filter values (see /api/trade/data/filters, "status_filters").</summary>
    public static string StatusOption(TradeMode mode) => mode switch
    {
        TradeMode.InstantAndInPerson => "available",
        TradeMode.InstantOnly => "securable",
        TradeMode.InPersonOnly => "online",
        _ => "any",
    };

    private static JsonObject? MinMax(int? min, int? max)
    {
        if (min is null && max is null)
            return null;
        var o = new JsonObject();
        if (min is { } a) o["min"] = a;
        if (max is { } b) o["max"] = b;
        return o;
    }

    private const string FracturedPrefix = "fractured.";

    /// <summary>
    /// The same mod as a normal explicit ("fractured.stat_X" → "explicit.stat_X"), so items where it isn't
    /// fractured show up too. Every fractured stat on the trade site has an explicit twin with the same number.
    /// </summary>
    public static string AsExplicit(string statId) =>
        statId.StartsWith(FracturedPrefix, StringComparison.Ordinal)
            ? "explicit." + statId[FracturedPrefix.Length..]
            : statId;
}
