using System.Globalization;
using System.Text.Json;

namespace PoETradeSearch.Core.Trade;

public enum ListingModKind { Enchant, Implicit, Fractured, Explicit, Crafted }

public sealed record ListingMod(string Text, ListingModKind Kind);

/// <summary>One result from GET /api/trade/fetch/{ids}.</summary>
public sealed class TradeListing
{
    public string Id { get; init; } = "";
    public double? PriceAmount { get; init; }
    public string? PriceCurrency { get; init; }
    public string? AccountName { get; init; }
    public string? CharacterName { get; init; }
    public bool SellerOnline { get; init; }
    public DateTimeOffset? Indexed { get; init; }
    public string? Whisper { get; init; }

    public string? Name { get; init; }
    public string? TypeLine { get; init; }
    public int? ItemLevel { get; init; }
    public bool Corrupted { get; init; }
    public List<ListingMod> Mods { get; } = [];

    /// <summary>Gem level and quality from the listing's properties ("Level" / "레벨", "Quality" / "퀄리티").</summary>
    public int? GemLevel { get; private set; }
    public int? Quality { get; private set; }

    private static readonly HashSet<string> LevelNames = ["Level", "레벨"];
    private static readonly HashSet<string> QualityNames = ["Quality", "퀄리티"];

    private void ReadProperties(JsonElement item)
    {
        if (!item.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Array)
            return;
        foreach (var p in props.EnumerateArray())
        {
            var name = Str(p, "name");
            if (name is null || !p.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array ||
                values.GetArrayLength() == 0 || values[0].ValueKind != JsonValueKind.Array || values[0].GetArrayLength() == 0)
                continue;
            var raw = values[0][0].GetString() ?? "";
            var digits = new string(raw.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var number))
                continue;
            if (LevelNames.Contains(name)) GemLevel = number;
            else if (QualityNames.Contains(name)) Quality = number;
        }
    }

    /// <summary>Parses the "result" array of a fetch response. Items whose data is missing are skipped.</summary>
    public static List<TradeListing> ParseFetchResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<TradeListing>();
        if (!doc.RootElement.TryGetProperty("result", out var results))
            return list;

        foreach (var r in results.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object)
                continue;
            if (!r.TryGetProperty("listing", out var listing) || !r.TryGetProperty("item", out var item))
                continue;

            listing.TryGetProperty("price", out var price);
            listing.TryGetProperty("account", out var account);

            var result = new TradeListing
            {
                Id = Str(r, "id") ?? "",
                PriceAmount = price.ValueKind == JsonValueKind.Object && price.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number
                    ? a.GetDouble() : null,
                PriceCurrency = price.ValueKind == JsonValueKind.Object ? Str(price, "currency") : null,
                AccountName = account.ValueKind == JsonValueKind.Object ? Str(account, "name") : null,
                CharacterName = account.ValueKind == JsonValueKind.Object ? Str(account, "lastCharacterName") : null,
                SellerOnline = account.ValueKind == JsonValueKind.Object && account.TryGetProperty("online", out var online) && online.ValueKind == JsonValueKind.Object,
                Indexed = DateTimeOffset.TryParse(Str(listing, "indexed"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null,
                Whisper = Str(listing, "whisper"),
                Name = Str(item, "name"),
                TypeLine = Str(item, "typeLine"),
                ItemLevel = item.TryGetProperty("ilvl", out var ilvl) && ilvl.ValueKind == JsonValueKind.Number ? ilvl.GetInt32() : null,
                Corrupted = item.TryGetProperty("corrupted", out var c) && c.ValueKind == JsonValueKind.True,
            };

            result.ReadProperties(item);
            AddMods(result, item, "enchantMods", ListingModKind.Enchant);
            AddMods(result, item, "implicitMods", ListingModKind.Implicit);
            AddMods(result, item, "fracturedMods", ListingModKind.Fractured);
            AddMods(result, item, "explicitMods", ListingModKind.Explicit);
            AddMods(result, item, "craftedMods", ListingModKind.Crafted);
            list.Add(result);
        }
        return list;
    }

    /// <summary>
    /// Mods come either as plain strings or as objects with "description" (and "flags.crafted"/"fractured"),
    /// depending on the API version, so both are accepted.
    /// </summary>
    private static void AddMods(TradeListing listing, JsonElement item, string property, ListingModKind kind)
    {
        if (!item.TryGetProperty(property, out var mods) || mods.ValueKind != JsonValueKind.Array)
            return;

        foreach (var mod in mods.EnumerateArray())
        {
            if (mod.ValueKind == JsonValueKind.String)
            {
                listing.Mods.Add(new ListingMod(StripMarkup(mod.GetString()!), kind));
                continue;
            }
            if (mod.ValueKind != JsonValueKind.Object)
                continue;

            var text = Str(mod, "description");
            if (string.IsNullOrEmpty(text))
                continue;

            var actualKind = kind;
            if (mod.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Object)
            {
                if (flags.TryGetProperty("crafted", out var cr) && cr.ValueKind == JsonValueKind.True) actualKind = ListingModKind.Crafted;
                else if (flags.TryGetProperty("fractured", out var fr) && fr.ValueKind == JsonValueKind.True) actualKind = ListingModKind.Fractured;
            }
            listing.Mods.Add(new ListingMod(StripMarkup(text), actualKind));
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static readonly System.Text.RegularExpressions.Regex Markup =
        new(@"\[(?:[^\]|]*\|)?([^\]|]*)\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Mod texts can contain link markup: "[PlayerItemRarity|Item Rarity]" or "[Life]" → shown text only.</summary>
    public static string StripMarkup(string text) => Markup.Replace(text, "$1");
}
