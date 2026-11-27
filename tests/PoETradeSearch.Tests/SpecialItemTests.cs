using System.Text.Json.Nodes;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>Gems, quality, and uniques/jewels whose variant is decided by one mod. Stat ids are real trade ids.</summary>
public class SpecialItemTests
{
    private const string TransfiguredGem = """
        Item Class: Skill Gems
        Rarity: Gem
        Arc of Surging
        --------
        Lightning, Spell, Chaining
        Level: 20 (Max)
        Cost: 18 Mana
        Cast Time: 0.70 sec
        Quality: +20% (augmented)
        --------
        Requirements:
        Level: 70
        Int: 155
        --------
        An arc of lightning stretches from the caster to a targeted enemy.
        --------
        Experience: 1/1
        --------
        Place into an item socket of the right colour to gain this skill. Right click to remove from a socket.
        --------
        Corrupted
        """;

    private const string KoreanGem = """
        아이템 종류: 스킬 젬
        아이템 희귀도: 젬
        뇌동의 연쇄 번개
        --------
        번개, 주문, 연쇄
        레벨: 18
        퀄리티: +13% (augmented)
        --------
        요구 사항:
        레벨: 64
        --------
        설명
        """;

    private const string ForbiddenFlame = """
        Item Class: Jewels
        Rarity: Unique
        Forbidden Flame
        Crimson Jewel
        --------
        Limited to: 1
        --------
        Item Level: 84
        --------
        Allocates Tasalio, Cleansing Water if you have the matching modifier on Forbidden Flesh
        --------
        Purity reveals the true path
        --------
        Place into an allocated Jewel Socket on the Passive Skill Tree. Right click to remove from the Socket.
        """;

    private const string Voices = """
        Item Class: Jewels
        Rarity: Unique
        Voices
        Large Cluster Jewel
        --------
        Item Level: 84
        --------
        Adds 7 Passive Skills (enchant)
        --------
        Adds 3 Jewel Socket Passive Skills
        Adds 5 Small Passive Skills which grant nothing
        --------
        The voices whisper secrets of power
        """;

    private const string RareCluster = """
        Item Class: Jewels
        Rarity: Rare
        Grim Spark
        Medium Cluster Jewel
        --------
        Item Level: 84
        --------
        Adds 5 Passive Skills (enchant)
        Added Small Passive Skills grant: 10% increased Spell Damage (enchant)
        --------
        Added Small Passive Skills also grant: +5 to Maximum Mana
        """;

    private static readonly StatMatcher Matcher = new(
    [
        new("explicit.stat_1190333629|48480", "Allocates Tasalio, Cleansing Water if you have the matching modifier on Forbidden Flesh", "explicit"),
        new("explicit.stat_2460506030|48480", "Allocates Tasalio, Cleansing Water if you have the matching modifier on Forbidden Flame", "explicit"),
        new("explicit.stat_247746531", "Adds # Jewel Socket Passive Skills", "explicit"),
        new("explicit.stat_1085446536", "Adds # Small Passive Skills which grant nothing", "explicit"),
        new("enchant.stat_3086156145", "Adds # Passive Skills", "enchant"),
        new("enchant.stat_3948993189|10", "Added Small Passive Skills grant: 10% increased Spell Damage", "enchant"),
        new("explicit.stat_1985969957", "Added Small Passive Skills also grant: +# to Maximum Mana", "explicit"),
        new("explicit.stat_3642528642|3", "Only affects Passives in Large Ring", "explicit"),
    ]);

    // ---- Gems ----

    [Fact]
    public void Parses_gem_level_quality_and_corruption()
    {
        var gem = ItemTextParser.Parse(TransfiguredGem);

        Assert.Equal(ItemRarity.Gem, gem.Rarity);
        Assert.Equal("Arc of Surging", gem.BaseType);
        Assert.Equal(20, gem.GemLevel);   // "Level: 20 (Max)", not the requirement level 70
        Assert.Equal(20, gem.Quality);
        Assert.True(gem.Corrupted);
        Assert.Empty(gem.Mods);

        var ko = ItemTextParser.Parse(KoreanGem);
        Assert.Equal("뇌동의 연쇄 번개", ko.BaseType);
        Assert.Equal(18, ko.GemLevel);
        Assert.Equal(13, ko.Quality);
    }

