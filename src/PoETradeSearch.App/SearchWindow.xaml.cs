using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App;

public partial class SearchWindow : Window
{
    private static readonly Dictionary<string, string> InfluenceNames = new()
    {
        ["shaper"] = "쉐이퍼",
        ["elder"] = "엘더",
        ["crusader"] = "크루세이더",
        ["hunter"] = "헌터",
        ["redeemer"] = "리디머",
        ["warlord"] = "워로드",
    };

    private readonly ParsedItem _item;
    private readonly TradeApiClient _api;
    private readonly CurrencyCatalog _currencies;
    private readonly string _league;
    private readonly ObservableCollection<ModFilterRow> _rows = [];
    private readonly ObservableCollection<ListingView> _results = [];
    private readonly Dictionary<string, ToggleButton> _influenceChips = [];

    private readonly GemCatalog.GemType? _transfiguredGem;
    private TradeSearchResult? _lastSearch;
    private CancellationTokenSource? _cts;

    public SearchWindow(ParsedItem item, IReadOnlyList<MatchedMod> matches, TradeApiClient api,
                        CurrencyCatalog currencies, GemCatalog gems, string league, string leagueText)
    {
        InitializeComponent();
        _item = item;
        _api = api;
        _currencies = currencies;
        _league = league;

        var defaults = TradeQueryOptions.DefaultsFor(item, gems);
        _transfiguredGem = defaults.TransfiguredGem;
        ShowHeader(item, $"{leagueText} · {(api.Server == TradeServer.Kakao ? "카카오" : "글로벌")}");
        ApplyDefaults(defaults);

        foreach (var m in matches)
            _rows.Add(ModFilterRow.Create(m, item));
        ModsList.ItemsSource = _rows;
        // Gems, currency and cards have no mods to choose from.
        ModsSection.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.ItemsSource = _results;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => _cts?.Cancel();
        Loaded += async (_, _) => await SearchAsync();
    }

