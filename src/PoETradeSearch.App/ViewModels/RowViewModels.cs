using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PoETradeSearch.Core.Data;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App.ViewModels;

/// <summary>One mod row in the search window: checkbox, text and min/max boxes.</summary>
public sealed partial class ModFilterRow : ObservableObject
{
    public required MatchedMod Match { get; init; }

    public bool IsMatched => Match.IsMatched;
    public string Text => Match.DisplayText;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _minText = "";
    [ObservableProperty] private string _maxText = "";

    /// <summary>
    /// For a fractured mod: on = only listings where this mod is fractured; off = the same mod as a normal
    /// explicit, so items where it isn't fractured are found too.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FractureToggleText), nameof(Hint), nameof(HasHint))]
    private bool _searchAsFractured = true;

    /// <summary>Fractured mods get their own on/off toggle in the row instead of a fixed tag.</summary>
    public bool IsFracturedMod => Match.Source[0].Type == ModType.Fractured;

    public string FractureToggleText => SearchAsFractured ? "분열" : "분열 X";

    /// <summary>The plain tag is shown for every mod type except fractured, which has the toggle.</summary>
    public bool ShowTag => HasTag && !IsFracturedMod;

    public string Tag => Match.Source[0].Type switch
    {
        ModType.Implicit when Match.Source[0].FromCorruption => "타락 고정",
        ModType.Implicit => "고정",
        ModType.Crafted => "제작",
        ModType.Fractured => "분열",
        ModType.Enchant => "인챈트",
        ModType.Scourge => "스컬지",
        ModType.Crucible => "도가니",
        _ => "",
    };

    public bool HasTag => Tag.Length > 0;

    /// <summary>Fixed mods (enchants, implicits) come first; the item's own mods are a separate group.</summary>
    public bool IsFixedMod => Match.Source[0].Type is ModType.Implicit or ModType.Enchant;

    /// <summary>Draw a line above this row because the group changes here (set by the search view model).</summary>
    public bool StartsNewGroup { get; set; }

    public Brush TextColor => Match.Source[0].Type switch
    {
        ModType.Crafted or ModType.Enchant => Palette.ModCrafted,
        ModType.Fractured => Palette.ModFractured,
        _ => Palette.ModBlue,
    };

    /// <summary>Only stats with a "#" can take a min/max.</summary>
    public bool HasValue => Match.SearchValue is not null;

    /// <summary>Tier range, "Adds # to #" averaging and the fractured toggle, explained under the mod.</summary>
    public string? Hint
    {
        get
        {
            if (!Match.IsMatched)
                return "거래소에서 찾을 수 없는 옵션이라 검색에 쓸 수 없습니다";
            var hints = new List<string>();
            if (Match.SearchRange is { } range && !ModDefaults.UseExactValue(Match))
                hints.Add($"현재 {Format(Match.SearchValue ?? 0)} · 이 티어 범위 {Format(range.Min)}~{Format(range.Max)}");
            if (Match.Values.Count > 1)
                hints.Add($"{string.Join(" ~ ", Match.Values.Select(Format))}의 평균값 {Format(Match.SearchValue!.Value)}(으)로 검색합니다");
            if (IsFracturedMod)
                hints.Add(SearchAsFractured
                    ? "이 옵션이 분열된 것만 찾습니다 (왼쪽 '분열'을 누르면 일반 옵션으로도 찾음)"
                    : "분열 여부와 상관없이 이 옵션이 붙은 것을 찾습니다");
            return hints.Count > 0 ? string.Join(" · ", hints) : null;
        }
    }

    public bool HasHint => Hint is not null;

    public static ModFilterRow Create(MatchedMod match, ParsedItem item)
    {
        var value = match.SearchValue is { } v ? Format(v) : "";
        string min = value, max = "";
        if (ModDefaults.UseExactValue(match))
        {
            // Variant counts (cluster passives, Voices sockets) are searched exactly.
            max = value;
        }
        else if (match.SearchRange is { } range)
        {
            // Advanced copy: the whole range this tier can roll, e.g. 85~109 for "97(85-109)%".
            min = Format(range.Min);
            max = Format(range.Max);
        }
        // Otherwise min = the item's own roll, so the first search finds items at least as good.

        return new ModFilterRow
        {
            Match = match,
            Enabled = ModDefaults.EnabledByDefault(item, match),
            MinText = min,
            MaxText = max,
        };
    }

    public StatFilter? ToFilter()
    {
        if (Match.Stat is null)
            return null;
        return new StatFilter
        {
            StatId = IsFracturedMod && !SearchAsFractured ? TradeQueryBuilder.AsExplicit(Match.Stat.Id) : Match.Stat.Id,
            Enabled = Enabled,
            Min = ParseNumber(MinText),
            Max = ParseNumber(MaxText),
        };
    }

