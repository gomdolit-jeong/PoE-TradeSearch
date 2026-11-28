using System.Text.Json.Nodes;

namespace PoETradeSearch.Core.Trade;

/// <summary>The part of the trade API the search window uses (search, fetch, web link), so it can be faked in tests.</summary>
public interface ITradeSearchApi
{
    TradeServer Server { get; }
    Task<TradeSearchResult> SearchAsync(string league, JsonObject body, CancellationToken ct = default);
    Task<IReadOnlyList<TradeListing>> FetchAsync(IEnumerable<string> resultIds, string queryId, CancellationToken ct = default);
    string WebSearchUrl(string league, string queryId);
}
