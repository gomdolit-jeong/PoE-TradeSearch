using System.Globalization;
using System.Text.RegularExpressions;

namespace PoETradeSearch.Core.Parsing;

/// <summary>
/// Parses the text PoE1 puts on the clipboard when you press Ctrl+C over an item, from the English
/// (global) or Korean (Kakao) client. Also accepts the advanced copy format (Ctrl+Alt+C); the
/// "{ Prefix Modifier ... }" header lines are skipped.
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

    [GeneratedRegex(@"[+-]?\d+(?:\.\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"^(?<text>.*?)\s*\((?<kind>implicit|crafted|enchant|fractured|scourge|crucible)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex ModSuffixRegex();

    [GeneratedRegex(@"^(?<key>[^:]+):\s*(?<value>.*)$")]
    private static partial Regex KeyValueRegex();

    public static bool LooksLikeItem(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        ClientStrings.Detect(text) is not null &&
        text.Contains(SectionSeparator, StringComparison.Ordinal);

    public static ParsedItem Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!LooksLikeItem(text))
            throw new FormatException("Clipboard text is not a Path of Exile item.");

        var strings = ClientStrings.Detect(text)!;
        var item = new ParsedItem { RawText = text, Language = strings.Language };
        var sections = SplitSections(text);

        ParseHeader(sections[0], item, strings);

        // Sections that come before "Item Level" are properties/requirements/sockets.
        // Mod sections come after it. Items without an item level (gems, currency) have no mod sections we care about.
        bool pastItemLevel = false;
        // Set after the first section with explicit mods; on uniques, later sections may be flavour text.
        bool explicitSectionSeen = false;

        for (int i = 1; i < sections.Count; i++)
        {
            var section = sections[i];

            if (TryParseFlagSection(section, item, strings))
                continue;

            // Before "Item Level" everything is properties/requirements/sockets. Weapon property sections start
            // with a plain line ("One Handed Axe"), so read the "Key: Value" lines and skip the rest.
            // After "Item Level" a "Key: Value" look is not enough: cluster jewel mods read
            // "Added Small Passive Skills also grant: +5 to Maximum Mana".
            if (!pastItemLevel)
            {
                foreach (var line in section)
                    if (!ModSuffixRegex().IsMatch(line))
                        ParseKeyValue(line, item, strings, ref pastItemLevel);
                continue;
            }

            if (IsModSection(section, item, strings, explicitSectionSeen))
            {
                bool hasExplicit = false;
                foreach (var line in section)
                {
                    var mod = ParseModLine(line);
                    if (mod is null)
                        continue;
                    item.Mods.Add(mod);
                    hasExplicit |= mod.Type is not (ModType.Implicit or ModType.Enchant);
                }
                explicitSectionSeen |= hasExplicit;
            }
        }

        // A "(fractured)" mod means a fractured item even if the flag line wasn't recognised.
        if (item.Mods.Any(m => m.Type == ModType.Fractured))
            item.FracturedItem = true;

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

    private static void ParseHeader(List<string> header, ParsedItem item, ClientStrings strings)
    {
        var nameLines = new List<string>();
        foreach (var line in header)
        {
            if (line.StartsWith(strings.ItemClass, StringComparison.Ordinal))
                item.ItemClass = line[strings.ItemClass.Length..].Trim();
            else if (line.StartsWith(strings.Rarity, StringComparison.Ordinal))
                item.Rarity = strings.Rarities.GetValueOrDefault(line[strings.Rarity.Length..].Trim(), ItemRarity.Unknown);
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

    /// <summary>Sections that only contain flag words like "Corrupted" or "Shaper Item".</summary>
    private static bool TryParseFlagSection(List<string> section, ParsedItem item, ClientStrings strings)
    {
        bool allFlags = section.All(l => strings.Flags.ContainsKey(l) || strings.Influences.ContainsKey(l));
        if (!allFlags)
            return false;

        foreach (var line in section)
        {
            if (strings.Influences.TryGetValue(line, out var influence))
            {
                item.Influences.Add(influence);
                continue;
            }
            switch (strings.Flags[line])
            {
                case ItemFlag.Corrupted: item.Corrupted = true; break;
                case ItemFlag.Unidentified: item.Unidentified = true; break;
                case ItemFlag.Mirrored: item.Mirrored = true; break;
                case ItemFlag.Split: item.Split = true; break;
                case ItemFlag.Synthesised: item.Synthesised = true; break;
                case ItemFlag.Fractured: item.FracturedItem = true; break;
                case ItemFlag.SearingExarch: item.SearingExarch = true; break;
                case ItemFlag.EaterOfWorlds: item.EaterOfWorlds = true; break;
            }
        }
        return true;
    }

    private static bool IsKeyValueSection(List<string> section)
    {
        // "Requirements:" header line has an empty value; everything else must be "Key: Value".
        return section.All(l => KeyValueRegex().IsMatch(l) && !ModSuffixRegex().IsMatch(l));
    }

    private static void ParseKeyValue(string line, ParsedItem item, ClientStrings strings, ref bool pastItemLevel)
    {
        var m = KeyValueRegex().Match(line);
        if (!m.Success)
            return;

        var key = m.Groups["key"].Value.Trim();
        var value = m.Groups["value"].Value.Trim();

        if (key == strings.ItemLevel)
        {
            item.ItemLevel = FirstInt(value);
            pastItemLevel = true;
        }
        else if (key == strings.Quality)
            item.Quality = FirstInt(value);
        else if (key == strings.Sockets)
            item.Sockets = value;
        else if (key == strings.StackSize)
            item.StackSize = FirstInt(value);
        else if (key == strings.GemLevel && item.Rarity == ItemRarity.Gem && item.GemLevel is null)
            item.GemLevel = FirstInt(value);
        else if (value.Length > 0) // "Requirements:" style headers have no value
            item.Properties.TryAdd(key, value);
    }

    /// <summary>
    /// Decides whether a section after "Item Level" holds mods, filtering out price notes, usage hints
    /// ("Place into an allocated Jewel Socket...") and unique flavour text.
    /// </summary>
    private static bool IsModSection(List<string> section, ParsedItem item, ClientStrings strings, bool explicitSectionSeen)
    {
        if (section.Any(l => l.StartsWith(strings.Note, StringComparison.Ordinal)))
            return false;
        if (section.Any(l => strings.UsageHints.Any(h => l.StartsWith(h, StringComparison.Ordinal))))
            return false;

        // Only uniques have flavour text, so for other rarities every remaining section is mods.
        if (item.Rarity != ItemRarity.Unique)
            return true;

        // A unique's first section after the implicits is its explicit mods, even without numbers
        // ("Allocates Tasalio, Cleansing Water if you have the matching modifier on Forbidden Flesh").
        if (!explicitSectionSeen)
            return true;

        // After that, only sections that look like mods; flavour text has no numbers or mod markers.
        return section.Any(l => ModSuffixRegex().IsMatch(l) || NumberRegex().IsMatch(l) || l.StartsWith('{'));
    }

    private static int? FirstInt(string value)
    {
        var m = NumberRegex().Match(value);
        return m.Success ? (int)double.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }
}
