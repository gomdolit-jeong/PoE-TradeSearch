using System.Net.Http;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App.Services;

/// <summary>
/// Everything that belongs to one trade server: API client (with its own rate limits), stat matcher and
/// the transfigured gem list.
/// </summary>
public sealed class ServerContext
{
    private Task<StatMatcher>? _matcherTask;
    private Task<GemCatalog>? _gemsTask;

    public ServerContext(HttpClient http, TradeServer server)
    {
        Server = server;
        Http = http;
        Api = new TradeApiClient(http, new RateLimiter(), server);
    }

    public TradeServer Server { get; }
    public TradeApiClient Api { get; }
    private HttpClient Http { get; }

    /// <summary>Set once loading finished; null while loading or after a failure.</summary>
    public StatMatcher? Matcher { get; private set; }
    public string StatusText { get; private set; } = "불러오는 중...";

    /// <summary>Loads (once) and returns the matcher; later calls reuse the same task.</summary>
    public Task<StatMatcher> GetMatcherAsync() => _matcherTask ??= LoadMatcherAsync();

    /// <summary>Transfigured gem names; an empty catalog if it can't be loaded (normal gems still work).</summary>
    public Task<GemCatalog> GetGemsAsync() => _gemsTask ??= LoadGemsAsync();

    private async Task<StatMatcher> LoadMatcherAsync()
    {
        try
        {
            var (json, source) = await TradeDataCache.GetAsync(Http, Server.StatsUrl, $"stats-{Server.Id}.json",
                j => TradeDataCache.TryParse(j, TradeStatsLoader.Parse));
            var matcher = await Task.Run(() => new StatMatcher(TradeStatsLoader.Parse(json)));
            Matcher = matcher;
            StatusText = $"{matcher.StatCount:N0}개 ({source})";
            Log.Info($"{Server.Id} 옵션 데이터: {StatusText}");
            return matcher;
        }
        catch (Exception ex)
        {
            StatusText = "실패: " + ex.Message;
            Log.Error($"{Server.Id} 옵션 데이터 로드 실패", ex);
            _matcherTask = null; // allow a retry on the next item
            throw;
        }
    }

    private async Task<GemCatalog> LoadGemsAsync()
    {
        try
        {
            var (json, source) = await TradeDataCache.GetAsync(Http, Server.ItemsUrl, $"items-{Server.Id}.json",
                j => TradeDataCache.TryParse(j, GemCatalog.Parse));
            var gems = GemCatalog.Parse(json);
            Log.Info($"{Server.Id} 변형 젬 {gems.Count}개 ({source})");
            return gems;
        }
        catch (Exception ex)
        {
            Log.Warn($"{Server.Id} 젬 데이터 로드 실패, 변형 젬은 이름 그대로 검색: {ex.Message}");
            _gemsTask = null;
            return GemCatalog.Empty;
        }
    }
}
