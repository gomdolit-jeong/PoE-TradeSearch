using System.Text.Json.Nodes;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

public class TradeQueryBuilderTests
{
    private static ParsedItem Rare() => ItemTextParser.Parse("""
        Item Class: Rings
        Rarity: Rare
        Storm Loop
        Amethyst Ring
        --------
        Item Level: 84
        --------
        +52 to maximum Life
        """);

    [Fact]
    public void Sorts_by_price_ascending_and_includes_instant_buyout()
    {
        var body = TradeQueryBuilder.Build(new TradeQueryOptions { Item = Rare() });

        Assert.Equal("asc", body["sort"]!["price"]!.GetValue<string>());
        Assert.Equal("available", body["query"]!["status"]!["option"]!.GetValue<string>()); // instant buyout + in person
    }

    [Fact]
    public void Rare_uses_category_and_optional_base_type()
    {
        var withoutBase = TradeQueryBuilder.Build(new TradeQueryOptions { Item = Rare() });
        Assert.Equal("accessory.ring", withoutBase["query"]!["filters"]!["type_filters"]!["filters"]!["category"]!["option"]!.GetValue<string>());
        Assert.Null(withoutBase["query"]!["type"]);

        var withBase = TradeQueryBuilder.Build(new TradeQueryOptions { Item = Rare(), UseBaseType = true });
        Assert.Equal("Amethyst Ring", withBase["query"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Unique_searches_by_name_and_base()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Belts
            Rarity: Unique
            Headhunter
            Leather Belt
            --------
            Item Level: 83
            --------
            +50 to Strength
            """);
        var query = TradeQueryBuilder.Build(new TradeQueryOptions { Item = item })["query"]!;

        Assert.Equal("Headhunter", query["name"]!.GetValue<string>());
        Assert.Equal("Leather Belt", query["type"]!.GetValue<string>());
        Assert.Null(query["filters"]);
    }

    [Fact]
    public void Includes_only_enabled_stats_with_given_min_and_max()
    {
        var options = new TradeQueryOptions { Item = Rare(), ItemLevelMin = 82 };
        options.Stats.Add(new StatFilter { StatId = "explicit.stat_1", Enabled = true, Min = 50 });
        options.Stats.Add(new StatFilter { StatId = "explicit.stat_2", Enabled = true, Min = 5, Max = 9 });
        options.Stats.Add(new StatFilter { StatId = "explicit.stat_3", Enabled = true });
        options.Stats.Add(new StatFilter { StatId = "explicit.stat_4", Enabled = false, Min = 10 });

        var query = TradeQueryBuilder.Build(options)["query"]!;
        var filters = query["stats"]![0]!["filters"]!.AsArray();

        Assert.Equal(3, filters.Count);
        Assert.Equal(50, filters[0]!["value"]!["min"]!.GetValue<double>());
        Assert.Null(filters[0]!["value"]!["max"]);
        Assert.Equal(9, filters[1]!["value"]!["max"]!.GetValue<double>());
        Assert.Null(filters[2]!["value"]);
        Assert.Equal(82, query["filters"]!["misc_filters"]!["filters"]!["ilvl"]!["min"]!.GetValue<int>());
    }
}

public class ItemConditionTests
{
    private static ParsedItem FracturedShaperArmour() => ItemTextParser.Parse("""
        Item Class: Body Armours
        Rarity: Rare
        Dusk Shell
        Vaal Regalia
        --------
        Sockets: B-B-B-B-B B
        --------
        Item Level: 86
        --------
        +41% to Lightning Resistance (fractured)
        --------
        Shaper Item
        --------
        Fractured Item
        """);

    [Fact]
    public void Parser_reads_fractured_flag_influence_and_links()
    {
        var item = FracturedShaperArmour();

        Assert.True(item.FracturedItem);
        Assert.Equal(["shaper"], item.Influences);
        Assert.Equal(5, item.MaxLinks);
        Assert.Single(item.Mods);
    }

    [Fact]
    public void Defaults_keep_price_relevant_conditions()
    {
        var defaults = TradeQueryOptions.DefaultsFor(FracturedShaperArmour());

        Assert.True(defaults.FracturedItem);
        Assert.Equal(["shaper"], defaults.Influences);
        Assert.Equal(5, defaults.LinksMin);
        Assert.Null(defaults.Corrupted); // rares: corrupted state doesn't matter by default
        Assert.False(defaults.UseBaseType);
    }

    [Fact]
    public void Query_contains_misc_socket_and_influence_filters()
    {
        var options = TradeQueryOptions.DefaultsFor(FracturedShaperArmour());
        options.Corrupted = false;
        var query = TradeQueryBuilder.Build(options)["query"]!;
        var filters = query["filters"]!;

        Assert.Equal("true", filters["misc_filters"]!["filters"]!["fractured_item"]!["option"]!.GetValue<string>());
        Assert.Equal("false", filters["misc_filters"]!["filters"]!["corrupted"]!["option"]!.GetValue<string>());
        Assert.Equal(5, filters["socket_filters"]!["filters"]!["links"]!["min"]!.GetValue<int>());
        Assert.Contains(query["stats"]![0]!["filters"]!.AsArray(),
            f => f!["id"]!.GetValue<string>() == "pseudo.pseudo_has_shaper_influence");
    }

    [Fact]
    public void Fractured_stat_ids_convert_to_explicit()
    {
        Assert.Equal("explicit.stat_4220027924", TradeQueryBuilder.AsExplicit("fractured.stat_4220027924"));
        Assert.Equal("explicit.stat_1", TradeQueryBuilder.AsExplicit("explicit.stat_1"));
        Assert.Equal("crafted.stat_1", TradeQueryBuilder.AsExplicit("crafted.stat_1"));
    }

    [Fact]
    public void Fractured_item_filter_follows_the_option()
    {
        var options = TradeQueryOptions.DefaultsFor(FracturedShaperArmour());
        Assert.NotNull(TradeQueryBuilder.Build(options)["query"]!["filters"]!["misc_filters"]?["filters"]?["fractured_item"]);

        options.FracturedItem = false;
        Assert.Null(TradeQueryBuilder.Build(options)["query"]!["filters"]!["misc_filters"]?["filters"]?["fractured_item"]);
    }

    [Fact]
    public void Unique_defaults_match_corrupted_state()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Belts
            Rarity: Unique
            Headhunter
            Leather Belt
            --------
            Item Level: 83
            --------
            +50 to Strength
            """);
        Assert.False(TradeQueryOptions.DefaultsFor(item).Corrupted);

        item.Corrupted = true;
        Assert.True(TradeQueryOptions.DefaultsFor(item).Corrupted);
    }
}

public class ListingPropertiesTests
{
    [Theory]
    [InlineData("Level", "Quality")]
    [InlineData("레벨", "퀄리티")]
    public void Reads_gem_level_and_quality(string levelName, string qualityName)
    {
        var json = """
            {"result":[{"id":"g","listing":{"price":{"amount":9,"currency":"chaos"}},
              "item":{"typeLine":"Arc of Surging","ilvl":0,"corrupted":true,
                "properties":[{"name":"LEVEL","values":[["20 (Max)",0]]},{"name":"QUALITY","values":[["+23%",1]]}]}}]}
            """.Replace("LEVEL", levelName).Replace("QUALITY", qualityName);

        var listing = Assert.Single(TradeListing.ParseFetchResponse(json));
        Assert.Equal(20, listing.GemLevel);
        Assert.Equal(23, listing.Quality);
    }
}

public class ListingMarkupTests
{
    [Theory]
    [InlineData("[PlayerItemRarity|아이템 희귀도] +10%", "아이템 희귀도 +10%")]
    [InlineData("+10% increased [ItemRarity|Rarity of Items] found", "+10% increased Rarity of Items found")]
    [InlineData("Regenerate 1% of [Life] per second", "Regenerate 1% of Life per second")]
    [InlineData("+52 to maximum Life", "+52 to maximum Life")]
    public void Strips_link_markup(string input, string expected) =>
        Assert.Equal(expected, TradeListing.StripMarkup(input));
}

public class CurrencyCatalogTests
{
    [Fact]
    public void Builds_absolute_icon_urls()
    {
        var catalog = CurrencyCatalog.Parse("""
            {"result":[{"id":"Currency","label":"Currency","entries":[
              {"id":"chaos","text":"Chaos Orb","image":"/gen/image/abc/CurrencyRerollRare.png"},
              {"id":"divine","text":"Divine Orb"}]}]}
            """);

        Assert.Equal("https://web.poecdn.com/gen/image/abc/CurrencyRerollRare.png", catalog.Find("chaos")?.ImageUrl);
        Assert.Equal("Divine Orb", catalog.Find("DIVINE")?.Name);
        Assert.Null(catalog.Find("divine")?.ImageUrl);
        Assert.Null(catalog.Find("unknown"));
    }
}

/// <summary>Hits the real trade API. Runs only when POE_LIVE_TEST=1 so normal test runs stay offline.</summary>
public class LiveTradeApiTests
{
    [Theory]
    [InlineData("global", "maximum Life")]
    [InlineData("kakao", "생명력 최대치")]
    public async Task Searches_and_fetches_cheapest_first(string serverId, string lifeText)
    {
        if (Environment.GetEnvironmentVariable("POE_LIVE_TEST") != "1")
            return;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoETradeSearch/0.4");
        var api = new TradeApiClient(http, new RateLimiter(), TradeServer.FromId(serverId));

        var leagues = await api.GetLeaguesAsync();
        Assert.NotEmpty(leagues);

        var item = ItemTextParser.Parse("""
            Item Class: Rings
            Rarity: Rare
            Storm Loop
            Amethyst Ring
            --------
            Item Level: 84
            --------
            +70 to maximum Life
            Adds 3 to 7 Physical Damage to Attacks
            """);
        var options = new TradeQueryOptions { Item = item };
        options.Stats.Add(new StatFilter { StatId = "explicit.stat_3299347043", Enabled = true, Min = 70 });
        options.Stats.Add(new StatFilter { StatId = "explicit.stat_3032590688", Enabled = true, Min = 3 });

        var search = await api.SearchAsync(leagues[0].Id, TradeQueryBuilder.Build(options));
        Assert.NotEmpty(search.ResultIds);

        var listings = await api.FetchAsync(search.ResultIds, search.QueryId);
        Assert.NotEmpty(listings);
        Assert.All(listings, l => Assert.Contains(l.Mods, m => m.Text.Contains(lifeText)));
        Console.WriteLine($"[live {serverId}] total={search.Total} " +
                          string.Join(" | ", listings.Select(l => $"{l.PriceAmount} {l.PriceCurrency}")));
    }
}

public class TradeModeTests
{
    private static ParsedItem Ring() => ItemTextParser.Parse("""
        Item Class: Rings
        Rarity: Rare
        Storm Loop
        Amethyst Ring
        --------
        Item Level: 84
        --------
        +52 to maximum Life
        """);

