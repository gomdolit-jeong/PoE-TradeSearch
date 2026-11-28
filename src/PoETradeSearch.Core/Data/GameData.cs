using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoETradeSearch.Core.Data;

/// <summary>
/// Game words and trade filter definitions that can change between leagues (item flags, influences, name
/// prefixes, item classes, ...). They live in Data/game-data.json, embedded in the program, so a new league
/// mechanic is usually one JSON entry instead of a code change. A user copy of the file can replace it
/// without rebuilding: see <see cref="Load"/>.
/// </summary>
public sealed class GameData
{
    /// <summary>A line like "Fractured Item" under the item. With <see cref="TradeFilter"/> it becomes a search chip.</summary>
    public sealed record ItemFlagDefinition(
        string Id,
        IReadOnlyList<string> Text,
        string? TradeFilter = null,
        string? Label = null,
        string? Tooltip = null);

    public sealed record InfluenceDefinition(string Id, IReadOnlyList<string> Text, string Label);

    public sealed record PrefixDefinitions(IReadOnlyList<string> Foulborn, IReadOnlyList<string> Synthesised);

    public sealed record ModHeaderWords(IReadOnlyList<string> Implicit, IReadOnlyList<string> Corrupted, IReadOnlyList<string> Mutated);

    /// <summary>
    /// Special handling for an item class ("살덩이" / "Viscera", ...): base-name prefixes the trade site doesn't
    /// list ("상위 " / "Greater "), the rarity when the copy has no rarity line, and whether implicits are ticked.
    /// </summary>
    public sealed record ItemClassRule(
        IReadOnlyList<string> ItemClass,
        IReadOnlyList<string>? BasePrefixes = null,
        string? Rarity = null,
        bool CheckImplicits = false,
        bool ModsAreImplicit = false,
        int? MaxModSections = null)
    {
        public IReadOnlyList<string> BasePrefixes { get; init; } = BasePrefixes ?? [];
    }

    public IReadOnlyList<ItemFlagDefinition> ItemFlags { get; init; } = [];
    public IReadOnlyList<InfluenceDefinition> Influences { get; init; } = [];
    public PrefixDefinitions NamePrefixes { get; init; } = new([], []);
    public ModHeaderWords ModHeaders { get; init; } = new([], [], []);
    public IReadOnlyList<string> VariantStats { get; init; } = [];
    public IReadOnlyList<int> ClusterItemLevelSteps { get; init; } = [];
    public IReadOnlyDictionary<string, string> CurrencyNames { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ItemCategories { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<ItemClassRule> ItemClassRules { get; init; } = [];

    /// <summary>Start of lines that explain how to use the item ("Place into...", "마을이나 은신처에..."), not mods.</summary>
    public IReadOnlyList<string> UsageHints { get; init; } = [];

    /// <summary>File name of the user override in the app's data folder.</summary>
    public const string FileName = "game-data.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private static GameData? _current;

    /// <summary>The data in use: the user override if one was loaded, otherwise the built-in file.</summary>
    public static GameData Current
    {
        get => _current ??= BuiltIn();
        set => _current = value;
    }

    /// <summary>Where the data came from, for the settings window.</summary>
    public static string Source { get; private set; } = "내장";

    public static GameData Parse(string json) =>
        JsonSerializer.Deserialize<GameData>(json, JsonOptions) ?? throw new FormatException("game-data.json is empty.");

    public static GameData BuiltIn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PoETradeSearch.Core.Data.game-data.json")
            ?? throw new InvalidOperationException("Embedded game-data.json is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Uses <paramref name="directory"/>/game-data.json when it exists and is valid, otherwise the built-in data.
    /// Returns an error message when the user file was found but couldn't be read.
    /// </summary>
    public static string? Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            Current = BuiltIn();
            Source = "내장";
            return null;
        }
        try
        {
            Current = Parse(File.ReadAllText(path));
            Source = path;
            return null;
        }
        catch (Exception ex)
        {
            Current = BuiltIn();
            Source = "내장";
            return $"{path}를 읽지 못해 기본값을 씁니다: {ex.Message}";
        }
    }

    // ---------------- Lookups ----------------

    public ItemFlagDefinition? FlagByText(string line) => ItemFlags.FirstOrDefault(f => f.Text.Contains(line));
    public ItemFlagDefinition? Flag(string id) => ItemFlags.FirstOrDefault(f => f.Id == id);
    public InfluenceDefinition? InfluenceByText(string line) => Influences.FirstOrDefault(i => i.Text.Contains(line));
    public string InfluenceLabel(string id) => Influences.FirstOrDefault(i => i.Id == id)?.Label ?? id;

    public ItemClassRule? ClassRule(string? itemClass) =>
        itemClass is null ? null : ItemClassRules.FirstOrDefault(r => r.ItemClass.Contains(itemClass.Trim()));

    /// <summary>Flags that are searched with a trade filter (shown as chips).</summary>
    public IEnumerable<ItemFlagDefinition> SearchableFlags => ItemFlags.Where(f => f.TradeFilter is not null);

    public static string? StripPrefix(string text, IEnumerable<string> prefixes)
    {
        foreach (var p in prefixes)
            if (text.StartsWith(p, StringComparison.Ordinal))
                return text[p.Length..];
        return null;
    }

    public static bool ContainsAny(string text, IEnumerable<string> words) =>
        words.Any(w => text.Contains(w, StringComparison.Ordinal));
}

/// <summary>Ids of the item flags the parser and query builder treat specially.</summary>
public static class FlagIds
{
    public const string Corrupted = "corrupted";
    public const string Unidentified = "unidentified";
    public const string Mirrored = "mirrored";
    public const string Split = "split";
    public const string Fractured = "fractured";
    public const string Synthesised = "synthesised";
    public const string Searing = "searing";
    public const string Tangled = "tangled";
}
