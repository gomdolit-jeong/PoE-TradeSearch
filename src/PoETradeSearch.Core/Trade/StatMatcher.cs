using System.Globalization;
using System.Text.RegularExpressions;
using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.Core.Trade;

/// <summary>A mod from the item matched to a trade stat.</summary>
/// <param name="Source">Item mod lines this match covers (two for multi-line stats).</param>
/// <param name="Stat">Matched trade stat, or null if nothing matched.</param>
/// <param name="AlternativeIds">Other stat IDs with exactly the same text (the trade site has a few duplicates).</param>
/// <param name="Values">Values for the stat's "#" placeholders, already negated when "reduced" was matched to "increased".</param>
/// <param name="SearchValue">The value the trade site filters on: the single value, or the average for "Adds # to #".</param>
public sealed record MatchedMod(
    IReadOnlyList<ItemMod> Source,
    StatDefinition? Stat,
    IReadOnlyList<string> AlternativeIds,
    IReadOnlyList<double> Values,
    double? SearchValue)
{
    public bool IsMatched => Stat is not null;
    public string DisplayText => string.Join(" / ", Source.Select(s => s.Text));
}

/// <summary>
/// Maps parsed item mods to trade stat IDs.
/// <para>
/// Every stat text and item line is reduced to a "shape" key where each number or "#" (with its sign) becomes "#".
/// Candidates with the same shape are then checked with a regex built from the stat text, so stats that contain
/// literal numbers ("...you gain its Modifiers for 60 seconds") only match the right lines.
/// </para>
/// </summary>
public sealed partial class StatMatcher
{
    private static readonly Dictionary<ModType, string> TypeToGroup = new()
    {
        [ModType.Explicit] = "explicit",
        [ModType.Implicit] = "implicit",
        [ModType.Crafted] = "crafted",
        [ModType.Enchant] = "enchant",
        [ModType.Fractured] = "fractured",
        [ModType.Scourge] = "scourge",
        [ModType.Crucible] = "crucible",
    };