    [Theory]
    [InlineData(TradeMode.InstantAndInPerson, "available")]
    [InlineData(TradeMode.InstantOnly, "securable")]
    [InlineData(TradeMode.InPersonOnly, "online")]
    [InlineData(TradeMode.Any, "any")]
    public void Trade_mode_maps_to_status(TradeMode mode, string status)
    {
        var body = TradeQueryBuilder.Build(new TradeQueryOptions { Item = Ring(), Mode = mode });
        Assert.Equal(status, body["query"]!["status"]!["option"]!.GetValue<string>());
    }


}

public class RateLimiterTests
{
    [Theory]
    [InlineData("5:10:60,15:60:300", "1:10:0,1:60:0", 0)]   // plenty left
    [InlineData("5:10:60,15:60:300", "5:10:0,6:60:0", 10)]  // 10 s window full → wait it out
    [InlineData("5:10:60,15:60:300", "3:10:0,15:60:0", 60)] // 60 s window full
    [InlineData("5:10:60,15:60:300", "6:10:60,6:60:0", 60)] // already banned for 60 s
    public void Computes_wait_from_headers(string limits, string state, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RateLimiter.ComputeBlock(limits, state));
    }
}

public class TradeListingTests
{
    // Shape copied from a real /api/trade/fetch response, with made-up seller and item data.
    private const string FetchJson = """
        {"result":[
          {"id":"aaa","listing":{"indexed":"2026-11-27T13:22:10Z",
             "price":{"type":"~price","amount":1.5,"currency":"divine"},
             "account":{"name":"tester#0001","online":{"league":"Covens"},"lastCharacterName":"TestChar"},
             "whisper":"@TestChar Hi, I would like to buy your Test Ring"},
           "item":{"name":"Test Loop","typeLine":"Amethyst Ring","ilvl":82,"corrupted":true,
             "implicitMods":[{"description":"+14% to Chaos Resistance","domain":"implicit"}],
             "explicitMods":[
               {"description":"+75 to maximum Life","domain":"explicit"},
               {"description":"Non-Channelling Skills have -7 to Total Mana Cost","flags":{"crafted":true},"domain":"crafted"}]}},
          {"id":"bbb","listing":{"price":{"amount":3,"currency":"chaos"},"account":{"name":"other#0002"}},
           "item":{"name":"","typeLine":"Amethyst Ring","explicitMods":["+60 to maximum Life"],"craftedMods":["+20% to Cold Resistance"]}},
          null
        ]}
        """;

    [Fact]
    public void Parses_price_seller_and_mods_in_both_formats()
    {
        var listings = TradeListing.ParseFetchResponse(FetchJson);

        Assert.Equal(2, listings.Count);

        var first = listings[0];
        Assert.Equal(1.5, first.PriceAmount);
        Assert.Equal("divine", first.PriceCurrency);
        Assert.Equal("TestChar", first.CharacterName);
        Assert.True(first.SellerOnline);
        Assert.True(first.Corrupted);
        Assert.Equal(82, first.ItemLevel);
        Assert.Equal(
            [("+14% to Chaos Resistance", ListingModKind.Implicit),
             ("+75 to maximum Life", ListingModKind.Explicit),
             ("Non-Channelling Skills have -7 to Total Mana Cost", ListingModKind.Crafted)],
            first.Mods.Select(m => (m.Text, m.Kind)));

        var second = listings[1];
        Assert.False(second.SellerOnline);
        Assert.Equal(
            [("+60 to maximum Life", ListingModKind.Explicit), ("+20% to Cold Resistance", ListingModKind.Crafted)],
            second.Mods.Select(m => (m.Text, m.Kind)));
    }
}
