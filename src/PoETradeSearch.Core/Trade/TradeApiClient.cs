using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoETradeSearch.Core.Trade;

public sealed record TradeSearchResult(string QueryId, IReadOnlyList<string> ResultIds, int Total);

public sealed record League(string Id, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Client for one PoE1 trade site (global or Kakao). Each server has its own rate limits.</summary>
public sealed class TradeApiClient(HttpClient http, RateLimiter limiter, TradeServer server) : ITradeSearchApi
{
    /// <summary>The fetch endpoint accepts at most 10 result IDs per request.</summary>
    public const int FetchBatchSize = 10;

    public TradeServer Server { get; } = server;

    private string BaseUrl => Server.BaseUrl;

    public string WebSearchUrl(string league, string queryId) => Server.WebSearchUrl(league, queryId);

    public async Task<IReadOnlyList<League>> GetLeaguesAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(Server.LeaguesUrl, ct));
        var leagues = new List<League>();
        foreach (var l in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            // The list also contains console realms; only PC leagues are searchable here.
            if (l.TryGetProperty("realm", out var realm) && realm.GetString() != "pc")
                continue;
            leagues.Add(new League(l.GetProperty("id").GetString()!, l.GetProperty("text").GetString()!));
        }
        return leagues;
    }

    public async Task<TradeSearchResult> SearchAsync(string league, JsonObject body, CancellationToken ct = default)
    {
        await limiter.WaitAsync("search", ct);
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{BaseUrl}/api/trade/search/{Uri.EscapeDataString(league)}", content, ct);
        limiter.Update("search", response.StatusCode, response.Headers);

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new TradeApiException((int)response.StatusCode, ErrorMessage(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var ids = root.GetProperty("result").EnumerateArray().Select(e => e.GetString()!).ToList();
        int total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : ids.Count;
        return new TradeSearchResult(root.GetProperty("id").GetString()!, ids, total);
    }

    public async Task<IReadOnlyList<TradeListing>> FetchAsync(IEnumerable<string> resultIds, string queryId, CancellationToken ct = default)
    {
        var ids = resultIds.Take(FetchBatchSize).ToList();
        if (ids.Count == 0)
            return [];

        await limiter.WaitAsync("fetch", ct);
        using var response = await http.GetAsync($"{BaseUrl}/api/trade/fetch/{string.Join(",", ids)}?query={queryId}", ct);
        limiter.Update("fetch", response.StatusCode, response.Headers);

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new TradeApiException((int)response.StatusCode, ErrorMessage(json));

        // Keep the order of the search result (cheapest first).
        var listings = TradeListing.ParseFetchResponse(json);
        var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return listings.OrderBy(l => order.GetValueOrDefault(l.Id, int.MaxValue)).ToList();
    }

    private static string ErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString() ?? json;
        }
        catch (JsonException) { }
        return json.Length > 200 ? json[..200] : json;
    }
}

public sealed class TradeApiException(int statusCode, string message)
    : Exception($"Trade API error {statusCode}: {message}")
{
    public int StatusCode { get; } = statusCode;
    public string ApiMessage { get; } = message;
}
