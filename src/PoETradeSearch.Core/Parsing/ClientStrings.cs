namespace PoETradeSearch.Core.Parsing;

public enum ClientLanguage
{
    English,
    Korean,
}

/// <summary>
/// The fixed words the game client puts into copied item text, per language.
/// Korean strings follow the Kakao client (source: Awakened PoE Trade's ko/client_strings.js, MIT).
/// Mod markers like "(implicit)" and "(augmented)" stay in English in every client.
/// </summary>
public sealed class ClientStrings
{
    public required ClientLanguage Language { get; init; }
    public required string ItemClass { get; init; }
    public required string Rarity { get; init; }
    public required string ItemLevel { get; init; }
    public required string Quality { get; init; }
    public required string Sockets { get; init; }
    public required string StackSize { get; init; }
    public required string GemLevel { get; init; }
    public required string Note { get; init; }
    public required IReadOnlyDictionary<string, ItemRarity> Rarities { get; init; }
    public required IReadOnlyDictionary<string, ItemFlag> Flags { get; init; }
    public required IReadOnlyDictionary<string, string> Influences { get; init; }
    public required IReadOnlyList<string> UsageHints { get; init; }

    public static readonly ClientStrings English = new()
    {
        Language = ClientLanguage.English,
        ItemClass = "Item Class:",
        Rarity = "Rarity:",
        ItemLevel = "Item Level",
        Quality = "Quality",
        Sockets = "Sockets",
        StackSize = "Stack Size",
        GemLevel = "Level",
        Note = "Note:",
        Rarities = new Dictionary<string, ItemRarity>
        {
            ["Normal"] = ItemRarity.Normal,
            ["Magic"] = ItemRarity.Magic,
            ["Rare"] = ItemRarity.Rare,
            ["Unique"] = ItemRarity.Unique,
            ["Gem"] = ItemRarity.Gem,
            ["Currency"] = ItemRarity.Currency,
            ["Divination Card"] = ItemRarity.DivinationCard,
        },
        Flags = new Dictionary<string, ItemFlag>
        {
            ["Corrupted"] = ItemFlag.Corrupted,
            ["Twice Corrupted"] = ItemFlag.Corrupted,
            ["Unidentified"] = ItemFlag.Unidentified,
            ["Mirrored"] = ItemFlag.Mirrored,
            ["Split"] = ItemFlag.Split,
            ["Synthesised Item"] = ItemFlag.Synthesised,
            ["Fractured Item"] = ItemFlag.Fractured,
            ["Searing Exarch Item"] = ItemFlag.SearingExarch,
            ["Eater of Worlds Item"] = ItemFlag.EaterOfWorlds,
        },
        Influences = new Dictionary<string, string>
        {
            ["Shaper Item"] = "shaper",
            ["Elder Item"] = "elder",
            ["Crusader Item"] = "crusader",
            ["Hunter Item"] = "hunter",
            ["Redeemer Item"] = "redeemer",
            ["Warlord Item"] = "warlord",
        },
        UsageHints = ["Place into", "Right click"],
    };

    public static readonly ClientStrings Korean = new()
    {
        Language = ClientLanguage.Korean,
        ItemClass = "아이템 종류:",
        Rarity = "아이템 희귀도:",
        ItemLevel = "아이템 레벨",
        Quality = "퀄리티",
        Sockets = "홈",
        StackSize = "중첩 개수",
        GemLevel = "레벨",
        Note = "메모:",
        Rarities = new Dictionary<string, ItemRarity>
        {
            ["일반"] = ItemRarity.Normal,
            ["마법"] = ItemRarity.Magic,
            ["희귀"] = ItemRarity.Rare,
            ["고유"] = ItemRarity.Unique,
            ["젬"] = ItemRarity.Gem,
            ["화폐"] = ItemRarity.Currency,
            ["점술 카드"] = ItemRarity.DivinationCard,
        },
        Flags = new Dictionary<string, ItemFlag>
        {
            ["타락"] = ItemFlag.Corrupted,
            ["두 번 타락"] = ItemFlag.Corrupted,
            ["미확인"] = ItemFlag.Unidentified,
            ["복제"] = ItemFlag.Mirrored,
            ["분할됨"] = ItemFlag.Split,
            ["결합된 아이템"] = ItemFlag.Synthesised,
            // Not in the reference strings; the "(fractured)" mod marker sets the flag as well.
            ["분열된 아이템"] = ItemFlag.Fractured,
            ["작열의 총주교 아이템"] = ItemFlag.SearingExarch,
            ["세계 포식자 아이템"] = ItemFlag.EaterOfWorlds,
        },
        Influences = new Dictionary<string, string>
        {
            ["쉐이퍼 아이템"] = "shaper",
            ["엘더 아이템"] = "elder",
            ["십자군 아이템"] = "crusader",
            ["사냥꾼 아이템"] = "hunter",
            ["대속자 아이템"] = "redeemer",
            ["전쟁군주 아이템"] = "warlord",
        },
        UsageHints = [],
    };

    public static IReadOnlyList<ClientStrings> All { get; } = [English, Korean];

    /// <summary>The language whose header the text starts with, or null if it isn't a PoE item.</summary>
    public static ClientStrings? Detect(string text)
    {
        foreach (var s in All)
            if (text.StartsWith(s.ItemClass, StringComparison.Ordinal) || text.StartsWith(s.Rarity, StringComparison.Ordinal))
                return s;
        return null;
    }
}

public enum ItemFlag
{
    Corrupted,
    Unidentified,
    Mirrored,
    Split,
    Synthesised,
    Fractured,
    SearingExarch,
    EaterOfWorlds,
}
