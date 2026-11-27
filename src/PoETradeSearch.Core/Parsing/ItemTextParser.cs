using System.Globalization;
using System.Text.RegularExpressions;

namespace PoETradeSearch.Core.Parsing;

/// <summary>
/// Parses the text PoE1 (English client) puts on the clipboard when you press Ctrl+C over an item.
/// Also accepts the advanced copy format (Ctrl+Alt+C); the "{ Prefix Modifier ... }" header lines are skipped.
/// </summary>
public static partial class ItemTextParser
{
    private const string SectionSeparator = "--------";

    private static readonly Dictionary<string, ModType> ModSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["implicit"] = ModType.Implicit,
        ["crafted"] = ModType.Crafted,
        ["enchant"] = ModType.Enchant,
        ["fractured"] = ModType.Fractured,
        ["scourge"] = ModType.Scourge,
        ["crucible"] = ModType.Crucible,
    };

    private static readonly Dictionary<string, string> InfluenceLines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shaper Item"] = "shaper",
        ["Elder Item"] = "elder",
        ["Crusader Item"] = "crusader",
        ["Hunter Item"] = "hunter",
        ["Redeemer Item"] = "redeemer",
        ["Warlord Item"] = "warlord",
        ["Searing Exarch Item"] = "searing_exarch",
        ["Eater of Worlds Item"] = "eater_of_worlds",
    };

    [GeneratedRegex(@"[+-]?\d+(?:\.\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"^(?<text>.*?)\s*\((?<kind>implicit|crafted|enchant|fractured|scourge|crucible)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex ModSuffixRegex();

    [GeneratedRegex(@"^(?<key>[^:]+):\s*(?<value>.*)$")]
    private static partial Regex KeyValueRegex();

    public static bool LooksLikeItem(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.StartsWith("Item Class:", StringComparison.Ordinal) || text.StartsWith("Rarity:", StringComparison.Ordinal)) &&
        text.Contains(SectionSeparator, StringComparison.Ordinal);

    public static ParsedItem Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!LooksLikeItem(text))
            throw new FormatException("Clipboard text is not a Path of Exile item.");

        var item = new ParsedItem { RawText = text };
        var sections = SplitSections(text);

        ParseHeader(sections[0], item);

        // Sections that come before "Item Level" are properties/requirements/sockets.
        // Mod sections come after it. Items without an item level (gems, currency) have no mod sections we care about.
        bool pastItemLevel = false;

        for (int i = 1; i < sections.Count; i++)
        {
            var section = sections[i];

            if (TryParseFlagSection(section, item))
                continue;

            if (IsKeyValueSection(section))
            {
                foreach (var line in section)
                    ParseKeyValue(line, item, ref pastItemLevel);
                continue;
            }

            if (!pastItemLevel)
                continue;

            if (IsModSection(section, item))
            {
                foreach (var line in section)
                {
                    var mod = ParseModLine(line);
                    if (mod is not null)
                        item.Mods.Add(mod);
                }
            }
        }

        return item;
    }

    public static ItemMod? ParseModLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line.StartsWith('{'))
            return null;

        var type = ModType.Explicit;
        var match = ModSuffixRegex().Match(line);
        if (match.Success)
        {
            type = ModSuffixes[match.Groups["kind"].Value];
            line = match.Groups["text"].Value;
        }

        // Advanced copy puts the roll range in parentheses: "+52(40-54) to maximum Life".
        line = Regex.Replace(line, @"\((?:[+-]?\d+(?:\.\d+)?)-(?:[+-]?\d+(?:\.\d+)?)\)", "");

        var values = new List<double>();
        foreach (Match m in NumberRegex().Matches(line))
            values.Add(double.Parse(m.Value, CultureInfo.InvariantCulture));

        // Keep the sign in the template ("+#"), since trade stat texts use it.
        var template = NumberRegex().Replace(line, m => m.Value[0] is '+' or '-' ? m.Value[0] + "#" : "#");

        return new ItemMod(line, template, values, type);
    }

    private static List<List<string>> SplitSections(string text)
    {
        var sections = new List<List<string>> { new() };
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line == SectionSeparator)
            {
                sections.Add([]);
                continue;
            }
            if (line.Length > 0)
                sections[^1].Add(line);
        }
        sections.RemoveAll(s => s.Count == 0);
        return sections;
    }

    private static void ParseHeader(List<string> header, ParsedItem item)
    {
        var nameLines = new List<string>();
        foreach (var line in header)
        {
            if (line.StartsWith("Item Class:", StringComparison.Ordinal))
                item.ItemClass = line["Item Class:".Length..].Trim();
            else if (line.StartsWith("Rarity:", StringComparison.Ordinal))
                item.Rarity = ParseRarity(line["Rarity:".Length..].Trim());
            else
                nameLines.Add(line);
        }

        if (nameLines.Count >= 2)
        {
            item.Name = nameLines[0];
            item.BaseType = nameLines[1];
        }
        else if (nameLines.Count == 1)
        {
            item.BaseType = nameLines[0];
        }
    }

    private static ItemRarity ParseRarity(string value) => value switch
    {
        "Normal" => ItemRarity.Normal,
        "Magic" => ItemRarity.Magic,
        "Rare" => ItemRarity.Rare,
        "Unique" => ItemRarity.Unique,
        "Gem" => ItemRarity.Gem,
        "Currency" => ItemRarity.Currency,
        "Divination Card" => ItemRarity.DivinationCard,
        _ => ItemRarity.Unknown,
    };

    /// <summary>Sections that only contain flag words like "Corrupted" or "Shaper Item".</summary>
    private static bool TryParseFlagSection(List<string> section, ParsedItem item)
    {
        bool allFlags = section.All(l =>
            l is "Corrupted" or "Unidentified" or "Mirrored" or "Synthesised Item" || InfluenceLines.ContainsKey(l));
        if (!allFlags)
            return false;

        foreach (var line in section)
        {
            switch (line)
            {
                case "Corrupted": item.Corrupted = true; break;
                case "Unidentified": item.Unidentified = true; break;
                case "Mirrored": item.Mirrored = true; break;
                case "Synthesised Item": item.Synthesised = true; break;
                default: item.Influences.Add(InfluenceLines[line]); break;
            }
        }
        return true;
    }

    private static bool IsKeyValueSection(List<string> section)
    {
        // "Requirements:" header line has an empty value; everything else must be "Key: Value".
        return section.All(l => KeyValueRegex().IsMatch(l) && !ModSuffixRegex().IsMatch(l));
    }

    private static void ParseKeyValue(string line, ParsedItem item, ref bool pastItemLevel)
    {
        var m = KeyValueRegex().Match(line);
        if (!m.Success)
            return;

        var key = m.Groups["key"].Value.Trim();
        var value = m.Groups["value"].Value.Trim();

        switch (key)
        {
            case "Item Level":
                item.ItemLevel = FirstInt(value);
                pastItemLevel = true;
                break;
            case "Quality":
                item.Quality = FirstInt(value);
                break;
            case "Sockets":
                item.Sockets = value;
                break;
            case "Stack Size":
                item.StackSize = FirstInt(value);
                break;
            case "Level" when item.Rarity == ItemRarity.Gem && item.GemLevel is null:
                item.GemLevel = FirstInt(value);
                break;
            case "Requirements":
                break;
            default:
                item.Properties.TryAdd(key, value);
                break;
        }
    }

    /// <summary>
    /// Mod sections contain at least one line with a number or a mod suffix. This filters out
    /// flavour text on uniques and usage hints ("Place into an allocated Jewel Socket...").
    /// </summary>
    private static bool IsModSection(List<string> section, ParsedItem item)
    {
        if (section.Any(l => l.StartsWith("Note:", StringComparison.Ordinal)))
            return false;
        if (section.Any(l => l.StartsWith("Place into", StringComparison.Ordinal) ||
                             l.StartsWith("Right click", StringComparison.Ordinal)))
            return false;

        // Only uniques have flavour text, so for other rarities every remaining section is mods.
        if (item.Rarity != ItemRarity.Unique)
            return true;

        return section.Any(l => ModSuffixRegex().IsMatch(l) || NumberRegex().IsMatch(l) || l.StartsWith('{'));
    }

    private static int? FirstInt(string value)
    {
        var m = NumberRegex().Match(value);
        return m.Success ? (int)double.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }
}
