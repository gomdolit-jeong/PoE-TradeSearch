namespace PoETradeSearch.Core.Parsing;

public enum ItemRarity
{
    Unknown,
    Normal,
    Magic,
    Rare,
    Unique,
    Gem,
    Currency,
    DivinationCard,
}

public enum ModType
{
    Explicit,
    Implicit,
    Crafted,
    Enchant,
    Fractured,
    Scourge,
    Crucible,
}

/// <summary>
/// One modifier line from the item text.
/// </summary>
/// <param name="Text">Original line without the "(implicit)" style suffix, e.g. "+52 to maximum Life".</param>
/// <param name="Template">Line with numbers replaced by "#", e.g. "+# to maximum Life". Used to match trade stat IDs.</param>
/// <param name="Values">Numbers found in the line, in order.</param>
public sealed record ItemMod(string Text, string Template, IReadOnlyList<double> Values, ModType Type);

public sealed class ParsedItem
{
    public string? ItemClass { get; set; }
    public ItemRarity Rarity { get; set; }

    /// <summary>Unique/rare name, e.g. "Storm Loop". Null for normal/magic items.</summary>
    public string? Name { get; set; }

    /// <summary>Base type, e.g. "Amethyst Ring". For magic items this is the full magic name.</summary>
    public string? BaseType { get; set; }

    public int? ItemLevel { get; set; }
    public int? Quality { get; set; }
    public int? GemLevel { get; set; }
    public string? Sockets { get; set; }
    public int? StackSize { get; set; }

    public bool Corrupted { get; set; }
    public bool Unidentified { get; set; }
    public bool Mirrored { get; set; }
    public bool Synthesised { get; set; }
    public List<string> Influences { get; } = [];

    /// <summary>Other "Key: Value" properties (Armour, Evasion Rating, Physical Damage, ...).</summary>
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ItemMod> Mods { get; } = [];

    public string RawText { get; set; } = "";
}
