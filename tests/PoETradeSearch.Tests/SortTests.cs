using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

public class SortTests
{
    [Fact]
    public void Price_ascending_by_default_and_newest_on_request()
    {
        var item = ItemTextParser.Parse("""
            아이템 종류: 스킬 젬
            아이템 희귀도: 젬
            화염구
            --------
            레벨: 20 (최대)
            퀄리티: +20% (augmented)
            --------
            """);
        var options = TradeQueryOptions.DefaultsFor(item);
        Assert.Equal("asc", TradeQueryBuilder.Build(options)["sort"]!["price"]!.GetValue<string>());

        options.NewestFirst = true;
        var sort = TradeQueryBuilder.Build(options)["sort"]!;
        Assert.Equal("desc", sort["indexed"]!.GetValue<string>());
        Assert.Null(sort["price"]);
    }
}
