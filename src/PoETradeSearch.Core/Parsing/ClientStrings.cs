namespace PoETradeSearch.Core.Parsing;

public enum ClientLanguage
{
    English,
    Korean,
}

/// <summary>
/// The fixed words of the copied item text's layout, per language: header keys and rarities. These don't
/// change between leagues. Item flags, influences and name prefixes, which do, are in Data/game-data.json
/// (<see cref="Data.GameData"/>).
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