    public static string Format(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    public static double? ParseNumber(string? text)
    {
        if (text is null)
            return null;
        text = text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

/// <summary>
/// A toggle chip in the "아이템" row: an item flag from game-data.json (분열, 결합, 작열의 총주교, ...),
/// an influence, or the Foulborn filter.
/// </summary>
public sealed partial class ChipViewModel : ObservableObject
{
    public enum ChipKind { Flag, Influence, Foulborn }

    public required ChipKind Kind { get; init; }

    /// <summary>Flag or influence id ("fractured", "shaper"); unused for Foulborn.</summary>
    public required string Id { get; init; }

    public required string Label { get; init; }
    public string? Tooltip { get; init; }

    [ObservableProperty] private bool _isChecked;
}

/// <summary>A search result as shown in the list.</summary>
public sealed class ListingView
{
    public required TradeListing Listing { get; init; }
    public required int Rank { get; init; }
    public required CurrencyCatalog Currencies { get; init; }

    public string PriceAmount => Listing.PriceAmount is { } a ? ModFilterRow.Format(a) : "—";

    // Short community names first ("카오스", game-data.json "currencyNames"), then the server's own name.
    public string PriceCurrency => Listing.PriceCurrency is not { } c
        ? "가격 없음"
        : GameData.Current.CurrencyNames.FirstOrDefault(n => string.Equals(n.Key, c, StringComparison.OrdinalIgnoreCase)).Value
          ?? Currencies.Find(c)?.Name ?? c;

    public string? PriceIcon => Currencies.Find(Listing.PriceCurrency)?.ImageUrl;

    public string Title => string.Join(" ", new[] { Listing.Name, Listing.TypeLine }.Where(s => !string.IsNullOrEmpty(s)));

    public string Details
    {
        get
        {
            var parts = new List<string>();
            if (Listing.GemLevel is { } gemLevel && Listing.ItemLevel is null or 0) parts.Add($"젬 레벨 {gemLevel}");
            if (Listing.Quality is { } quality) parts.Add($"퀄리티 {quality}%");
            if (Listing.ItemLevel is { } ilvl and > 0) parts.Add($"아이템 레벨 {ilvl}");
            if (Listing.Corrupted) parts.Add("타락");
            return string.Join("  ·  ", parts);
        }
    }

    public bool IsCorrupted => Listing.Corrupted;

    /// <summary>Gems: "20레벨 · 퀄 20%" (and "타락") in the result line itself.</summary>
    public string GemInfo
    {
        get
        {
            if (Listing.GemLevel is null || Listing.ItemLevel is > 0)
                return "";
            var parts = new List<string> { $"{Listing.GemLevel}레벨", $"퀄 {Listing.Quality ?? 0}%" };
            if (Listing.Corrupted) parts.Add("타락");
            return string.Join(" · ", parts);
        }
    }

    public Brush OnlineColor => Listing.SellerOnline ? Palette.Online : Palette.TextFaint;
    public string ListedAgo => Listing.Indexed is { } t ? Ago(DateTimeOffset.UtcNow - t) : "";

    public IReadOnlyList<ModLineView> TopMods => Listing.Mods
        .Where(m => m.Kind is ListingModKind.Enchant or ListingModKind.Implicit)
        .Select(ModLineView.From).ToList();

    public IReadOnlyList<ModLineView> MainMods => Listing.Mods
        .Where(m => m.Kind is not (ListingModKind.Enchant or ListingModKind.Implicit))
        .Select(ModLineView.From).ToList();

    public bool HasTopMods => TopMods.Count > 0;

    private static string Ago(TimeSpan span) => span.TotalMinutes switch
    {
        < 1 => "방금 등록",
        < 60 => $"{(int)span.TotalMinutes}분 전",
        < 60 * 24 => $"{(int)span.TotalHours}시간 전",
        _ => $"{(int)span.TotalDays}일 전",
    };
}

public sealed record ModLineView(string Text, Brush Color, string? Tag)
{
    public bool HasTag => Tag is not null;

    public static ModLineView From(ListingMod m) => m.Kind switch
    {
        ListingModKind.Implicit => new(m.Text, Palette.ModBlue, null),
        ListingModKind.Enchant => new(m.Text, Palette.ModCrafted, "인챈트"),
        ListingModKind.Crafted => new(m.Text, Palette.ModCrafted, "제작"),
        ListingModKind.Fractured => new(m.Text, Palette.ModFractured, "분열"),
        _ => new(m.Text, Palette.ModBlue, null),
    };
}

/// <summary>Brushes used from code; they match Theme.xaml.</summary>
public static class Palette
{
    public static readonly Brush ModBlue = Frozen("#8888FF");
    public static readonly Brush ModCrafted = Frozen("#B4B4FF");
    public static readonly Brush ModFractured = Frozen("#A29162");
    public static readonly Brush Online = Frozen("#5FBF6A");
    public static readonly Brush TextFaint = Frozen("#5C5546");
    public static readonly Brush Danger = Frozen("#D9534F");

    public static Brush Rarity(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Unique => Frozen("#D7792F"),
        ItemRarity.Rare => Frozen("#FFFF77"),
        ItemRarity.Magic => Frozen("#8888FF"),
        ItemRarity.Gem => Frozen("#1BA29B"),
        ItemRarity.Currency => Frozen("#AA9E82"),
        ItemRarity.DivinationCard => Frozen("#0EBAFF"),
        _ => Frozen("#C8C8C8"),
    };

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
