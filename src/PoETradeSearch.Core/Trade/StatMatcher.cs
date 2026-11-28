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

    /// <summary>
    /// The tier's range in the same units as <see cref="SearchValue"/> (averaged for "Adds # to #"), when the item
    /// text came from the advanced copy. "물리 피해 11(11-14)~23(21-25) 추가" → 16–19.5.
    /// </summary>
    public ValueRange? SearchRange { get; init; }

    /// <summary>Which numbers of the item line (0-based, across joined lines) the stat's "#" placeholders took.</summary>
    internal IReadOnlyList<int> ValueOrdinals { get; init; } = [];
    internal bool Negated { get; init; }
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

    private static readonly (string From, string To)[] OppositeWords =
    [
        ("increased", "reduced"), ("reduced", "increased"),
        ("more", "less"), ("less", "more"),
        ("증가", "감소"), ("감소", "증가"),
    ];

    /// <summary>How "local" stats are marked: " (Local)" on the global site, "(특정)" on Kakao.</summary>
    private static readonly string[] LocalSuffixes = [" (Local)", "(특정)"];

    // Key: "group|shape", value: candidates in the order the API lists them.
    private readonly Dictionary<string, List<Candidate>> _byShape = new(StringComparer.Ordinal);

    public int StatCount { get; }

    [GeneratedRegex(@"[+-]?(?:#|\d+(?:\.\d+)?)")]
    private static partial Regex NumberOrPlaceholderRegex();

    // Same number pattern the parser uses for ItemMod.Values, so ordinals line up with Values/Ranges.
    [GeneratedRegex(@"[+-]?\d+(?:\.\d+)?")]
    private static partial Regex NumberOnlyRegex();

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
        // Weapon and armour defence/damage mods are "local" (they modify the item itself).
        var category = ItemCategories.FromItemClass(item.ItemClass);
        bool preferLocal = category is not null &&
                           (category.StartsWith("weapon.", StringComparison.Ordinal) ||
                            (category.StartsWith("armour.", StringComparison.Ordinal) && category != "armour.quiver"));
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
                    results.Add(WithRange(two with { Source = [mod, item.Mods[i + 1]] }));
                    i++;
                    continue;
                }
            }

            results.Add(TryMatch(mod.Text, mod.Type, preferLocal) is { } one
                ? WithRange(one with { Source = [mod] })
                : new MatchedMod([mod], null, [], mod.Values, null));
        }

        return results;
    }

    /// <summary>Turns the source lines' tier ranges into a range for the search value.</summary>
    private static MatchedMod WithRange(MatchedMod match)
    {
        var values = match.Source.SelectMany(s => s.Values).ToList();
        var ranges = match.Source.SelectMany(s => s.Ranges ?? s.Values.Select(_ => (ValueRange?)null)).ToList();
        if (match.ValueOrdinals.Count == 0 || ranges.Count != values.Count ||
            !match.ValueOrdinals.Any(o => o < ranges.Count && ranges[o] is not null))
            return match;

        // A number without its own range (fixed part of a hybrid roll) counts as exactly its value.
        double lo = match.ValueOrdinals.Average(o => ranges[o]?.Min ?? values[o]);
        double hi = match.ValueOrdinals.Average(o => ranges[o]?.Max ?? values[o]);
        var range = match.Negated ? new ValueRange(-hi, -lo) : new ValueRange(lo, hi);
        return match with { SearchRange = range };
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

        // On weapons/armour prefer local stats; elsewhere use the plain stat and only then try local.
        var local = LocalSuffixes.Select(suffix => (Shape: shape + suffix, Suffix: suffix));
        (string Shape, string Suffix)[] shapes = preferLocal
            ? [.. local, (shape, "")]
            : [(shape, ""), .. local];

        foreach (var (s, suffix) in shapes)
        {
            if (!_byShape.TryGetValue(Key(group, s), out var candidates))
                continue;

            // Local candidates carry the suffix in their regex, so match against the text with it appended.
            var input = text + suffix;

            var matching = new List<(Candidate Candidate, List<double> Values, List<int> Ordinals)>();
            foreach (var c in candidates)
            {
                if (c.TryExtract(input) is { } extracted)
                    matching.Add((c, extracted.Values, extracted.Ordinals));
            }
            if (matching.Count == 0)
                continue;

            var (best, bestValues, ordinals) = matching[0];
            if (negate)
                bestValues = bestValues.Select(v => -v).ToList();

            var alternatives = matching.Skip(1)
                .Where(m => m.Candidate.Stat.Text == best.Stat.Text)
                .Select(m => m.Candidate.Stat.Id)
                .ToList();

            double? searchValue = bestValues.Count > 0 ? bestValues.Average() : null;
            return new MatchedMod([], best.Stat, alternatives, bestValues, searchValue)
            {
                ValueOrdinals = ordinals,
                Negated = negate,
            };
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

        /// <summary>
        /// The "#" values, plus which number of the text each one is (counting every number, literal or not),
        /// so the matching tier ranges can be looked up on the item line.
        /// </summary>
        public (List<double> Values, List<int> Ordinals)? TryExtract(string text)
        {
            _regex ??= BuildRegex(Stat.Text);
            var m = _regex.Match(text);
            if (!m.Success)
                return null;
            var numbers = NumberOnlyRegex().Matches(text);
            var values = new List<double>();
            var ordinals = new List<int>();
            for (int g = 1; g < m.Groups.Count; g++)
            {
                values.Add(double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture));
                int index = m.Groups[g].Index;
                ordinals.Add(numbers.Count(n => n.Index < index));
            }
            return (values, ordinals);
        }
    }
}
