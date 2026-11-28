using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>Viscera (살덩이), added in the Covens league. The text is a real Kakao advanced copy.</summary>
public class VisceraTests
{
    private const string GreaterCharredLeg = """
        아이템 종류: 살덩이
        상위 그을린 다리
        --------
        하지
        --------
        아이템 레벨: 83
        --------
        { 고정 속성 부여 }
        장착 중인 장화로 받는 보너스 12(8-12)% 증가
        --------
        마을이나 은신처에 있는 버들가지 제웅으로 가져가서 부착하십시오.
        """;

    [Fact]
    public void Tier_prefix_is_removed_and_usage_text_is_not_a_mod()
    {
        var item = ItemTextParser.Parse(GreaterCharredLeg);

        Assert.Equal("그을린 다리", item.BaseType); // the trade site's base; "상위" is a tier of it
        Assert.Equal(ItemRarity.Normal, item.Rarity); // no rarity line in the copy
        var mod = Assert.Single(item.Mods);
        Assert.Equal(ModType.Implicit, mod.Type);
        Assert.Equal(new ValueRange(8, 12), mod.Ranges![0]);
    }

    [Fact]
    public void Query_uses_the_base_and_the_ticked_implicit()
    {
        var item = ItemTextParser.Parse(GreaterCharredLeg);
        var matcher = new StatMatcher([new("implicit.stat_1590183647", "장착 중인 장화로 받는 보너스 #% 증가", "implicit")]);
        var match = Assert.Single(matcher.Match(item));
        Assert.True(ModDefaults.EnabledByDefault(item, match));
        Assert.Equal("viscera", ItemCategories.For(item));

        var options = TradeQueryOptions.DefaultsFor(item);
        options.Stats.Add(new StatFilter { StatId = match.Stat!.Id, Enabled = true, Min = 8, Max = 12 });
        var query = TradeQueryBuilder.Build(options)["query"]!;
        Assert.Equal("그을린 다리", query["type"]!.GetValue<string>());
        Assert.Null(query["name"]);
    }

    [Fact]
    public void Normal_copy_mods_are_implicits_and_flavour_text_is_skipped()
    {
        // Real listing text of "마녀단 여왕의 왕관": no "(implicit)" marker, flavour text after the mods.
        var item = ItemTextParser.Parse("""
            아이템 종류: 살덩이
            마녀단 여왕의 왕관
            --------
            머리
            --------
            아이템 레벨: 85
            --------
            투사체가 대상 3개를 추가 관통
            장착한 투구, 갑옷, 장갑, 장화로 얻는 회피 49% 증가
            --------
            뼈에서 힘줄이 떨어져 나온 채, 왕관이 그녀의 머리 위에 놓이고
            두 눈은 그림자를 띠고 번뜩였다. 어둠 속에서, 그 자리에 모인 마녀들은
            움브라 최초이자 최후의 여왕에게 숭배의 말을 속삭였다.
            --------
            마을이나 은신처에 있는 버들가지 제웅으로 가져가서 부착하십시오.
            """);

        Assert.Equal("마녀단 여왕의 왕관", item.BaseType);
        Assert.Equal(2, item.Mods.Count);
        Assert.All(item.Mods, m => Assert.Equal(ModType.Implicit, m.Type));
    }

    [Fact]
    public void English_greater_prefix_too()
    {
        var item = ItemTextParser.Parse("""
            Item Class: Viscera
            Greater Charred Leg
            --------
            Lower Limb
            --------
            Item Level: 83
            --------
            12% increased Bonuses gained from Equipped Boots (implicit)
            """);
        Assert.Equal("Charred Leg", item.BaseType);
        Assert.Equal(ModType.Implicit, Assert.Single(item.Mods).Type);
    }
}
