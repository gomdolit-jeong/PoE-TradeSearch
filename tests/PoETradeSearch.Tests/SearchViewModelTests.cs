using System.Text.Json.Nodes;
using PoETradeSearch.App.ViewModels;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>The search window's state → trade query, without a window or the network.</summary>
public class SearchViewModelTests
{
    private sealed class FakeApi : ITradeSearchApi
    {
        public JsonObject? LastBody { get; private set; }
        public TradeServer Server => TradeServer.Kakao;

        public Task<TradeSearchResult> SearchAsync(string league, JsonObject body, CancellationToken ct = default)
        {
            LastBody = body;
            return Task.FromResult(new TradeSearchResult("q1", ["a", "b"], 2));
        }

        public Task<IReadOnlyList<TradeListing>> FetchAsync(IEnumerable<string> resultIds, string queryId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TradeListing>>(resultIds.Select(id => new TradeListing { Id = id, PriceAmount = 1, PriceCurrency = "chaos" }).ToList());

        public string WebSearchUrl(string league, string queryId) => $"https://example.test/{league}/{queryId}";
    }

    private static SearchViewModel Create(string text, StatMatcher? matcher = null)
    {
        var item = ItemTextParser.Parse(text);
        var matches = (matcher ?? new StatMatcher([])).Match(item);
        return new SearchViewModel(item, matches, new FakeApi(), CurrencyCatalog.Empty, null, "Standard", "스탠다드");
    }

    private const string FracturedBoots = """
        아이템 종류: 장화
        아이템 희귀도: 희귀
        기적의 길
        악령술사 장화
        --------
        홈: G-B B-B
        --------
        아이템 레벨: 83
        --------
        이동 속도 25% 증가 (fractured)
        생명력 최대치 +27
        --------
        작열의 총주교 아이템
        십자군 아이템
        """;

    private static readonly StatMatcher BootsMatcher = new(
    [
        new("fractured.stat_2250533757", "이동 속도 #% 증가", "fractured"),
        new("explicit.stat_2250533757", "이동 속도 #% 증가", "explicit"),
        new("explicit.stat_3299347043", "생명력 최대치 +#", "explicit"),
    ]);

    [Fact]
    public void Chips_come_from_item_flags_and_influences()
    {
        var vm = Create(FracturedBoots, BootsMatcher);

        Assert.Equal(["분열된 아이템만", "작열의 총주교", "십자군"], vm.Chips.Select(c => c.Label));
        Assert.All(vm.Chips, c => Assert.True(c.IsChecked));

        var options = vm.BuildOptions();
        Assert.True(options.FracturedItem);
        Assert.True(options.SearingExarch);
        Assert.Equal(["crusader"], options.Influences);
    }

    [Fact]
    public void Turning_fractured_chip_off_searches_fractured_mods_as_explicit()
    {
        var vm = Create(FracturedBoots, BootsMatcher);
        var fracturedRow = Assert.Single(vm.Mods, r => r.IsFracturedMod);
        fracturedRow.Enabled = true;

        vm.Chips.Single(c => c.Id == "fractured").IsChecked = false;

        Assert.False(fracturedRow.SearchAsFractured);
        var options = vm.BuildOptions();
        Assert.False(options.FracturedItem);
        Assert.Contains(options.Stats, s => s.StatId == "explicit.stat_2250533757" && s.Enabled);
    }

    [Fact]
    public void Socket_and_trade_choices_map_to_options()
    {
        var vm = Create(FracturedBoots, BootsMatcher);
        Assert.False(vm.SocketsOn); // no white sockets → 상관없음
        Assert.Null(vm.BuildOptions().SocketsMin);

        vm.SocketsOn = true;
        vm.SocketCountText = "4";
        vm.SocketBlueText = "3";
        vm.Mode = TradeMode.InstantOnly;
        vm.Corrupted = TriState.No;
        vm.NewestFirst = true;

        var options = vm.BuildOptions();
        Assert.Equal(4, options.SocketsMin);
        Assert.Equal(new SocketColours(null, null, 3, null), options.SocketColours);
        Assert.Equal(TradeMode.InstantOnly, options.Mode);
        Assert.False(options.Corrupted);
        Assert.True(options.NewestFirst);
    }

    [Fact]
    public void Mod_min_is_the_tier_bottom_and_max_is_left_open()
    {
        var vm = Create("""
            아이템 종류: 한손 도끼
            아이템 희귀도: 희귀
            휴거 갈증
            악령 도끼
            --------
            아이템 레벨: 57
            --------
            { 접두어 속성 부여 "포악한" (등급: 5) — 피해, 물리, 공격 }
            물리 피해 97(85-109)% 증가
            """, new StatMatcher([new("explicit.stat_1509134228", "물리 피해 #% 증가", "explicit")]));

        var row = Assert.Single(vm.Mods);
        Assert.Equal("85", row.MinText);
        Assert.Equal("", row.MaxText); // a better roll can be cheaper
        var filter = Assert.Single(vm.BuildOptions().Stats);
        Assert.Equal(85, filter.Min);
        Assert.Null(filter.Max);
    }

    [Fact]
    public void Foulborn_chip_sends_the_mutated_filter()
    {
        var vm = Create("""
            아이템 종류: 목걸이
            아이템 희귀도: 고유
            삿된 뒤틀린 시계
            터키석 목걸이
            --------
            아이템 레벨: 83
            --------
            시간의 사슬 저주 효과 27% 증가
            """);

        var chip = Assert.Single(vm.Chips);
        Assert.Equal("삿된 아이템만", chip.Label);
        Assert.True(vm.BuildOptions().Foulborn);

        chip.IsChecked = false;
        Assert.Null(vm.BuildOptions().Foulborn);
    }

    [Fact]
    public void Gem_numbers_are_on_and_hidden_rows_are_ignored()
    {
        var vm = Create("""
            아이템 종류: 스킬 젬
            아이템 희귀도: 젬
            화염구
            --------
            레벨: 20 (최대)
            퀄리티: +20% (augmented)
            --------
            """);

        Assert.True(vm.ShowGemLevel);
        Assert.False(vm.ShowSocketRow);
        Assert.False(vm.HasMods);
        var options = vm.BuildOptions();
        Assert.Equal(20, options.GemLevelMin);
        Assert.Equal(20, options.QualityMin);
        Assert.Null(options.LinksMin);
    }

    [Fact]
    public async Task Search_fills_results_and_status()
    {
        var api = new FakeApi();
        var item = ItemTextParser.Parse(FracturedBoots);
        var vm = new SearchViewModel(item, BootsMatcher.Match(item), api, CurrencyCatalog.Empty, null, "Standard", "스탠다드");
        string? opened = null;
        vm.OpenUrl = url => opened = url;

        await vm.SearchAsync();

        Assert.Equal(2, vm.Results.Count);
        Assert.Equal("2개 중 2개", vm.Status);
        Assert.False(vm.HasMore);
        Assert.Equal("true", api.LastBody!["query"]!["filters"]!["misc_filters"]!["filters"]!["searing_item"]!["option"]!.GetValue<string>());

        vm.OpenWebCommand.Execute(null);
        Assert.Equal("https://example.test/Standard/q1", opened);
    }
}
