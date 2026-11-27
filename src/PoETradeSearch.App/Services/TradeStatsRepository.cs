using System.IO;
using System.Net.Http;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App.Services;

/// <summary>
/// Downloads the trade stat list once a day and keeps a copy in %LOCALAPPDATA%\PoETradeSearch,
/// so the app still works when the trade site is down.
/// </summary>
public sealed class TradeStatsRepository
{
    public const string StatsUrl = "https://www.pathofexile.com/api/trade/data/stats";
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(1);

    private readonly HttpClient _http;

    public static string CacheDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoETradeSearch");

    public static string CachePath { get; } = Path.Combine(CacheDirectory, "stats.json");

    public TradeStatsRepository(HttpClient http) => _http = http;

    /// <returns>The stats and a short Korean description of where they came from.</returns>
    public async Task<(IReadOnlyList<StatDefinition> Stats, string Source)> LoadAsync(CancellationToken ct = default)
    {
        var cacheInfo = new FileInfo(CachePath);
        bool cacheFresh = cacheInfo.Exists && DateTime.UtcNow - cacheInfo.LastWriteTimeUtc < MaxCacheAge;

        if (cacheFresh)
        {
            var cached = await TryReadCacheAsync(ct);
            if (cached is not null)
                return (cached, $"캐시 ({cacheInfo.LastWriteTime:yyyy-MM-dd HH:mm})");
        }

        try
        {
            var json = await _http.GetStringAsync(StatsUrl, ct);
            var stats = TradeStatsLoader.Parse(json);
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllTextAsync(CachePath, json, ct);
            return (stats, "거래소에서 새로 받음");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
        {
            var cached = await TryReadCacheAsync(ct);
            if (cached is not null)
                return (cached, $"다운로드 실패, 이전 캐시 사용 ({ex.Message})");
            throw new InvalidOperationException($"거래소 stat 데이터를 받지 못했고 캐시도 없습니다: {ex.Message}", ex);
        }
    }

    private static async Task<IReadOnlyList<StatDefinition>?> TryReadCacheAsync(CancellationToken ct)
    {
        try
        {
            return File.Exists(CachePath)
                ? TradeStatsLoader.Parse(await File.ReadAllTextAsync(CachePath, ct))
                : null;
        }
        catch
        {
            return null;
        }
    }
}
