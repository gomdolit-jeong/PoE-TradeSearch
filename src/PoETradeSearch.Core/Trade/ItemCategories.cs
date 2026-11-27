namespace PoETradeSearch.Core.Trade;

/// <summary>
/// Maps the "Item Class:" / "아이템 종류:" line to the trade site's category filter (see /api/trade/data/filters).
/// Korean names come from the Kakao trade site's category list, which matches the Kakao client's item classes.
/// </summary>
public static class ItemCategories
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // English client
        ["Rings"] = "accessory.ring",
        ["Amulets"] = "accessory.amulet",
        ["Belts"] = "accessory.belt",
        ["Trinkets"] = "accessory.trinket",
        ["Body Armours"] = "armour.chest",
        ["Helmets"] = "armour.helmet",
        ["Gloves"] = "armour.gloves",
        ["Boots"] = "armour.boots",
        ["Shields"] = "armour.shield",
        ["Quivers"] = "armour.quiver",
        ["Jewels"] = "jewel.base",
        ["Abyss Jewels"] = "jewel.abyss",
        ["Claws"] = "weapon.claw",
        ["Daggers"] = "weapon.basedagger",
        ["Rune Daggers"] = "weapon.runedagger",
        ["One Hand Swords"] = "weapon.basesword",
        ["Thrusting One Hand Swords"] = "weapon.rapier",
        ["One Hand Axes"] = "weapon.oneaxe",
        ["One Hand Maces"] = "weapon.basemace",
        ["Sceptres"] = "weapon.sceptre",
        ["Wands"] = "weapon.wand",
        ["Bows"] = "weapon.bow",
        ["Staves"] = "weapon.basestaff",
        ["Warstaves"] = "weapon.warstaff",
        ["Two Hand Swords"] = "weapon.twosword",
        ["Two Hand Axes"] = "weapon.twoaxe",
        ["Two Hand Maces"] = "weapon.twomace",
        ["Fishing Rods"] = "weapon.rod",
        ["Life Flasks"] = "flask",
        ["Mana Flasks"] = "flask",
        ["Hybrid Flasks"] = "flask",
        ["Utility Flasks"] = "flask",
        ["Maps"] = "map",

        // Korean (Kakao) client
        ["반지"] = "accessory.ring",
        ["목걸이"] = "accessory.amulet",
        ["허리띠"] = "accessory.belt",
        ["장신구"] = "accessory.trinket",
        ["갑옷"] = "armour.chest",
        ["투구"] = "armour.helmet",
        ["장갑"] = "armour.gloves",
        ["장화"] = "armour.boots",
        ["방패"] = "armour.shield",
        ["화살통"] = "armour.quiver",
        ["주얼"] = "jewel.base",
        ["심연 주얼"] = "jewel.abyss",
        ["클로"] = "weapon.claw",
        ["단검"] = "weapon.basedagger",
        ["룬 단검"] = "weapon.runedagger",
        ["한손 검"] = "weapon.basesword",
        ["레이피어"] = "weapon.rapier",
        ["찌르기 한손 검"] = "weapon.rapier",
        ["한손 도끼"] = "weapon.oneaxe",
        ["한손 철퇴"] = "weapon.basemace",
        ["셉터"] = "weapon.sceptre",
        ["마법봉"] = "weapon.wand",
        ["활"] = "weapon.bow",
        ["지팡이"] = "weapon.basestaff",
        ["전쟁지팡이"] = "weapon.warstaff",
        ["양손검"] = "weapon.twosword",
        ["양손 검"] = "weapon.twosword",
        ["양손 도끼"] = "weapon.twoaxe",
        ["양손 철퇴"] = "weapon.twomace",
        ["낚싯대"] = "weapon.rod",
        ["생명력 플라스크"] = "flask",
        ["마나 플라스크"] = "flask",
        ["복합 플라스크"] = "flask",
        ["유틸리티 플라스크"] = "flask",
        ["플라스크"] = "flask",
        ["지도"] = "map",
    };

    public static string? FromItemClass(string? itemClass) =>
        itemClass is not null && Map.TryGetValue(itemClass.Trim(), out var category) ? category : null;

    /// <summary>Like <see cref="FromItemClass"/>, but cluster jewels (item class "Jewels") get their own category.</summary>
    public static string? For(Parsing.ParsedItem item) =>
        ModDefaults.IsClusterJewel(item) ? "jewel.cluster" : FromItemClass(item.ItemClass);
}
