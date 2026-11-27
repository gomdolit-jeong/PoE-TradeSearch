using System.Text.Json;

namespace PoETradeSearch.Core.Trade;

public sealed record CurrencyInfo(string Id, string Name, string? ImageUrl);

/// <summary>Currency names and icons from https://www.pathofexile.com/api/trade/data/static.</summary>
public sealed class CurrencyCatalog
{
    public const string StaticDataUrl = "https://www.pathofexile.com/api/trade/data/static";
    private const string ImageHost = "https://web.poecdn.com";

    private readonly Dictionary<string, CurrencyInfo> _byId;

    public static CurrencyCatalog Empty { get; } = new([]);

    private CurrencyCatalog(Dictionary<string, CurrencyInfo> byId) => _byId = byId;

    public CurrencyInfo? Find(string? id) => id is not null && _byId.TryGetValue(id, out var c) ? c : null;

    public static CurrencyCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var byId = new Dictionary<string, CurrencyInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            if (!group.TryGetProperty("entries", out var entries))
                continue;
            foreach (var e in entries.EnumerateArray())
            {
                if (!e.TryGetProperty("id", out var idProp) || idProp.GetString() is not { } id)
                    continue;
                var name = e.TryGetProperty("text", out var t) ? t.GetString() ?? id : id;
                string? image = e.TryGetProperty("image", out var img) && img.GetString() is { } path
                    ? (path.StartsWith("http", StringComparison.Ordinal) ? path : ImageHost + path)
                    : null;
                // Prices use the "Currency" group ids; keep the first entry if an id repeats.
                byId.TryAdd(id, new CurrencyInfo(id, name, image));
            }
        }
        return new CurrencyCatalog(byId);
    }
}
