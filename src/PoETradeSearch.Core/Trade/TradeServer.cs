using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.Core.Trade;

/// <summary>
/// A PoE1 trade site. Both use the same API and stat IDs; Kakao's texts are Korean and it only lists
/// Kakao-realm players, so Korean clients must search there.
/// </summary>
public sealed record TradeServer(string Id, string DisplayName, string BaseUrl, ClientLanguage Language)
{
    public static readonly TradeServer Global = new("global", "글로벌 (pathofexile.com)", "https://www.pathofexile.com", ClientLanguage.English);
    public static readonly TradeServer Kakao = new("kakao", "카카오 (poe.kakaogames.com)", "https://poe.kakaogames.com", ClientLanguage.Korean);

    public static IReadOnlyList<TradeServer> All { get; } = [Kakao, Global];

    public static TradeServer ForLanguage(ClientLanguage language) => language == ClientLanguage.Korean ? Kakao : Global;

    public static TradeServer FromId(string? id) => All.FirstOrDefault(s => s.Id == id) ?? Kakao;

    public string StatsUrl => $"{BaseUrl}/api/trade/data/stats";
    public string LeaguesUrl => $"{BaseUrl}/api/trade/data/leagues";
    public string StaticDataUrl => $"{BaseUrl}/api/trade/data/static";
    public string ItemsUrl => $"{BaseUrl}/api/trade/data/items";

    public string WebSearchUrl(string league, string queryId) =>
        $"{BaseUrl}/trade/search/{Uri.EscapeDataString(league)}/{queryId}";

    public override string ToString() => DisplayName;
}
