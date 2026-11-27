using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>Korean (Kakao) client text. Stat texts below are copied from poe.kakaogames.com/api/trade/data/stats.</summary>
public class KoreanClientTests
{
    private const string KoreanAxe = """
        아이템 종류: 한손 도끼
        아이템 희귀도: 희귀
        휴거 갈증
        악령 도끼
        --------
        한손 도끼
        퀄리티: +20% (augmented)
        물리 피해: 110-193 (augmented)
        치명타 확률: 5.00%
        초당 공격 횟수: 1.50 (augmented)
        --------
        요구 사항:
        레벨: 68
        힘: 140
        --------
        홈: R-R-G
        --------
        아이템 레벨: 84
        --------
        물리 피해 #~# 추가
        --------
        물리 피해 10~20 추가
        공격 속도 12% 증가
        생명력 최대치 +52
        화염 저항 +38% (crafted)
        --------
        쉐이퍼 아이템
        --------
        타락
        """;

    private static readonly StatMatcher KakaoMatcher = new(
    [
        new("explicit.stat_1940865751", "물리 피해 #~# 추가(특정)", "explicit"),
        new("explicit.stat_960081730", "물리 피해 #~# 추가", "explicit"),
        new("explicit.stat_210067635", "공격 속도 #% 증가(특정)", "explicit"),
        new("explicit.stat_681332047", "공격 속도 #% 증가", "explicit"),
        new("explicit.stat_3299347043", "생명력 최대치 +#", "explicit"),
        new("crafted.stat_3372524247", "화염 저항 +#%", "crafted"),
        new("explicit.stat_474294393", "스킬의 마나 소모 #% 감소", "explicit"),
    ]);

    [Fact]
    public void Detects_and_parses_korean_header_and_properties()
    {
        Assert.True(ItemTextParser.LooksLikeItem(KoreanAxe));
        var item = ItemTextParser.Parse(KoreanAxe);

        Assert.Equal(ClientLanguage.Korean, item.Language);
        Assert.Equal("한손 도끼", item.ItemClass);
        Assert.Equal(ItemRarity.Rare, item.Rarity);
        Assert.Equal("휴거 갈증", item.Name);
        Assert.Equal("악령 도끼", item.BaseType);
        Assert.Equal(84, item.ItemLevel);
        Assert.Equal(20, item.Quality);
        Assert.Equal("R-R-G", item.Sockets);
        Assert.Equal(3, item.MaxLinks);
        Assert.Equal("110-193 (augmented)", item.Properties["물리 피해"]);
        Assert.True(item.Corrupted);
        Assert.Equal(["shaper"], item.Influences);
        Assert.Equal(5, item.Mods.Count);
        Assert.Equal(ModType.Crafted, item.Mods[4].Type);
    }

    [Fact]
    public void Matches_korean_mods_preferring_local_on_weapons()
    {
        var matches = KakaoMatcher.Match(ItemTextParser.Parse(KoreanAxe));

        Assert.Equal("explicit.stat_1940865751", matches[1].Stat?.Id);  // 물리 피해 10~20 추가 → (특정)
        Assert.Equal([10.0, 20.0], matches[1].Values);
        Assert.Equal(15.0, matches[1].SearchValue);
        Assert.Equal("explicit.stat_210067635", matches[2].Stat?.Id);   // 공격 속도 증가 → (특정)
        Assert.Equal("explicit.stat_3299347043", matches[3].Stat?.Id);
        Assert.Equal([52.0], matches[3].Values);
        Assert.Equal("crafted.stat_3372524247", matches[4].Stat?.Id);
    }

    [Fact]
    public void Matches_korean_opposite_wording()
    {
        var item = ItemTextParser.Parse("""
            아이템 종류: 반지
            아이템 희귀도: 희귀
            폭풍 고리
            자수정 반지
            --------
            아이템 레벨: 84
            --------
            스킬의 마나 소모 10% 증가
            공격 속도 7% 증가
            """);

        var matches = KakaoMatcher.Match(item);
        Assert.Equal("explicit.stat_474294393", matches[0].Stat?.Id);
        Assert.Equal([-10.0], matches[0].Values);
        Assert.Equal("explicit.stat_681332047", matches[1].Stat?.Id); // ring: global attack speed
        Assert.Equal("accessory.ring", ItemCategories.FromItemClass(item.ItemClass));
    }

    [Fact]
    public void Fractured_mod_marks_item_as_fractured()
    {
        var item = ItemTextParser.Parse("""
            아이템 종류: 장갑
            아이템 희귀도: 희귀
            어둠 손
            가죽 장갑
            --------
            아이템 레벨: 80
            --------
            생명력 최대치 +70 (fractured)
            """);

        Assert.True(item.FracturedItem);
        Assert.Equal(ModType.Fractured, Assert.Single(item.Mods).Type);
    }

    [Fact]
    public void Full_kakao_stats_match_korean_axe()
    {
        var path = Environment.GetEnvironmentVariable("POE_KAKAO_STATS_JSON");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        var matcher = new StatMatcher(TradeStatsLoader.Parse(File.ReadAllText(path)));
        var matches = matcher.Match(ItemTextParser.Parse(KoreanAxe));
        Assert.All(matches.Skip(1), m => Assert.True(m.IsMatched, $"Not matched: {m.DisplayText}"));
        Assert.Contains("(특정)", matches[1].Stat!.Text);
    }
}
