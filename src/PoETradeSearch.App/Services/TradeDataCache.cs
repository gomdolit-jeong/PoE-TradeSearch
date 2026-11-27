using System.IO;
using System.Net.Http;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App.Services;

/// <summary>
/// Downloads trade site data (stats, items) once a day and keeps a copy in %LOCALAPPDATA%\PoETradeSearch,
/// so the app still works when the trade site is down.
/// </summary>
public static class TradeDataCache
{
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(1);

    public static string CacheDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PoETradeSearch");

    /// <returns>The JSON and a short Korean description of where it came from.</returns>
    public static async Task<(string Json, string Source)> GetAsync(HttpClient http, string url, string fileName,
                                                                     Func<string, bool> isValid, CancellationToken ct = default)
    {
        var path = Path.Combine(CacheDirectory, fileName);
        var info = new FileInfo(path);

        if (info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < MaxCacheAge)
        {
            var cached = await TryReadAsync(path, isValid, ct);
            if (cached is not null)
                return (cached, $"캐시 {info.LastWriteTime:MM-dd HH:mm}");
        }

        try
        {
            var json = await http.GetStringAsync(url, ct);
            if (!isValid(json))
                throw new FormatException("거래소 응답 형식이 예상과 다릅니다.");
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllTextAsync(path, json, ct);
            return (json, "새로 받음");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
        {
            var cached = await TryReadAsync(path, isValid, ct);
            if (cached is not null)
                return (cached, $"다운로드 실패, 이전 캐시 사용 ({ex.Message})");
            throw new InvalidOperationException($"{url} 를 받지 못했고 캐시도 없습니다: {ex.Message}", ex);
        }
    }

    private static async Task<string?> TryReadAsync(string path, Func<string, bool> isValid, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var json = await File.ReadAllTextAsync(path, ct);
            return isValid(json) ? json : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryParse<T>(string json, Func<string, T> parse)
    {
        try { parse(json); return true; }
        catch { return false; }
    }
}