    [Fact]
    public void Transfigured_gem_query_uses_base_gem_with_discriminator()
    {
        var catalog = GemCatalog.Parse("""
            {"result":[{"id":"gem","label":"Gems","entries":[
              {"type":"Arc"},
              {"type":"Arc","text":"Arc of Surging","disc":"alt_x"},
              {"type":"연쇄 번개","text":"뇌동의 연쇄 번개","disc":"alt_y"}]}]}
            """);
        var options = TradeQueryOptions.DefaultsFor(ItemTextParser.Parse(TransfiguredGem), catalog);
        var query = TradeQueryBuilder.Build(options)["query"]!;

        Assert.Equal("Arc", query["type"]!["option"]!.GetValue<string>());
        Assert.Equal("alt_x", query["type"]!["discriminator"]!.GetValue<string>());
        var misc = query["filters"]!["misc_filters"]!["filters"]!;
        Assert.Equal(20, misc["gem_level"]!["min"]!.GetValue<int>());
        Assert.Equal(20, misc["quality"]!["min"]!.GetValue<int>());
        Assert.Equal("true", misc["corrupted"]!["option"]!.GetValue<string>());
        Assert.Null(query["filters"]!["type_filters"]); // no item category for gems

        Assert.Equal(new GemCatalog.GemType("연쇄 번개", "alt_y"), catalog.FindTransfigured("뇌동의 연쇄 번개"));
    }

    [Fact]
    public void Normal_gem_query_uses_plain_type()
    {
        var gem = ItemTextParser.Parse(KoreanGem.Replace("뇌동의 연쇄 번개", "연쇄 번개"));
        var query = TradeQueryBuilder.Build(TradeQueryOptions.DefaultsFor(gem, GemCatalog.Empty))["query"]!;

        Assert.Equal("연쇄 번개", query["type"]!.GetValue<string>());
        Assert.Equal("false", query["filters"]!["misc_filters"]!["filters"]!["corrupted"]!["option"]!.GetValue<string>());
    }

