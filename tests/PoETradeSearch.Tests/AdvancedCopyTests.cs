using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>Advanced copy (Ctrl+Alt+C) tier ranges and socket colours. The axe is a real Kakao client copy.</summary>
public class AdvancedCopyTests
{
    private const string KoreanAxe = """
        아이템 종류: 한손 도끼
        아이템 희귀도: 희귀
        휴거 갈증
        악령 도끼
        --------
        한손 도끼
        퀄리티: +4% (augmented)
        물리 피해: 115-201 (augmented)
        원소 피해: 2-44 (augmented)
        치명타 확률: 6.30% (augmented)
        초당 공격 횟수: 1.30
        무기 범위: 1.1미터
        --------
        요구사항:
        레벨: 56
        힘: 134
        민첩: 88
        --------
        홈: W-W-W
        --------
        아이템 레벨: 57
        --------
        { 접두어 속성 부여 "포악한" (등급: 5) — 피해, 물리, 공격 }
        물리 피해 97(85-109)% 증가
        { 접두어 속성 부여 "타닥대는" (등급: 8) — 피해, 원소, 번개, 공격 }
        번개 피해 2~44(41-48) 추가
        { 접두어 속성 부여 "번뜩이는" (등급: 5) — 피해, 물리, 공격 }
        물리 피해 11(11-14)~23(21-25) 추가
        { 접미어 속성 부여 "- 구멍" (등급: 3) — 공격, 치명타 }
        치명타 확률 26(25-29)% 증가
        { 접미어 속성 부여 "- 정밀함" (등급: 4) — 공격 }
        정확도 +164(131-215)
        """;

    // Real Kakao stat ids and texts. Weapon physical damage % and crit chance % have no "(특정)" form on the
    // trade site (the plain stat is the local one); added damage and accuracy do.
    private static readonly StatMatcher Matcher = new(
    [
        new("explicit.stat_1509134228", "물리 피해 #% 증가", "explicit"),
        new("explicit.stat_3336890334", "번개 피해 #~# 추가(특정)", "explicit"),
        new("explicit.stat_1940865751", "물리 피해 #~# 추가(특정)", "explicit"),
        new("explicit.stat_2375316951", "치명타 확률 #% 증가", "explicit"),
        new("explicit.stat_691932474", "정확도 +#(특정)", "explicit"),
    ]);

    [Fact]
    public void Parser_keeps_tier_ranges_and_strips_them_from_text()
    {
        var item = ItemTextParser.Parse(KoreanAxe);

        Assert.Equal(5, item.Mods.Count); // "{ ... }" header lines are skipped
        var phys = item.Mods[0];
        Assert.Equal("물리 피해 97% 증가", phys.Text);
        Assert.Equal([97.0], phys.Values);
        Assert.Equal([new ValueRange(85, 109)], phys.Ranges!.Select(r => r!.Value));

        var lightning = item.Mods[1];
        Assert.Equal("번개 피해 2~44 추가", lightning.Text);
        Assert.Null(lightning.Ranges![0]);                    // "2" had no range
        Assert.Equal(new ValueRange(41, 48), lightning.Ranges[1]);
    }

    [Fact]
    public void Search_range_is_the_tier_range_in_search_units()
    {
        var matches = Matcher.Match(ItemTextParser.Parse(KoreanAxe));

        Assert.All(matches, m => Assert.True(m.IsMatched, m.DisplayText));
        Assert.Equal(new ValueRange(85, 109), matches[0].SearchRange);
        // "2~44(41-48)": the fixed 2 counts as itself → average 21.5–25.
        Assert.Equal(new ValueRange(21.5, 25), matches[1].SearchRange);
        // "11(11-14)~23(21-25)" → (11+21)/2 – (14+25)/2.
        Assert.Equal(new ValueRange(16, 19.5), matches[2].SearchRange);
        Assert.Equal(new ValueRange(25, 29), matches[3].SearchRange);
        Assert.Equal(new ValueRange(131, 215), matches[4].SearchRange);
    }

