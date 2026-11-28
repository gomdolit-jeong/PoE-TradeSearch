using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>Foulborn (삿된) uniques. Texts are real Kakao listings.</summary>
public class FoulbornTests
{
    private const string FoulbornTimepiece = """
        아이템 종류: 목걸이
        아이템 희귀도: 고유
        삿된 뒤틀린 시계
        터키석 목걸이
        --------
        요구사항:
        레벨: 50
        --------
        아이템 레벨: 83
        --------
        민첩 및 지능 +22 (implicit)
        --------
        공격 속도 25% 증가
        시전 속도 12% 증가
        스킬 효과 지속시간 15% 감소
        플레이어에게 적용되는 디버프가 100% 더 빠르게 만료됨
        시간의 사슬 저주 효과 27% 증가
        --------
        그렇게 살아왔으니, 그렇게 죽는 거겠지.
        """;

    [Fact]
    public void Prefix_is_removed_from_the_name_and_sets_foulborn()
    {
        var item = ItemTextParser.Parse(FoulbornTimepiece);
        Assert.True(item.Foulborn);
        Assert.Equal("뒤틀린 시계", item.Name);

        var query = TradeQueryBuilder.Build(TradeQueryOptions.DefaultsFor(item))["query"]!;
        Assert.Equal("뒤틀린 시계", query["name"]!.GetValue<string>());
        Assert.Equal("true", query["filters"]!["misc_filters"]!["filters"]!["mutated"]!["option"]!.GetValue<string>());
    }

    [Fact]
    public void Normal_unique_excludes_foulborn()
    {
        var item = ItemTextParser.Parse(FoulbornTimepiece.Replace("삿된 뒤틀린 시계", "뒤틀린 시계"));
        Assert.False(item.Foulborn);
        var query = TradeQueryBuilder.Build(TradeQueryOptions.DefaultsFor(item))["query"]!;
        Assert.Equal("false", query["filters"]!["misc_filters"]!["filters"]!["mutated"]!["option"]!.GetValue<string>());
    }

    [Fact]
    public void English_prefix_and_rares_are_untouched()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Amulets
            Rarity: Unique
            Foulborn Warped Timepiece
            Turquoise Amulet
            --------
            Item Level: 83
            --------
            26% increased Temporal Chains Curse Effect
            """);
        Assert.True(item.Foulborn);
        Assert.Equal("Warped Timepiece", item.Name);

        var rare = ItemTextParser.Parse("""
            Item Class: Amulets
            Rarity: Rare
            Foulborn Charm
            Turquoise Amulet
            --------
            Item Level: 83
            --------
            +20 to Strength
            """);
        Assert.False(rare.Foulborn);
        Assert.Null(TradeQueryOptions.DefaultsFor(rare).Foulborn);
    }

    [Fact]
    public void Mutated_header_marks_the_mod_and_ticks_it()
    {
        var item = ItemTextParser.Parse("""
            아이템 종류: 목걸이
            아이템 희귀도: 고유
            삿된 뒤틀린 시계
            터키석 목걸이
            --------
            아이템 레벨: 83
            --------
            { 고유 속성 }
            공격 속도 25(10-25)% 증가
            { 삿된 고유 속성 }
            시간의 사슬 저주 효과 27(20-30)% 증가
            """);
        Assert.False(item.Mods[0].Mutated);
        Assert.True(item.Mods[1].Mutated);

        var matcher = new StatMatcher(
        [
            new("explicit.stat_681332047", "공격 속도 #% 증가", "explicit"),
            new("explicit.stat_1662974426", "시간의 사슬 저주 효과 #% 증가", "explicit"),
        ]);
        var matches = matcher.Match(item);
        Assert.False(ModDefaults.EnabledByDefault(item, matches[0]));
        Assert.True(ModDefaults.EnabledByDefault(item, matches[1]));
    }
}
