using PoETradeSearch.Core.Data;

namespace PoETradeSearch.Core.Trade;

/// <summary>
/// Maps the "Item Class:" / "아이템 종류:" line to the trade site's category filter (see /api/trade/data/filters).
/// The table is game-data.json "itemCategories" (Korean names follow the Kakao trade site's category list).
/// </summary>
public static class ItemCategories
{
    public static string? FromItemClass(string? itemClass)
    {
        if (itemClass is null)
            return null;
        var key = itemClass.Trim();
        return GameData.Current.ItemCategories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>Like <see cref="FromItemClass"/>, but cluster jewels (item class "Jewels") get their own category.</summary>
    public static string? For(Parsing.ParsedItem item) =>
        ModDefaults.IsClusterJewel(item) ? "jewel.cluster" : FromItemClass(item.ItemClass);
}