    [Fact]
    public void Normal_copy_has_no_ranges()
    {
        var item = ItemTextParser.Parse("""
            아이템 종류: 한손 도끼
            아이템 희귀도: 희귀
            휴거 갈증
            악령 도끼
            --------
            아이템 레벨: 57
            --------
            물리 피해 97% 증가
            """);

        Assert.Null(item.Mods[0].Ranges);
        Assert.Null(Matcher.Match(item)[0].SearchRange);
    }

    [Fact]
    public void Reduced_mod_range_is_negated_and_flipped()
    {
        var matcher = new StatMatcher([new("explicit.stat_474294393", "스킬의 마나 소모 #% 감소", "explicit")]);
        var item = ItemTextParser.Parse("""
            아이템 종류: 반지
            아이템 희귀도: 희귀
            폭풍 고리
            자수정 반지
            --------
            아이템 레벨: 84
            --------
            스킬의 마나 소모 10(8-12)% 증가
            """);

        var m = Assert.Single(matcher.Match(item));
        Assert.Equal([-10.0], m.Values);
        Assert.Equal(new ValueRange(-12, -8), m.SearchRange);
    }

    [Fact]
    public void Sockets_count_colours_and_white_default()
    {
        var item = ItemTextParser.Parse(KoreanAxe);

        Assert.Equal("W-W-W", item.Sockets);
        Assert.Equal(3, item.SocketCount);
        Assert.Equal(3, item.SocketsOfColour('W'));
        Assert.Equal(0, item.SocketsOfColour('R'));

        var options = TradeQueryOptions.DefaultsFor(item);
        Assert.Equal(new SocketColours(null, null, null, 3), options.SocketColours);

        var sockets = TradeQueryBuilder.Build(options)["query"]!["filters"]!["socket_filters"]!["filters"]!["sockets"]!;
        Assert.Equal(3, sockets["w"]!.GetValue<int>());
        Assert.Null(sockets["r"]);
    }