    // Item classes whose defence/damage mods are "local" (they modify the item itself).
    private static readonly HashSet<string> LocalModItemClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Body Armours", "Helmets", "Gloves", "Boots", "Shields",
        "Claws", "Daggers", "Rune Daggers", "Wands", "One Hand Swords", "Thrusting One Hand Swords",
        "One Hand Axes", "One Hand Maces", "Sceptres", "Bows", "Staves", "Warstaves",
        "Two Hand Swords", "Two Hand Axes", "Two Hand Maces", "Fishing Rods",
    };

    private static readonly (string From, string To)[] OppositeWords =
    [
        ("increased", "reduced"), ("reduced", "increased"),
        ("more", "less"), ("less", "more"),
    ];

    private const string LocalSuffix = " (Local)";

    // Key: "group|shape", value: candidates in the order the API lists them.
    private readonly Dictionary<string, List<Candidate>> _byShape = new(StringComparer.Ordinal);

    public int StatCount { get; }

    [GeneratedRegex(@"[+-]?(?:#|\d+(?:\.\d+)?)")]
    private static partial Regex NumberOrPlaceholderRegex();

    public StatMatcher(IEnumerable<StatDefinition> stats)
    {
        foreach (var stat in stats)
        {
            var key = Key(stat.Type, Shape(stat.Text));
            if (!_byShape.TryGetValue(key, out var list))
                _byShape[key] = list = [];
            list.Add(new Candidate(stat));
            StatCount++;
        }
    }

    public IReadOnlyList<MatchedMod> Match(ParsedItem item)
    {
        bool preferLocal = item.ItemClass is not null && LocalModItemClasses.Contains(item.ItemClass);
        var results = new List<MatchedMod>();

        for (int i = 0; i < item.Mods.Count; i++)
        {
            var mod = item.Mods[i];

            // Multi-line stats: try this line joined with the next one first.
            if (i + 1 < item.Mods.Count && item.Mods[i + 1].Type == mod.Type)
            {
                var joined = mod.Text + "\n" + item.Mods[i + 1].Text;
                var two = TryMatch(joined, mod.Type, preferLocal);
                if (two is not null)
                {
                    results.Add(two with { Source = [mod, item.Mods[i + 1]] });
                    i++;
                    continue;
                }
            }

            results.Add(TryMatch(mod.Text, mod.Type, preferLocal) is { } one
                ? one with { Source = [mod] }
                : new MatchedMod([mod], null, [], mod.Values, null));
        }

        return results;
    }

    private MatchedMod? TryMatch(string text, ModType type, bool preferLocal)
    {
        if (!TypeToGroup.TryGetValue(type, out var group))
            return null;

        // Fractured/crafted/... mods that the API doesn't list separately fall back to explicit.
        string[] groups = group == "explicit" ? ["explicit"] : [group, "explicit"];

        foreach (var g in groups)
        {
            var result = TryMatchInGroup(text, g, preferLocal, negate: false);
            if (result is not null)
                return result;

            // "10% reduced Mana Cost" is usually listed only as "#% increased Mana Cost" with a negative value.
            foreach (var (from, to) in OppositeWords)
            {
                var swapped = Regex.Replace(text, $@"\b{from}\b", to);
                if (swapped == text)
                    continue;
                result = TryMatchInGroup(swapped, g, preferLocal, negate: true);
                if (result is not null)
                    return result;
            }
        }

        return null;
    }

    private MatchedMod? TryMatchInGroup(string text, string group, bool preferLocal, bool negate)
    {
        var shape = Shape(text);

        // On weapons/armour prefer "(Local)" stats; elsewhere use the plain stat and only then try local.
        string[] shapes = preferLocal ? [shape + LocalSuffix, shape] : [shape, shape + LocalSuffix];

        foreach (var s in shapes)
        {
            if (!_byShape.TryGetValue(Key(group, s), out var candidates))
                continue;

            // Local candidates carry the suffix in their regex, so match against the text with it appended.
            var input = s.EndsWith(LocalSuffix, StringComparison.Ordinal) ? text + LocalSuffix : text;

            var matching = new List<(Candidate Candidate, List<double> Values)>();
            foreach (var c in candidates)
            {
                var values = c.TryExtract(input);
                if (values is not null)
                    matching.Add((c, values));
            }
            if (matching.Count == 0)
                continue;

            var (best, bestValues) = matching[0];
            if (negate)
                bestValues = bestValues.Select(v => -v).ToList();

            var alternatives = matching.Skip(1)
                .Where(m => m.Candidate.Stat.Text == best.Stat.Text)
                .Select(m => m.Candidate.Stat.Id)
                .ToList();

            double? searchValue = bestValues.Count > 0 ? bestValues.Average() : null;
            return new MatchedMod([], best.Stat, alternatives, bestValues, searchValue);
        }

        return null;
    }

    private static string Key(string group, string shape) => group + "|" + shape;

    /// <summary>"+52 to maximum Life" and "+# to maximum Life" both become "# to maximum Life".</summary>
    internal static string Shape(string text) => NumberOrPlaceholderRegex().Replace(text, "#");

    /// <summary>
    /// Regex that matches item text against a stat text: "#" captures a number, literal numbers must match exactly,
    /// and a "+"/"-" sign is optional so "-5% to Fire Resistance" still matches "+#% to Fire Resistance".
    /// </summary>
    private static Regex BuildRegex(string statText)
    {
        var pattern = new System.Text.StringBuilder("^");
        int last = 0;
        foreach (Match m in NumberOrPlaceholderRegex().Matches(statText))
        {
            pattern.Append(Regex.Escape(statText[last..m.Index]));
            var token = m.Value.TrimStart('+', '-');
            pattern.Append(token == "#"
                ? @"([+-]?\d+(?:\.\d+)?)"
                : @"[+-]?" + Regex.Escape(token));
            last = m.Index + m.Length;
        }
        pattern.Append(Regex.Escape(statText[last..]));
        pattern.Append('$');
        return new Regex(pattern.ToString(), RegexOptions.CultureInvariant);
    }

    private sealed class Candidate(StatDefinition stat)
    {
        private Regex? _regex;

        public StatDefinition Stat { get; } = stat;

        public List<double>? TryExtract(string text)
        {
            _regex ??= BuildRegex(Stat.Text);
            var m = _regex.Match(text);
            if (!m.Success)
                return null;
            var values = new List<double>();
            for (int g = 1; g < m.Groups.Count; g++)
                values.Add(double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture));
            return values;
        }
    }
}