    private void ShowHeader(ParsedItem item, string league)
    {
        var rarityBrush = Palette.Rarity(item.Rarity);
        ItemNameText.Text = item.Name ?? item.BaseType ?? "알 수 없는 아이템";
        ItemNameText.Foreground = rarityBrush;

        RarityBadgeText.Text = item.Rarity switch
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
        RarityBadgeText.Foreground = rarityBrush;
        RarityBadge.BorderBrush = rarityBrush;

        var info = new List<string>();
        if (item.Name is not null && item.BaseType is not null) info.Add(item.BaseType);
        if (item.GemLevel is { } gemLevel && item.Rarity == ItemRarity.Gem) info.Add($"젬 레벨 {gemLevel}");
        if (item.Quality is { } q) info.Add($"퀄리티 {q}%");
        if (item.ItemLevel is { } ilvl) info.Add($"아이템 레벨 {ilvl}");
        if (item.Sockets is not null) info.Add($"소켓 {item.Sockets}");
        if (item.Corrupted) info.Add("타락");
        info.Add(league);
        ItemInfoText.Text = string.Join("   ·   ", info);

        UniqueHintText.Visibility = item.Rarity == ItemRarity.Unique ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows only the chips that apply to this item, pre-set from the defaults.</summary>
    private void ApplyDefaults(TradeQueryOptions defaults)
    {
        var item = defaults.Item;

        BaseTypeChip.Content = item.Rarity == ItemRarity.Magic
            ? "베이스 일치 (마법 아이템 미지원)"
            : $"베이스 일치: {item.BaseType}";
        BaseTypeChip.IsChecked = defaults.UseBaseType;
        // Uniques, gems and currency always search by name/base, so the chip would do nothing.
        BaseTypeChip.IsEnabled = item.Rarity is ItemRarity.Rare or ItemRarity.Normal;

        Show(FracturedChip, item.FracturedItem, defaults.FracturedItem);
        Show(SynthesisedChip, item.Synthesised, defaults.Synthesised);
        Show(SearingChip, item.SearingExarch, defaults.SearingExarch);
        Show(TangledChip, item.EaterOfWorlds, defaults.EaterOfWorlds);

        foreach (var influence in item.Influences)
        {
            var chip = new ToggleButton
            {
                Style = (Style)FindResource("Chip"),
                Content = InfluenceNames.GetValueOrDefault(influence, influence),
                IsChecked = defaults.Influences.Contains(influence),
            };
            _influenceChips[influence] = chip;
            InfluenceChips.Children.Add(chip);
        }

        SocketsPanel.Visibility = item.SocketCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        SocketsChip.IsChecked = defaults.SocketsMin is not null || defaults.SocketColours.Any;
        SocketCountText.Text = defaults.SocketsMin?.ToString() ?? "";
        SocketRedText.Text = defaults.SocketColours.Red?.ToString() ?? "";
        SocketGreenText.Text = defaults.SocketColours.Green?.ToString() ?? "";
        SocketBlueText.Text = defaults.SocketColours.Blue?.ToString() ?? "";
        SocketWhiteText.Text = defaults.SocketColours.White?.ToString() ?? "";
        LinksPanel.Visibility = item.MaxLinks > 0 ? Visibility.Visible : Visibility.Collapsed;
        LinksChip.IsChecked = defaults.LinksMin is not null;
        LinksText.Text = item.MaxLinks > 0 ? item.MaxLinks.ToString() : "";

        ItemLevelPanel.Visibility = item.ItemLevel is not null ? Visibility.Visible : Visibility.Collapsed;
        ItemLevelChip.IsChecked = defaults.ItemLevelMin is not null;
        // Cluster jewels start at their bracket (83 → 75); other items show their own item level.
        ItemLevelText.Text = (defaults.ItemLevelMin ?? item.ItemLevel)?.ToString() ?? "";

        // Gems: level and quality on by default (they decide the price). Equipment: quality shown, off.
        bool gem = item.Rarity == ItemRarity.Gem;
        GemLevelPanel.Visibility = gem ? Visibility.Visible : Visibility.Collapsed;
        GemLevelChip.IsChecked = defaults.GemLevelMin is not null;
        GemLevelMinText.Text = item.GemLevel?.ToString() ?? "";

        QualityPanel.Visibility = gem || item.Quality is not null ? Visibility.Visible : Visibility.Collapsed;
        QualityChip.IsChecked = defaults.QualityMin is not null;
        QualityMinText.Text = item.Quality?.ToString() ?? "";

        // Rows that don't apply to this item are hidden entirely (e.g. rings have no sockets).
        bool anyNumbers = item.ItemLevel is not null || gem || item.Quality is not null;
        NumbersCaption.Visibility = NumbersPanel.Visibility = anyNumbers ? Visibility.Visible : Visibility.Collapsed;
        bool sockets = item.SocketCount > 0;
        SocketsCaption.Visibility = SocketsRow.Visibility = sockets ? Visibility.Visible : Visibility.Collapsed;

        (defaults.Corrupted switch
        {
            true => CorruptedOnly,
            false => CorruptedNo,
            null => CorruptedAny,
        }).IsChecked = true;

        static void Show(ToggleButton chip, bool applies, bool isChecked)
        {
            chip.Visibility = applies ? Visibility.Visible : Visibility.Collapsed;
            chip.IsChecked = isChecked;
        }
    }

    /// <summary>Puts the window next to the mouse cursor, kept inside the screen's work area.</summary>
    public void PlaceNearCursor()
    {
        Native.NativeMethods.GetCursorPos(out var p);
        var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
        var cursor = new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(cursor.X + 24, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(cursor.Y - 120, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    private TradeQueryOptions BuildOptions()
    {
        var options = new TradeQueryOptions
        {
            Item = _item,
            UseBaseType = BaseTypeChip.IsChecked == true,
            Mode = ModeInstant.IsChecked == true ? TradeMode.InstantOnly
                 : ModeInPerson.IsChecked == true ? TradeMode.InPersonOnly
                 : ModeAny.IsChecked == true ? TradeMode.Any
                 : TradeMode.InstantAndInPerson,
            ItemLevelMin = ItemLevelChip.IsChecked == true && int.TryParse(ItemLevelText.Text.Trim(), out var ilvl) ? ilvl : null,
            LinksMin = LinksChip.IsChecked == true && int.TryParse(LinksText.Text.Trim(), out var links) ? links : null,
            FracturedItem = FracturedChip.IsChecked == true && FracturedChip.IsVisible,
            Synthesised = SynthesisedChip.IsChecked == true && SynthesisedChip.IsVisible,
            SearingExarch = SearingChip.IsChecked == true && SearingChip.IsVisible,
            EaterOfWorlds = TangledChip.IsChecked == true && TangledChip.IsVisible,
            Corrupted = CorruptedOnly.IsChecked == true ? true : CorruptedNo.IsChecked == true ? false : null,
            GemLevelMin = GemLevelChip.IsChecked == true && GemLevelPanel.IsVisible ? ParseInt(GemLevelMinText.Text) : null,
            GemLevelMax = GemLevelChip.IsChecked == true && GemLevelPanel.IsVisible ? ParseInt(GemLevelMaxText.Text) : null,
            QualityMin = QualityChip.IsChecked == true && QualityPanel.IsVisible ? ParseInt(QualityMinText.Text) : null,
            QualityMax = QualityChip.IsChecked == true && QualityPanel.IsVisible ? ParseInt(QualityMaxText.Text) : null,
            TransfiguredGem = _transfiguredGem,
            SocketsMin = SocketsChip.IsChecked == true && SocketsPanel.IsVisible ? ParseInt(SocketCountText.Text) : null,
            SocketColours = SocketsChip.IsChecked == true && SocketsPanel.IsVisible
                ? new SocketColours(ParseInt(SocketRedText.Text), ParseInt(SocketGreenText.Text), ParseInt(SocketBlueText.Text), ParseInt(SocketWhiteText.Text))
                : default,
        };
        options.Influences.AddRange(_influenceChips.Where(c => c.Value.IsChecked == true).Select(c => c.Key));
        foreach (var row in _rows)
            if (row.ToFilter() is { } f)
                options.Stats.Add(f);
        return options;
    }

    private async Task SearchAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SetBusy(true, "검색 중...");
        _results.Clear();
        MoreButton.Visibility = Visibility.Collapsed;
        OpenWebButton.IsEnabled = false;
        _lastSearch = null;

        try
        {
            var body = TradeQueryBuilder.Build(BuildOptions());
            Services.Log.Info($"검색 요청 ({_api.Server.Id}/{_league}): {body.ToJsonString()}");
            _lastSearch = await _api.SearchAsync(_league, body, ct);
            Services.Log.Info($"검색 결과: 총 {_lastSearch.Total}개, ID {_lastSearch.ResultIds.Count}개");
            OpenWebButton.IsEnabled = true;

            if (_lastSearch.ResultIds.Count == 0)
            {
                SetBusy(false, "검색 결과가 없습니다. " + EmptyResultHint());
                return;
            }
            await LoadMoreAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetBusy(false, ErrorText(ex));
        }
    }

    private async Task LoadMoreAsync(CancellationToken ct)
    {
        if (_lastSearch is null)
            return;

        SetBusy(true, "결과 불러오는 중...");
        var next = _lastSearch.ResultIds.Skip(_results.Count).Take(TradeApiClient.FetchBatchSize).ToList();
        try
        {
            var listings = await _api.FetchAsync(next, _lastSearch.QueryId, ct);
            foreach (var l in listings)
                _results.Add(new ListingView { Listing = l, Rank = _results.Count + 1, Currencies = _currencies });

            MoreButton.Visibility = _results.Count < _lastSearch.ResultIds.Count ? Visibility.Visible : Visibility.Collapsed;
            SetBusy(false, $"{_lastSearch.Total:N0}개 중 {_results.Count}개 · 가격 낮은 순");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetBusy(false, ErrorText(ex));
        }
    }

    private static int? ParseInt(string text) => int.TryParse(text.Trim(), out var v) ? v : null;

    /// <summary>Names the conditions that most often cause an empty result, so the user knows what to relax.</summary>
    private string EmptyResultHint()
    {
        var strict = new List<string>();
        if (CorruptedNo.IsChecked == true) strict.Add("타락 제외");
        if (CorruptedOnly.IsChecked == true) strict.Add("타락만");
        if (FracturedChip.IsVisible && FracturedChip.IsChecked == true) strict.Add("분열 아이템만");
        if (LinksPanel.IsVisible && LinksChip.IsChecked == true) strict.Add("링크");
        if (GemLevelPanel.IsVisible && GemLevelChip.IsChecked == true) strict.Add("젬 레벨");
        if (QualityPanel.IsVisible && QualityChip.IsChecked == true) strict.Add("퀄리티");
        if (_influenceChips.Values.Any(c => c.IsChecked == true)) strict.Add("영향력");
        if (ModeInstant.IsChecked == true) strict.Add("즉시 구매만");
        if (ModeInPerson.IsChecked == true) strict.Add("직접 거래만");
        int mods = _rows.Count(r => r.Enabled);
        if (mods > 0) strict.Add($"옵션 {mods}개");
        return strict.Count > 0
            ? $"켜져 있는 조건({string.Join(", ", strict)}) 중 일부를 끄거나 최소값을 낮춰 보세요."
            : "거래 방식을 '오프라인 포함'으로 바꿔 보세요.";
    }

    private void SetBusy(bool busy, string status)
    {
        SearchButton.IsEnabled = !busy;
        MoreButton.IsEnabled = !busy;
        SearchStatusText.Text = status;
    }

    private static string ErrorText(Exception ex)
    {
        Services.Log.Error("거래소 검색 오류", ex);
        return ErrorMessage(ex);
    }

    private static string ErrorMessage(Exception ex) => ex switch
    {
        RateLimitedException r => $"요청 제한에 걸리지 않도록 {Math.Ceiling(r.Wait.TotalSeconds)}초 쉬는 중입니다. 잠시 후 다시 검색하세요.",
        TradeApiException { StatusCode: 429 } => "거래소 요청 제한에 걸렸습니다. 잠시 후 다시 시도하세요.",
        TradeApiException api => $"거래소 오류 ({api.StatusCode}): {api.ApiMessage}",
        System.Net.Http.HttpRequestException or TaskCanceledException => "거래소에 연결하지 못했습니다: " + ex.Message,
        _ => "오류: " + ex.Message,
    };

    /// <summary>
    /// Turning "분열 아이템만" off also turns every row's "분열" toggle off (normal items wanted);
    /// each row can still be switched back on by itself.
    /// </summary>
    private void FracturedChip_Changed(object sender, RoutedEventArgs e)
    {
        if (FracturedChip.IsChecked == true)
            return;
        foreach (var row in _rows)
            row.SearchAsFractured = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
            await LoadMoreAsync(_cts.Token);
    }

    private void OpenWebButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSearch is null)
            return;
        Process.Start(new ProcessStartInfo(_api.WebSearchUrl(_league, _lastSearch.QueryId)) { UseShellExecute = true });
    }

}
