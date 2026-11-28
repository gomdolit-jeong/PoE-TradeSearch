using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.Core.Trade;

/// <summary>Which mods are ticked when the search window opens, and with which values.</summary>
public static class ModDefaults
{
    /// <summary>
    /// Numeric stats that decide which variant of an item it is, so they are searched by exact value:
    /// cluster jewel passive counts and Voices' socket / empty-passive counts.
    /// </summary>
    private static readonly HashSet<string> VariantStatNumbers =
    [
        "stat_3086156145", // Adds # Passive Skills
        "stat_4079888060", // # Added Passive Skills are Jewel Sockets
        "stat_247746531",  // Adds # Jewel Socket Passive Skills (Voices)
        "stat_1085446536", // Adds # Small Passive Skills which grant nothing (Voices)
    ];

    /// <summary>Item level thresholds where cluster jewels unlock new notables and mod tiers.</summary>
    private static readonly int[] ClusterItemLevelSteps = [84, 75, 68, 50];

    /// <summary>
    /// For cluster jewels the item level decides which notables can roll, so it is searched from the
    /// start of the item's bracket: 83 → 75 or more, 84 → 84 or more. Uniques don't need it.
    /// </summary>
    public static int? ClusterItemLevelMin(ParsedItem item)
    {
        if (!IsClusterJewel(item) || item.Rarity == ItemRarity.Unique || item.ItemLevel is not { } ilvl)
            return null;
        foreach (var step in ClusterItemLevelSteps)
            if (ilvl >= step)
                return step;
        return null;
    }

    public static bool IsClusterJewel(ParsedItem item) =>
        item.BaseType is { } b && (b.EndsWith("Cluster Jewel", StringComparison.Ordinal) || b.Contains("군 주얼", StringComparison.Ordinal));

    /// <summary>
    /// A stat that names one choice out of a list: Forbidden Flame/Flesh notable, Thread of Hope ring size,
    /// anoints, cluster jewel types. The trade site gives each choice its own id: "explicit.stat_1190333629|4194".
    /// </summary>
    public static bool IsChoiceStat(StatDefinition stat) => stat.Id.Contains('|');

    public static bool IsVariantStat(StatDefinition stat) =>
        IsChoiceStat(stat) || VariantStatNumbers.Contains(StatNumber(stat.Id));

    public static bool EnabledByDefault(ParsedItem item, MatchedMod match)
    {
        if (match.Stat is null)
            return false;
        var type = match.Source[0].Type;

        // Cluster jewel enchants (passive count, small passive type) define what the jewel is.
        if (type == ModType.Enchant)
            return IsClusterJewel(item) && IsVariantStat(match.Stat);

        // Synthesised implicits are random and are what the item is bought for.
        if (type == ModType.Implicit && item.Synthesised)
            return true;

        // Implicits from a Vaal orb are rare and priced by which one it got.
        if (type == ModType.Implicit && match.Source.Any(m => m.FromCorruption))
            return true;

        // Uniques are searched by name; only the mods that tell variants apart are ticked:
        // - choice stats and variant counts (Forbidden notable, Voices sockets, Thread of Hope ring)
        // - any fixed-text mod without "#", e.g. Megalomaniac's "1 Added Passive Skill is Pure Agony".
        //   Fixed mods every copy has (Headhunter's "gain its Modifiers") are ticked too; they match all
        //   listings, so they don't narrow the result.
        // - the mutated mod of a Foulborn unique (when the advanced copy marks it)
        if (item.Rarity == ItemRarity.Unique)
            return IsVariantStat(match.Stat) || !match.Stat.Text.Contains('#') || match.Source.Any(m => m.Mutated);

        // Rare/magic: the item's own rolled mods. Crafted mods stay off (buyers can bench-craft them).
        return type is ModType.Explicit or ModType.Fractured;
    }

    /// <summary>Variant counts are searched exactly (3 sockets, not "3 or more").</summary>
    public static bool UseExactValue(MatchedMod match) =>
        match.Stat is not null && VariantStatNumbers.Contains(StatNumber(match.Stat.Id));

    private static string StatNumber(string id)
    {
        var dot = id.IndexOf('.');
        var bar = id.IndexOf('|');
        var start = dot + 1;
        var end = bar >= 0 ? bar : id.Length;
        return id[start..end];
    }
}
