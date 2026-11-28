using PoETradeSearch.Core.Data;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.Tests;

/// <summary>Changes GameData.Current, so it runs alone (after the parallel tests).</summary>
[CollectionDefinition("GameData", DisableParallelization = true)]
public class GameDataCollection;

[Collection("GameData")]
public class GameDataTests
{
    [Fact]
    public void Built_in_data_has_the_known_flags()
    {
        var data = GameData.BuiltIn();
        Assert.Equal("fractured_item", data.Flag("fractured")!.TradeFilter);
        Assert.Equal("세계 포식자", data.Flag("tangled")!.Label);
        Assert.Equal("십자군", data.InfluenceLabel("crusader"));
        Assert.Equal("accessory.ring", data.ItemCategories["반지"]);
        Assert.Contains("삿된 ", data.NamePrefixes.Foulborn);
    }

    [Fact]
    public void A_new_league_flag_needs_only_a_json_entry()
    {
        var builtIn = GameData.BuiltIn();
        var custom = new GameData
        {
            ItemFlags = [.. builtIn.ItemFlags, new GameData.ItemFlagDefinition("newmech", ["New Mechanic Item", "새 메커니즘 아이템"], "newmech_item", "새 메커니즘")],
            Influences = builtIn.Influences,
            NamePrefixes = builtIn.NamePrefixes,
            ModHeaders = builtIn.ModHeaders,
            VariantStats = builtIn.VariantStats,
            ClusterItemLevelSteps = builtIn.ClusterItemLevelSteps,
            CurrencyNames = builtIn.CurrencyNames,
            ItemCategories = builtIn.ItemCategories,
        };

        try
        {
            GameData.Current = custom;
            var item = ItemTextParser.Parse("""
                아이템 종류: 반지
                아이템 희귀도: 희귀
                폭풍 고리
                자수정 반지
                --------
                아이템 레벨: 84
                --------
                생명력 최대치 +50
                --------
                새 메커니즘 아이템
                """);

            Assert.True(item.HasFlag("newmech"));
            var query = TradeQueryBuilder.Build(TradeQueryOptions.DefaultsFor(item))["query"]!;
            Assert.Equal("true", query["filters"]!["misc_filters"]!["filters"]!["newmech_item"]!["option"]!.GetValue<string>());
        }
        finally
        {
            GameData.Current = GameData.BuiltIn();
        }
    }

    [Fact]
    public void User_file_overrides_and_bad_file_falls_back()
    {
        var dir = Directory.CreateTempSubdirectory("poe-gamedata-");
        try
        {
            Assert.Null(GameData.Load(dir.FullName)); // no file → built-in
            Assert.Equal("내장", GameData.Source);

            File.WriteAllText(Path.Combine(dir.FullName, GameData.FileName), """{ "currencyNames": { "chaos": "카오" } }""");
            Assert.Null(GameData.Load(dir.FullName));
            Assert.Equal("카오", GameData.Current.CurrencyNames["chaos"]);

            File.WriteAllText(Path.Combine(dir.FullName, GameData.FileName), "{ not json");
            Assert.NotNull(GameData.Load(dir.FullName));
            Assert.Equal("카오스", GameData.Current.CurrencyNames["chaos"]);
        }
        finally
        {
            GameData.Current = GameData.BuiltIn();
            dir.Delete(true);
        }
    }
}
