using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoETradeSearch.Core.Data;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App.ViewModels;

/// <summary>Three-way choice shown as segmented buttons: 상관없음 / 예 / 아니오.</summary>
public enum TriState { Any, Yes, No }

/// <summary>
/// Everything the search window shows and edits: the item's conditions (filled from
/// <see cref="TradeQueryOptions.DefaultsFor"/>), the mod rows and the results. <see cref="BuildOptions"/> turns
/// the current state back into a trade query; the window only binds to it.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly ITradeSearchApi _api;
    private readonly CurrencyCatalog _currencies;
    private readonly string _league;
    private readonly GemCatalog.GemType? _transfiguredGem;
    private TradeSearchResult? _lastSearch;
    private CancellationTokenSource? _cts;

    public SearchViewModel(ParsedItem item, IReadOnlyList<MatchedMod> matches, ITradeSearchApi api,
                           CurrencyCatalog currencies, GemCatalog? gems, string league, string leagueText)
    {
        Item = item;
        _api = api;
        _currencies = currencies;
        _league = league;

        var defaults = TradeQueryOptions.DefaultsFor(item, gems);
        _transfiguredGem = defaults.TransfiguredGem;

        // Header
        ItemName = (item.Foulborn ? "삿된 " : "") + (item.Name ?? item.DisplayBaseType ?? "알 수 없는 아이템");
        RarityBrush = Palette.Rarity(item.Rarity);
        RarityText = RarityName(item.Rarity);
        ItemInfo = BuildItemInfo(item, $"{leagueText} · {(api.Server == TradeServer.Kakao ? "카카오" : "글로벌")}");
        NameTooltip = item.Rarity == ItemRarity.Unique
            ? "고유 아이템은 이름과 베이스로 검색합니다. 옵션을 체크하면 그 수치 이상으로 굴린 것만 찾습니다."
            : null;

        // 아이템 row
        BaseTypeLabel = item.Rarity == ItemRarity.Magic ? "베이스 일치 (마법 아이템 미지원)" : $"베이스 일치: {item.BaseType}";
        UseBaseType = defaults.UseBaseType;
        // Uniques, gems and currency always search by name/base, so the chip would do nothing.
        CanChangeBaseType = item.Rarity is ItemRarity.Rare or ItemRarity.Normal;
        foreach (var flag in GameData.Current.SearchableFlags.Where(f => item.HasFlag(f.Id)))
            AddChip(new ChipViewModel
            {
                Kind = ChipViewModel.ChipKind.Flag, Id = flag.Id, Label = flag.Label ?? flag.Id, Tooltip = flag.Tooltip,
                IsChecked = defaults.RequiredFlags.Contains(flag.Id),
            });
        foreach (var influence in item.Influences)
            AddChip(new ChipViewModel
            {
                Kind = ChipViewModel.ChipKind.Influence, Id = influence, Label = GameData.Current.InfluenceLabel(influence),
                IsChecked = defaults.Influences.Contains(influence),
            });
        if (defaults.Foulborn is not null)
            // Foulborn copy → "삿된 아이템만"; normal unique → "삿된 아이템 제외" (a Foulborn copy lacks one of its mods).
            AddChip(new ChipViewModel
            {
                Kind = ChipViewModel.ChipKind.Foulborn, Id = "foulborn",
                Label = item.Foulborn ? "삿된 아이템만" : "삿된 아이템 제외",
                Tooltip = item.Foulborn
                    ? "끄면 일반 버전도 같이 찾습니다"
                    : "삿된 버전은 옵션 하나가 다른 옵션으로 바뀌어 있어 따로 뺍니다. 끄면 같이 찾습니다",
                IsChecked = true,
            });

        // 거래 / 타락
        Mode = defaults.Mode;
        Corrupted = defaults.Corrupted switch { true => TriState.Yes, false => TriState.No, null => TriState.Any };

        // 수치: item level (cluster jewels start at their bracket), gem level and quality
        bool gem = item.Rarity == ItemRarity.Gem;
        ShowItemLevel = item.ItemLevel is not null;
        ItemLevelOn = defaults.ItemLevelMin is not null;
        ItemLevelText = (defaults.ItemLevelMin ?? item.ItemLevel)?.ToString() ?? "";
        ShowGemLevel = gem;
        GemLevelOn = defaults.GemLevelMin is not null;
        GemLevelMinText = item.GemLevel?.ToString() ?? "";
        ShowQuality = gem || item.Quality is not null;
        QualityOn = defaults.QualityMin is not null;
        QualityMinText = item.Quality?.ToString() ?? "";

        // 홈
        ShowSockets = item.SocketCount > 0;
        SocketsOn = defaults.SocketsMin is not null || defaults.SocketColours.Any;
        SocketCountText = defaults.SocketsMin?.ToString() ?? "";
        SocketRedText = defaults.SocketColours.Red?.ToString() ?? "";
        SocketGreenText = defaults.SocketColours.Green?.ToString() ?? "";
        SocketBlueText = defaults.SocketColours.Blue?.ToString() ?? "";
        SocketWhiteText = defaults.SocketColours.White?.ToString() ?? "";
        ShowLinks = item.MaxLinks > 0;
        LinksOn = defaults.LinksMin is not null;
        LinksText = item.MaxLinks > 0 ? item.MaxLinks.ToString() : "";

        // 옵션
        foreach (var m in matches)
        {
            var row = ModFilterRow.Create(m, item);
            row.StartsNewGroup = Mods.Count > 0 && Mods[^1].IsFixedMod && !row.IsFixedMod;
            Mods.Add(row);
        }
    }

    public ParsedItem Item { get; }

    // ---------------- Header ----------------

    public string ItemName { get; }
    public string RarityText { get; }
    public Brush RarityBrush { get; }
    public string ItemInfo { get; }
    public string? NameTooltip { get; }

    // ---------------- Conditions ----------------

    public string BaseTypeLabel { get; }
    public bool CanChangeBaseType { get; }
    [ObservableProperty] private bool _useBaseType;

    /// <summary>Item flag, influence and Foulborn chips that apply to this item.</summary>
    public ObservableCollection<ChipViewModel> Chips { get; } = [];

    [ObservableProperty] private TradeMode _mode;
    [ObservableProperty] private TriState _corrupted;

    public bool ShowItemLevel { get; }
    public bool ShowGemLevel { get; }
    public bool ShowQuality { get; }
    public bool ShowNumbers => ShowItemLevel || ShowGemLevel || ShowQuality;
    [ObservableProperty] private bool _itemLevelOn;
    [ObservableProperty] private string _itemLevelText = "";
    [ObservableProperty] private bool _gemLevelOn;
    [ObservableProperty] private string _gemLevelMinText = "";
    [ObservableProperty] private string _gemLevelMaxText = "";
    [ObservableProperty] private bool _qualityOn;
    [ObservableProperty] private string _qualityMinText = "";
    [ObservableProperty] private string _qualityMaxText = "";

    public bool ShowSockets { get; }
    public bool ShowLinks { get; }
    public bool ShowSocketRow => ShowSockets || ShowLinks;
    [ObservableProperty] private bool _socketsOn;
    [ObservableProperty] private string _socketCountText = "";
    [ObservableProperty] private string _socketRedText = "";
    [ObservableProperty] private string _socketGreenText = "";
    [ObservableProperty] private string _socketBlueText = "";
    [ObservableProperty] private string _socketWhiteText = "";
    [ObservableProperty] private bool _linksOn;
    [ObservableProperty] private string _linksText = "";

    public ObservableCollection<ModFilterRow> Mods { get; } = [];
    public bool HasMods => Mods.Count > 0;

    // ---------------- Results ----------------

    public ObservableCollection<ListingView> Results { get; } = [];

    /// <summary>false = cheapest first, true = newest first. Changing it searches again.</summary>
    [ObservableProperty] private bool _newestFirst;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(LoadMoreCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))] private bool _hasMore;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(OpenWebCommand))] private bool _hasSearch;

    /// <summary>Opens a URL in the browser; replaced in tests.</summary>
    public Action<string> OpenUrl { get; set; } = url =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    private void AddChip(ChipViewModel chip)
    {
        chip.PropertyChanged += Chip_PropertyChanged;
        Chips.Add(chip);
    }

    /// <summary>
    /// Turning "분열된 아이템만" off also turns every fractured row's "분열" toggle off (normal items wanted);
    /// each row can still be switched back on by itself.
    /// </summary>
    private void Chip_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ChipViewModel { Kind: ChipViewModel.ChipKind.Flag, Id: FlagIds.Fractured, IsChecked: false }
            && e.PropertyName == nameof(ChipViewModel.IsChecked))
            foreach (var row in Mods.Where(r => r.IsFracturedMod))
                row.SearchAsFractured = false;
    }

    async partial void OnNewestFirstChanged(bool value)
    {
        if (_lastSearch is not null)
            await SearchAsync();
    }

    // ---------------- Query ----------------

    /// <summary>The trade query for the current state of the window.</summary>
    public TradeQueryOptions BuildOptions()
    {
        var options = new TradeQueryOptions
        {
            Item = Item,
            UseBaseType = UseBaseType,
            Mode = Mode,
            Corrupted = Corrupted switch { TriState.Yes => true, TriState.No => false, _ => null },
            ItemLevelMin = ShowItemLevel && ItemLevelOn ? ParseInt(ItemLevelText) : null,
            GemLevelMin = ShowGemLevel && GemLevelOn ? ParseInt(GemLevelMinText) : null,
            GemLevelMax = ShowGemLevel && GemLevelOn ? ParseInt(GemLevelMaxText) : null,
            QualityMin = ShowQuality && QualityOn ? ParseInt(QualityMinText) : null,
            QualityMax = ShowQuality && QualityOn ? ParseInt(QualityMaxText) : null,
            LinksMin = ShowLinks && LinksOn ? ParseInt(LinksText) : null,
            SocketsMin = ShowSockets && SocketsOn ? ParseInt(SocketCountText) : null,
            SocketColours = ShowSockets && SocketsOn
                ? new SocketColours(ParseInt(SocketRedText), ParseInt(SocketGreenText), ParseInt(SocketBlueText), ParseInt(SocketWhiteText))
                : default,
            TransfiguredGem = _transfiguredGem,
            NewestFirst = NewestFirst,
        };
        foreach (var chip in Chips.Where(c => c.IsChecked))
        {
            switch (chip.Kind)
            {
                case ChipViewModel.ChipKind.Flag: options.RequiredFlags.Add(chip.Id); break;
                case ChipViewModel.ChipKind.Influence: options.Influences.Add(chip.Id); break;
                case ChipViewModel.ChipKind.Foulborn: options.Foulborn = Item.Foulborn; break;
            }
        }
        foreach (var row in Mods)
            if (row.ToFilter() is { } f)
                options.Stats.Add(f);
        return options;
    }

    // ---------------- Commands ----------------

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    public async Task SearchAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        Status = "검색 중...";
        Results.Clear();
        HasMore = false;
        HasSearch = false;
        _lastSearch = null;

        try
        {
            var body = TradeQueryBuilder.Build(BuildOptions());
            Services.Log.Info($"검색 요청 ({_api.Server.Id}/{_league}): {body.ToJsonString()}");
            _lastSearch = await _api.SearchAsync(_league, body, ct);
            HasSearch = true;

            if (_lastSearch.ResultIds.Count == 0)
            {
                Status = "검색 결과가 없습니다. " + EmptyResultHint();
                return;
            }
            await FetchNextAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Status = ErrorText(ex);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsBusy = false;
        }
    }

    private bool CanLoadMore() => !IsBusy && HasMore;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        if (_cts is null)
            return;
        IsBusy = true;
        try
        {
            await FetchNextAsync(_cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Status = ErrorText(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FetchNextAsync(CancellationToken ct)
    {
        if (_lastSearch is null)
            return;
        Status = "결과 불러오는 중...";
        var next = _lastSearch.ResultIds.Skip(Results.Count).Take(TradeApiClient.FetchBatchSize).ToList();
        var listings = await _api.FetchAsync(next, _lastSearch.QueryId, ct);
        foreach (var l in listings)
            Results.Add(new ListingView { Listing = l, Rank = Results.Count + 1, Currencies = _currencies });
        HasMore = Results.Count < _lastSearch.ResultIds.Count;
        Status = $"{_lastSearch.Total:N0}개 중 {Results.Count}개";
    }

    private bool CanOpenWeb() => HasSearch;

    [RelayCommand(CanExecute = nameof(CanOpenWeb))]
    private void OpenWeb()
    {
        if (_lastSearch is not null)
            OpenUrl(_api.WebSearchUrl(_league, _lastSearch.QueryId));
    }

    public void Cancel() => _cts?.Cancel();

    // ---------------- Helpers ----------------

    /// <summary>Names the conditions that most often cause an empty result, so the user knows what to relax.</summary>
    public string EmptyResultHint()
    {
        var strict = new List<string>();
        if (Corrupted == TriState.No) strict.Add("타락 제외");
        if (Corrupted == TriState.Yes) strict.Add("타락만");
        strict.AddRange(Chips.Where(c => c.IsChecked && c.Kind != ChipViewModel.ChipKind.Influence).Select(c => c.Label));
        if (Chips.Any(c => c.IsChecked && c.Kind == ChipViewModel.ChipKind.Influence)) strict.Add("영향력");
        if (ShowLinks && LinksOn) strict.Add("링크");
        if (ShowSockets && SocketsOn) strict.Add("홈");
        if (ShowGemLevel && GemLevelOn) strict.Add("젬 레벨");
        if (ShowQuality && QualityOn) strict.Add("퀄리티");
        if (Mode == TradeMode.InstantOnly) strict.Add("즉시 구매만");
        if (Mode == TradeMode.InPersonOnly) strict.Add("직접 거래만");
        int mods = Mods.Count(r => r.Enabled);
        if (mods > 0) strict.Add($"옵션 {mods}개");
        return strict.Count > 0
            ? $"켜져 있는 조건({string.Join(", ", strict)}) 중 일부를 끄거나 최소값을 낮춰 보세요."
            : "거래 방식을 '오프라인 포함'으로 바꿔 보세요.";
    }

    private static int? ParseInt(string? text) => int.TryParse(text?.Trim(), out var v) ? v : null;

    private static string RarityName(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Unique => "고유",
        ItemRarity.Rare => "희귀",
        ItemRarity.Magic => "마법",
        ItemRarity.Normal => "일반",
        ItemRarity.Gem => "젬",
        ItemRarity.Currency => "화폐",
        ItemRarity.DivinationCard => "점술 카드",
        _ => "알 수 없음",
    };

    private static string BuildItemInfo(ParsedItem item, string league)
    {
        var info = new List<string>();
        if (item.Name is not null && item.BaseType is not null) info.Add(item.BaseType);
        if (item.GemLevel is { } gemLevel && item.Rarity == ItemRarity.Gem) info.Add($"젬 레벨 {gemLevel}");
        if (item.Quality is { } q) info.Add($"퀄리티 {q}%");
        if (item.ItemLevel is { } ilvl) info.Add($"아이템 레벨 {ilvl}");
        if (item.Sockets is not null) info.Add($"소켓 {item.Sockets}");
        if (item.Corrupted) info.Add("타락");
        info.Add(league);
        return string.Join("   ·   ", info);
    }

    private static string ErrorText(Exception ex)
    {
        Services.Log.Error("거래소 검색 오류", ex);
        return ex switch
        {
            RateLimitedException r => $"요청 제한에 걸리지 않도록 {Math.Ceiling(r.Wait.TotalSeconds)}초 쉬는 중입니다. 잠시 후 다시 검색하세요.",
            TradeApiException { StatusCode: 429 } => "거래소 요청 제한에 걸렸습니다. 잠시 후 다시 시도하세요.",
            TradeApiException api => $"거래소 오류 ({api.StatusCode}): {api.ApiMessage}",
            System.Net.Http.HttpRequestException or TaskCanceledException => "거래소에 연결하지 못했습니다: " + ex.Message,
            _ => "오류: " + ex.Message,
        };
    }
}
