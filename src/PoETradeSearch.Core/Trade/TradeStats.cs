using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoETradeSearch.Core.Trade;

/// <summary>One entry of /api/trade/data/stats, e.g. ("explicit.stat_3299347043", "+# to maximum Life", "explicit").</summary>
public sealed record StatDefinition(string Id, string Text, string Type);

/// <summary>Loads the JSON returned by https://www.pathofexile.com/api/trade/data/stats.</summary>
public static class TradeStatsLoader
{
    public static IReadOnlyList<StatDefinition> Parse(string json)
    {
        var response = JsonSerializer.Deserialize<StatsResponse>(json)
                       ?? throw new FormatException("Empty trade stats response.");

        var stats = new List<StatDefinition>();
        foreach (var group in response.Result)
        foreach (var entry in group.Entries)
            stats.Add(new StatDefinition(entry.Id, entry.Text, entry.Type ?? group.Id));
        return stats;
    }

    private sealed class StatsResponse
    {
        [JsonPropertyName("result")] public List<StatGroup> Result { get; set; } = [];
    }

    private sealed class StatGroup
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("entries")] public List<StatEntry> Entries { get; set; } = [];
    }

    private sealed class StatEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}
