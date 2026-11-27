using System.Text.Json;

namespace PoETradeSearch.Core.Trade;

/// <summary>
/// Transfigured gems from /api/trade/data/items: the copied name ("Arc of Surging", "뇌동의 연쇄 번개") must be
/// searched as its base gem plus a discriminator: {"type": {"option": "Arc", "discriminator": "alt_x"}}.
/// </summary>
public sealed class GemCatalog
{
    public sealed record GemType(string Type, string Discriminator);

    private readonly Dictionary<string, GemType> _byName;

    public static GemCatalog Empty { get; } = new([]);

    private GemCatalog(Dictionary<string, GemType> byName) => _byName = byName;

    public int Count => _byName.Count;

    public GemType? FindTransfigured(string? name) =>
        name is not null && _byName.TryGetValue(name, out var g) ? g : null;

    public static GemCatalog Parse(string itemsJson)
    {
        using var doc = JsonDocument.Parse(itemsJson);
        var byName = new Dictionary<string, GemType>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            if (group.GetProperty("id").GetString() != "gem" || !group.TryGetProperty("entries", out var entries))
                continue;
            foreach (var e in entries.EnumerateArray())
            {
                if (e.TryGetProperty("disc", out var disc) && e.TryGetProperty("text", out var text) &&
                    e.TryGetProperty("type", out var type) &&
                    text.GetString() is { } name && type.GetString() is { } baseType && disc.GetString() is { } d)
                    byName.TryAdd(name, new GemType(baseType, d));
            }
        }
        return new GemCatalog(byName);
    }
}