    [Fact]
    public void Socket_filter_with_count_and_colours()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Body Armours
            Rarity: Rare
            Dusk Shell
            Vaal Regalia
            --------
            Sockets: R-G-B-B-B B
            --------
            Item Level: 86
            --------
            +98 to maximum Energy Shield
            """);
        Assert.Equal(6, item.SocketCount);
        Assert.Equal(3 + 1, item.SocketsOfColour('B'));
        Assert.Equal(default, TradeQueryOptions.DefaultsFor(item).SocketColours); // no white → off

        var options = TradeQueryOptions.DefaultsFor(item);
        options.SocketsMin = 6;
        options.SocketColours = new SocketColours(1, null, 4, null);
        var socketFilters = TradeQueryBuilder.Build(options)["query"]!["filters"]!["socket_filters"]!["filters"]!;
        Assert.Equal(6, socketFilters["sockets"]!["min"]!.GetValue<int>());
        Assert.Equal(1, socketFilters["sockets"]!["r"]!.GetValue<int>());
        Assert.Equal(4, socketFilters["sockets"]!["b"]!.GetValue<int>());
        Assert.Equal(5, socketFilters["links"]!["min"]!.GetValue<int>()); // 5-link default
    }

    [Fact]
    public void Synthesised_unique_implicits_come_from_headers()
    {
        var item = ItemTextParser.Parse("""
            아이템 종류: 반지
            아이템 희귀도: 고유
            유죄의 고리
            결합된 철제 반지
            --------
            아이템 레벨: 83
            --------
            { 고정 속성 부여 — 피해, 원소, 화염 }
            화염 피해 15(15-16)% 증가
            { 고정 속성 부여 — 피해, 카오스 }
            카오스 피해 12(11-12)% 증가
            --------
            { 고유 속성 부여 — 능력치 }
            모든 능력치 +18(10-20)
            ("능력치"는 힘, 민첩, 지능입니다)
            { 고유 속성 부여 — 방어력, 방어도 }
            방어도 +385(350-400)
            --------
            결합된 아이템
            """);

        Assert.Equal("철제 반지", item.BaseType);
        Assert.True(item.Synthesised);
        Assert.Equal([ModType.Implicit, ModType.Implicit, ModType.Explicit, ModType.Explicit], item.Mods.Select(m => m.Type));

        var matcher = new StatMatcher(
        [
            new("implicit.stat_3962278098", "화염 피해 #% 증가", "implicit"),
            new("implicit.stat_736967255", "카오스 피해 #% 증가", "implicit"),
            new("explicit.stat_1379411836", "모든 능력치 +#", "explicit"),
            new("explicit.stat_3484657501", "방어도 +#", "explicit"),
        ]);
        var matches = matcher.Match(item);
        Assert.Equal(["implicit.stat_3962278098", "implicit.stat_736967255", "explicit.stat_1379411836", "explicit.stat_3484657501"],
                     matches.Select(m => m.Stat!.Id));
        Assert.Equal([true, true, false, false], matches.Select(m => ModDefaults.EnabledByDefault(item, m)));

        var query = TradeQueryBuilder.Build(TradeQueryOptions.DefaultsFor(item))["query"]!;
        Assert.Equal("철제 반지", query["type"]!.GetValue<string>());
        Assert.Equal("true", query["filters"]!["misc_filters"]!["filters"]!["synthesised_item"]!["option"]!.GetValue<string>());
    }

    [Fact]
    public void Corrupted_implicit_is_marked_and_ticked()
    {
        var item = ItemTextParser.Parse("""
            아이템 종류: 방패
            아이템 희귀도: 고유
            공포의 성배
            전쟁용 버클러
            --------
            아이템 레벨: 83
            --------
            { 타락 고정 속성 부여 — 피해, 치명타 }
            치명타로 받는 추가 피해 20(20-30)% 감소
            --------
            { 고유 속성 부여 — 방어력, 회피 }
            회피 36(30-50)% 증가
            { 고유 속성 부여 }
            막아낼 시 100%의 확률로 몬스터가 도망침
            (마법 몬스터가 50% 확률로 도망을 피합니다. 희귀 및 고유 몬스터는 도망칠 수 없습니다.)
            --------
            타락
            """);

        Assert.True(item.Corrupted);
        Assert.Equal(3, item.Mods.Count); // reminder text skipped
        Assert.Equal(ModType.Implicit, item.Mods[0].Type);
        Assert.True(item.Mods[0].FromCorruption);
        Assert.False(item.Mods[1].FromCorruption);

        var matcher = new StatMatcher(
        [
            new("implicit.stat_3442976749", "치명타로 받는 추가 피해 #% 감소", "implicit"),
            new("explicit.stat_124859000", "회피 #% 증가", "explicit"),
        ]);
        var matches = matcher.Match(item);
        Assert.True(ModDefaults.EnabledByDefault(item, matches[0]));
        Assert.False(ModDefaults.EnabledByDefault(item, matches[1]));
    }

    [Fact]
    public void Full_kakao_stats_give_ranges_for_the_axe()
    {
        var path = Environment.GetEnvironmentVariable("POE_KAKAO_STATS_JSON");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var matches = new StatMatcher(TradeStatsLoader.Parse(File.ReadAllText(path))).Match(ItemTextParser.Parse(KoreanAxe));
        Assert.All(matches, m => Assert.True(m.IsMatched, "Not matched: " + m.DisplayText));
        Assert.All(matches, m => Assert.NotNull(m.SearchRange));
        Assert.Equal(["explicit.stat_1509134228", "explicit.stat_3336890334", "explicit.stat_1940865751",
                      "explicit.stat_2375316951", "explicit.stat_691932474"], matches.Select(m => m.Stat!.Id));
    }
}
