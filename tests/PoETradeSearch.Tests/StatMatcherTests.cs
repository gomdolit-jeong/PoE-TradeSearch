using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>
/// Uses TestData/stats-subset.json: real entries copied from /api/trade/data/stats.
/// Set POE_STATS_JSON to a full download to also run <see cref="Full_stats_file_matches_sample_items"/> against it.
/// </summary>
public class StatMatcherTests
{
    private static readonly StatMatcher Matcher = new(
        TradeStatsLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "stats-subset.json"))));

    private static IReadOnlyList<MatchedMod> MatchItem(string itemClass, params string[] modLines)
    {
        var text = $"""
            Item Class: {itemClass}
            Rarity: Rare
            Test Name
            Test Base
            --------
            Item Level: 84
            --------
            {string.Join("\n", modLines)}
            """;
        return Matcher.Match(ItemTextParser.Parse(text));
    }

    [Fact]
    public void Matches_simple_explicit_mod_with_value()
    {
        var m = Assert.Single(MatchItem("Rings", "+52 to maximum Life"));

        Assert.Equal("explicit.stat_3299347043", m.Stat?.Id);
        Assert.Equal([52.0], m.Values);
        Assert.Equal(52.0, m.SearchValue);
    }

    [Fact]
    public void Uses_implicit_group_for_implicit_mods()
    {
        var m = Assert.Single(MatchItem("Rings", "+17% to Chaos Resistance (implicit)"));
        Assert.Equal("implicit.stat_2923486259", m.Stat?.Id);
    }

    [Fact]
    public void Uses_crafted_and_fractured_groups()
    {
        var matches = MatchItem("Rings", "+25% to Cold Resistance (crafted)", "+41% to Lightning Resistance (fractured)");

        Assert.StartsWith("crafted.", matches[0].Stat?.Id);
        Assert.StartsWith("fractured.", matches[1].Stat?.Id);
    }

    [Fact]
    public void Prefers_local_stats_on_weapons_and_armour()
    {
        var weapon = MatchItem("Two Hand Swords", "Adds 10 to 20 Physical Damage", "12% increased Attack Speed");
        Assert.Equal("Adds # to # Physical Damage (Local)", weapon[0].Stat?.Text);
        Assert.Equal("#% increased Attack Speed (Local)", weapon[1].Stat?.Text);

        var armour = MatchItem("Body Armours", "+98 to maximum Energy Shield", "84% increased Energy Shield");
        Assert.Equal("+# to maximum Energy Shield (Local)", armour[0].Stat?.Text);
        Assert.Equal("#% increased Energy Shield (Local)", armour[1].Stat?.Text);
    }

    [Fact]
    public void Uses_global_stats_on_jewellery()
    {
        var ring = MatchItem("Rings", "+40 to maximum Energy Shield", "Adds 3 to 7 Physical Damage to Attacks", "7% increased Attack Speed");

        Assert.Equal("+# to maximum Energy Shield", ring[0].Stat?.Text);
        Assert.Equal("Adds # to # Physical Damage to Attacks", ring[1].Stat?.Text);
        Assert.Equal("#% increased Attack Speed", ring[2].Stat?.Text);
    }

    [Fact]
    public void Search_value_of_added_damage_is_the_average()
    {
        var m = Assert.Single(MatchItem("Rings", "Adds 3 to 7 Physical Damage to Attacks"));

        Assert.Equal([3.0, 7.0], m.Values);
        Assert.Equal(5.0, m.SearchValue);
    }

    [Fact]
    public void Matches_stats_with_literal_numbers()
    {
        var m = Assert.Single(MatchItem("Belts", "When you Kill a Rare monster, you gain its Modifiers for 60 seconds"));

        Assert.Equal("explicit.stat_2913235441", m.Stat?.Id);
        Assert.Empty(m.Values);
        Assert.Null(m.SearchValue);
    }

    [Fact]
    public void Literal_numbers_must_match_exactly()
    {
        var m = Assert.Single(MatchItem("Belts", "When you Kill a Rare monster, you gain its Modifiers for 30 seconds"));
        Assert.False(m.IsMatched);
    }

    [Fact]
    public void Matches_opposite_wording_with_negated_value()
    {
        // The API only lists "#% reduced Mana Cost of Skills".
        var m = Assert.Single(MatchItem("Rings", "10% increased Mana Cost of Skills"));

        Assert.Equal("#% reduced Mana Cost of Skills", m.Stat?.Text);
        Assert.Equal([-10.0], m.Values);
    }

    [Fact]
    public void Joins_two_lines_for_multi_line_stats()
    {
        var matches = MatchItem("Maps",
            "Your Maps have +5% chance to contain other Extra Content that can",
            "be turned off through Atlas Passives",
            "+52 to maximum Life");

        Assert.Equal(2, matches.Count);
        Assert.Equal("explicit.stat_344979199", matches[0].Stat?.Id);
        Assert.Equal(2, matches[0].Source.Count);
        Assert.Equal([5.0], matches[0].Values);
        Assert.Equal("explicit.stat_3299347043", matches[1].Stat?.Id);
    }

    [Fact]
    public void Lists_duplicate_stat_ids_as_alternatives()
    {
        var m = Assert.Single(MatchItem("Gloves", "+12% chance to Suppress Spell Damage"));

        Assert.True(m.IsMatched);
        Assert.Single(m.AlternativeIds);
    }

    [Fact]
    public void Reports_unmatched_mods()
    {
        var m = Assert.Single(MatchItem("Rings", "Some mod that does not exist"));

        Assert.False(m.IsMatched);
        Assert.Equal("Some mod that does not exist", m.DisplayText);
    }

    [Fact]
    public void Full_stats_file_matches_sample_items()
    {
        var path = Environment.GetEnvironmentVariable("POE_STATS_JSON");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return; // Optional: only runs when a full stats download is provided.

        var full = new StatMatcher(TradeStatsLoader.Parse(File.ReadAllText(path)));
        var item = ItemTextParser.Parse("""
            Item Class: Belts
            Rarity: Unique
            Headhunter
            Leather Belt
            --------
            Item Level: 83
            --------
            +32 to maximum Life (implicit)
            --------
            +50 to Strength
            +45 to Dexterity
            +60 to maximum Life
            20% increased Damage with Hits against Rare monsters
            When you Kill a Rare monster, you gain its Modifiers for 60 seconds
            """);

        var armour = ItemTextParser.Parse("""
            Item Class: Body Armours
            Rarity: Rare
            Dusk Shell
            Vaal Regalia
            --------
            Item Level: 86
            --------
            +98 to maximum Energy Shield
            84% increased Energy Shield
            +41% to Lightning Resistance (fractured)
            +12% chance to Suppress Spell Damage
            10% reduced Mana Cost of Skills
            """);

        var ring = ItemTextParser.Parse("""
            Item Class: Rings
            Rarity: Rare
            Storm Loop
            Amethyst Ring
            --------
            Item Level: 84
            --------
            +17% to Chaos Resistance (implicit)
            --------
            +52 to maximum Life
            +38% to Fire Resistance
            Adds 3 to 7 Physical Damage to Attacks
            +25% to Cold Resistance (crafted)
            """);

        var matches = full.Match(item).Concat(full.Match(armour)).Concat(full.Match(ring)).ToList();
        Assert.All(matches, m => Assert.True(m.IsMatched, $"Not matched: {m.DisplayText}"));
        Assert.Equal("+# to maximum Energy Shield (Local)", full.Match(armour)[0].Stat?.Text);
    }
}