    [Fact]
    public void Quality_min_and_max_go_to_misc_filters()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Body Armours
            Rarity: Rare
            Dusk Shell
            Vaal Regalia
            --------
            Quality: +28% (augmented)
            --------
            Item Level: 86
            --------
            +98 to maximum Energy Shield
            """);
        Assert.Equal(28, item.Quality);

        var options = TradeQueryOptions.DefaultsFor(item);
        Assert.Null(options.QualityMin); // off by default for equipment
        options.QualityMin = 20;
        options.QualityMax = 28;
        var quality = TradeQueryBuilder.Build(options)["query"]!["filters"]!["misc_filters"]!["filters"]!["quality"]!;
        Assert.Equal(20, quality["min"]!.GetValue<int>());
        Assert.Equal(28, quality["max"]!.GetValue<int>());
    }

    // ---- Uniques and jewels defined by one mod ----

    [Fact]
    public void Forbidden_flame_mod_without_numbers_is_parsed_matched_and_ticked()
    {
        var item = ItemTextParser.Parse(ForbiddenFlame);
        var mod = Assert.Single(item.Mods); // flavour text and usage hint are not mods
        Assert.StartsWith("Allocates Tasalio", mod.Text);

        var match = Assert.Single(Matcher.Match(item));
        Assert.Equal("explicit.stat_1190333629|48480", match.Stat?.Id);
        Assert.True(ModDefaults.EnabledByDefault(item, match));
        Assert.False(ModDefaults.UseExactValue(match));
    }

    [Fact]
    public void Voices_socket_counts_are_ticked_and_exact()
    {
        var item = ItemTextParser.Parse(Voices);
        var matches = Matcher.Match(item);

        Assert.Equal(3, matches.Count);
        Assert.All(matches, m => Assert.True(m.IsMatched, m.DisplayText));
        // Enchant "Adds 7 Passive Skills" on a cluster jewel, and both explicit counts.
        Assert.All(matches, m => Assert.True(ModDefaults.EnabledByDefault(item, m), m.DisplayText));
        Assert.All(matches, m => Assert.True(ModDefaults.UseExactValue(m), m.DisplayText));
        Assert.Equal("jewel.cluster", ItemCategories.For(item));
    }

    [Fact]
    public void Rare_cluster_jewel_ticks_its_enchants()
    {
        var item = ItemTextParser.Parse(RareCluster);
        var matches = Matcher.Match(item);

        Assert.Equal("enchant.stat_3948993189|10", matches[1].Stat?.Id);
        Assert.True(ModDefaults.EnabledByDefault(item, matches[0])); // Adds 5 Passive Skills
        Assert.True(ModDefaults.EnabledByDefault(item, matches[1])); // small passive type
        Assert.Equal(3, matches.Count);
        Assert.True(ModDefaults.EnabledByDefault(item, matches[2])); // rolled explicit "…also grant: +5 to Maximum Mana"
        Assert.Equal("jewel.cluster", ItemCategories.For(item));
    }

    [Theory]
    [InlineData(86, 84)]
    [InlineData(84, 84)]
    [InlineData(83, 75)]
    [InlineData(75, 75)]
    [InlineData(70, 68)]
    [InlineData(55, 50)]
    [InlineData(40, null)]
    public void Cluster_jewel_item_level_uses_bracket_start(int ilvl, int? expected)
    {
        var item = ItemTextParser.Parse(RareCluster.Replace("Item Level: 84", $"Item Level: {ilvl}"));
        var options = TradeQueryOptions.DefaultsFor(item);

        Assert.Equal(expected, options.ItemLevelMin);
        var misc = TradeQueryBuilder.Build(options)["query"]!["filters"]!["misc_filters"]?["filters"];
        Assert.Equal(expected, misc?["ilvl"]?["min"]?.GetValue<int>());
    }

    [Fact]
    public void Item_level_is_not_required_for_other_items_or_unique_clusters()
    {
        Assert.Null(TradeQueryOptions.DefaultsFor(ItemTextParser.Parse(Voices)).ItemLevelMin);
        Assert.Null(TradeQueryOptions.DefaultsFor(ItemTextParser.Parse(ForbiddenFlame)).ItemLevelMin);
    }

    [Fact]
    public void Thread_of_hope_ring_size_is_ticked()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Jewels
            Rarity: Unique
            Thread of Hope
            Crimson Jewel
            --------
            Item Level: 84
            --------
            Only affects Passives in Large Ring
            Passives in Radius can be Allocated without being connected to your tree
            -12% to all Elemental Resistances
            """);

        var ring = Matcher.Match(item)[0];
        Assert.Equal("explicit.stat_3642528642|3", ring.Stat?.Id);
        Assert.True(ModDefaults.EnabledByDefault(item, ring));
    }

    private const string Megalomaniac = """
        Item Class: Jewels
        Rarity: Unique
        Megalomaniac
        Medium Cluster Jewel
        --------
        Item Level: 84
        --------
        Adds 4 Passive Skills
        Added Small Passive Skills grant Nothing
        1 Added Passive Skill is Pure Agony
        1 Added Passive Skill is Disorienting Display
        1 Added Passive Skill is Wasting Affliction
        --------
        The chosen one
        """;

    private const string KoreanMegalomaniac = """
        아이템 종류: 주얼
        아이템 희귀도: 고유
        과대망상
        중형 스킬 군 주얼
        --------
        아이템 레벨: 84
        --------
        패시브 스킬 4개 추가
        순수한 고통 패시브 스킬 1개 추가
        혼란스러운 과시 패시브 스킬 1개 추가
        """;

    [Fact]
    public void Megalomaniac_notables_are_matched_and_ticked()
    {
        var matcher = new StatMatcher(
        [
            new("explicit.stat_3086156145", "Adds # Passive Skills", "explicit"),
            new("explicit.stat_2557943734", "Added Small Passive Skills grant Nothing", "explicit"),
            new("explicit.stat_1507409483", "1 Added Passive Skill is Pure Agony", "explicit"),
            new("explicit.stat_3206911230", "1 Added Passive Skill is Disorienting Display", "explicit"),
            new("explicit.stat_2066820199", "1 Added Passive Skill is Wasting Affliction", "explicit"),
        ]);
        var item = ItemTextParser.Parse(Megalomaniac);
        var matches = matcher.Match(item);

        Assert.Equal(5, matches.Count);
        Assert.Equal(["explicit.stat_1507409483", "explicit.stat_3206911230", "explicit.stat_2066820199"],
                     matches.Skip(2).Select(m => m.Stat?.Id));
        // The three notables decide what the jewel is worth, so they are ticked.
        Assert.All(matches.Skip(2), m => Assert.True(ModDefaults.EnabledByDefault(item, m), m.DisplayText));
        // "Adds 4 Passive Skills" (an explicit mod on Megalomaniac): ticked and exact.
        Assert.True(ModDefaults.EnabledByDefault(item, matches[0]));
        Assert.True(ModDefaults.UseExactValue(matches[0]));
    }

    [Fact]
    public void Korean_megalomaniac_matches_with_literal_count_in_text()
    {
        var matcher = new StatMatcher(
        [
            new("explicit.stat_3086156145", "패시브 스킬 #개 추가", "explicit"),
            new("explicit.stat_1507409483", "순수한 고통 패시브 스킬 1개 추가", "explicit"),
            new("explicit.stat_3206911230", "혼란스러운 과시 패시브 스킬 1개 추가", "explicit"),
        ]);
        var item = ItemTextParser.Parse(KoreanMegalomaniac);
        var matches = matcher.Match(item);

        Assert.Equal(["explicit.stat_3086156145", "explicit.stat_1507409483", "explicit.stat_3206911230"],
                     matches.Select(m => m.Stat?.Id));
        Assert.All(matches, m => Assert.True(ModDefaults.EnabledByDefault(item, m), m.DisplayText));
        Assert.Equal("jewel.cluster", ItemCategories.For(item));
    }

    [Fact]
    public void Unique_mods_with_rolled_numbers_stay_unticked()
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
        var match = new MatchedMod([item.Mods[0]], new StatDefinition("explicit.stat_4080418644", "+# to Strength", "explicit"), [], [50], 50);
        Assert.False(ModDefaults.EnabledByDefault(item, match));
    }

    [Fact]
    public void Amulet_anoint_is_not_ticked_by_default()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Amulets
            Rarity: Rare
            Rune Choker
            Onyx Amulet
            --------
            Item Level: 84
            --------
            Allocates Whispers of Doom (enchant)
            --------
            +10 to all Attributes (implicit)
            --------
            +60 to maximum Life
            """);
        var anoint = new MatchedMod([item.Mods[0]], new StatDefinition("enchant.stat_2954116742|22535", "Allocates Whispers of Doom", "enchant"), [], [], null);
        Assert.False(ModDefaults.EnabledByDefault(item, anoint));
    }

    [Fact]
    public void Full_stats_match_special_items()
    {
        var path = Environment.GetEnvironmentVariable("POE_STATS_JSON");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var full = new StatMatcher(TradeStatsLoader.Parse(File.ReadAllText(path)));
        foreach (var text in new[] { ForbiddenFlame, Voices, RareCluster, Megalomaniac })
        {
            var matches = full.Match(ItemTextParser.Parse(text));
            Assert.All(matches, m => Assert.True(m.IsMatched, "Not matched: " + m.DisplayText));
        }
        Assert.Equal("explicit.stat_1190333629|48480", full.Match(ItemTextParser.Parse(ForbiddenFlame))[0].Stat?.Id);
    }

    [Fact]
    public void Full_kakao_stats_match_korean_megalomaniac()
    {
        var path = Environment.GetEnvironmentVariable("POE_KAKAO_STATS_JSON");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var full = new StatMatcher(TradeStatsLoader.Parse(File.ReadAllText(path)));
        var matches = full.Match(ItemTextParser.Parse(KoreanMegalomaniac));
        Assert.All(matches, m => Assert.True(m.IsMatched, "Not matched: " + m.DisplayText));
        Assert.Equal("explicit.stat_1507409483", matches[1].Stat?.Id);
    }
}
