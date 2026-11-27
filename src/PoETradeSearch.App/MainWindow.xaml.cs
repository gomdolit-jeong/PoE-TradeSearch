using System.Net.Http;
using System.Text;
using System.Windows;
using PoETradeSearch.App.Native;
using PoETradeSearch.App.Services;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App;

public partial class MainWindow : Window
{
    private const ushort VK_D = 0x44;

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Dictionary<string, ServerContext> _servers =
        TradeServer.All.ToDictionary(s => s.Id, s => new ServerContext(Http, s));

    private GlobalHotkey? _priceCheckHotkey;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CurrencyCatalog> _currencies = new();
    private SearchWindow? _searchWindow;
    private bool _busy;
    private bool _changingServer;

    public MainWindow()
    {
        InitializeComponent();
        Log.Info($"시작: 버전 {typeof(MainWindow).Assembly.GetName().Version}, OS {Environment.OSVersion}, 관리자 권한 {ItemClipboardReader.SelfElevated}");

        _changingServer = true;
        ServerCombo.ItemsSource = TradeServer.All;
        ServerCombo.SelectedItem = TradeServer.FromId(_settings.Server);
        _changingServer = false;

        SourceInitialized += (_, _) => RegisterHotkeys();
        Loaded += async (_, _) => await Task.WhenAll(LoadAllStatsAsync(), LoadLeaguesAsync(), LoadCurrenciesAsync());
        // Check whenever the user comes back to this window (e.g. after starting the game).
        Activated += (_, _) => CheckRunningGameElevation();
        Closed += (_, _) =>
        {
            _priceCheckHotkey?.Dispose();
            Application.Current.Shutdown();
        };
    }

    private TradeServer SelectedServer => ServerCombo.SelectedItem as TradeServer ?? TradeServer.Kakao;
    private ServerContext Current => _servers[SelectedServer.Id];
    private string? SelectedLeague => (LeagueCombo.SelectedItem as League)?.Id;

    /// <summary>
    /// Currency names and icons per server (Kakao has Korean names). A nice-to-have: without them prices
    /// still show as text.
    /// </summary>
    private async Task LoadCurrenciesAsync()
    {
        await Task.WhenAll(TradeServer.All.Select(async server =>
        {
            try
            {
                _currencies[server.Id] = CurrencyCatalog.Parse(await Http.GetStringAsync(server.StaticDataUrl));
            }
            catch (Exception ex)
            {
                Log.Warn($"{server.Id} 화폐 데이터 로드 실패: " + ex.Message);
            }
        }));
    }

    private CurrencyCatalog CurrenciesFor(TradeServer server) =>
        _currencies.GetValueOrDefault(server.Id) ?? CurrencyCatalog.Empty;

    private async Task LoadLeaguesAsync()
    {
        var server = SelectedServer;
        try
        {
            var leagues = await _servers[server.Id].Api.GetLeaguesAsync();
            if (server != SelectedServer)
                return; // the user switched servers meanwhile
            LeagueCombo.ItemsSource = leagues;
            // The API lists the current challenge league first. League ids are the same on both servers.
            LeagueCombo.SelectedItem = leagues.FirstOrDefault(l => l.Id == _settings.League) ?? leagues.FirstOrDefault();
            LeagueStatusText.Text = "";
            Log.Info($"{server.Id} 리그 목록: {string.Join(", ", leagues.Select(l => l.Id))} / 선택: {SelectedLeague}");
        }
        catch (Exception ex)
        {
            Log.Error("리그 목록 로드 실패", ex);
            LeagueStatusText.Text = "리그 목록을 받지 못했습니다: " + ex.Message;
        }
    }

    private void LeagueCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SelectedLeague is { } league && league != _settings.League)
        {
            _settings.League = league;
            _settings.Save();
        }
    }

    private async void ServerCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_changingServer)
            return;
        _settings.Server = SelectedServer.Id;
        _settings.Save();
        Log.Info("서버 변경: " + SelectedServer.Id);
        await LoadLeaguesAsync();
    }

    /// <summary>Selects the server that matches the copied item's language (Korean → Kakao).</summary>
    private async Task<bool> SwitchServerForAsync(ParsedItem item)
    {
        var wanted = TradeServer.ForLanguage(item.Language);
        if (wanted == SelectedServer)
            return false;
        Log.Info($"아이템 언어({item.Language})에 맞춰 서버 변경: {SelectedServer.Id} → {wanted.Id}");
        _changingServer = true;
        ServerCombo.SelectedItem = wanted;
        _changingServer = false;
        _settings.Server = wanted.Id;
        _settings.Save();
        await LoadLeaguesAsync();
        return true;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GGG asks tools to identify themselves; requests without a User-Agent are rejected.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoETradeSearch/0.4");
        return http;
    }

    private void RegisterHotkeys()
    {
        try
        {
            _priceCheckHotkey = new GlobalHotkey(this, NativeMethods.MOD_CONTROL, VK_D, "Ctrl+D");
            _priceCheckHotkey.Pressed += async (_, _) => await OnPriceCheckHotkeyAsync();
            StatusText.Text = $"대기 중 — 게임에서 아이템에 마우스를 올리고 {_priceCheckHotkey.Description}를 누르세요";
            HotkeyText.Text = _priceCheckHotkey.Description;
            Log.Info($"단축키 등록 성공: {_priceCheckHotkey.Description}");
            HintText.Text = "단축키를 누르면 아이템 정보를 복사해서 검색 창을 띄우고, 가격 낮은 순으로 검색합니다. " +
                            "검색 창에서 옵션과 최소/최대값을 바꾸고 Enter로 다시 검색할 수 있습니다. Esc로 닫습니다.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "단축키 등록 실패";
            HintText.Text = ex.Message;
            Log.Error("단축키 등록 실패", ex);
        }
    }

    /// <summary>Loads both servers' option data so either client language works right away.</summary>
    private async Task LoadAllStatsAsync()
    {
        var tasks = _servers.Values.Select(async ctx =>
        {
            try { await ctx.GetMatcherAsync(); } catch { /* logged in ServerContext */ }
            await ctx.GetGemsAsync();
        });
        await Task.WhenAll(tasks);

        bool allOk = _servers.Values.All(s => s.Matcher is not null);
        StatsText.Text = "거래소 옵션 데이터 · " + string.Join("  ·  ",
            _servers.Values.Select(s => $"{(s.Server == TradeServer.Kakao ? "카카오" : "글로벌")} {s.StatusText}"));
        StatsDot.Fill = allOk ? Palette.Online : (System.Windows.Media.Brush)FindResource("Danger");
    }

    private async Task OnPriceCheckHotkeyAsync()
    {
        var fg = ForegroundInfo.Current();
        Log.Info($"단축키 눌림: 앞에 있는 창 {fg}");

        if (_busy)
        {
            Log.Info("이전 처리 중이라 무시");
            return;
        }
        if (OnlyInGameCheck.IsChecked == true && !fg.IsGame)
        {
            StatusText.Text = $"앞에 있는 창이 PoE가 아니라서 무시했습니다: '{fg.Title}' ({fg.ProcessName ?? "?"})";
            Log.Warn("PoE 창이 아니라서 무시");
            return;
        }

        bool elevationMismatch = fg.Elevated == true && !ItemClipboardReader.SelfElevated;
        ShowElevationWarning(elevationMismatch);
        if (elevationMismatch)
            Log.Warn("게임이 관리자 권한으로 실행 중이고 이 프로그램은 아님 — Windows가 Ctrl+C 입력을 막을 수 있음");

        _busy = true;
        try
        {
            var text = await ItemClipboardReader.CopyItemUnderCursorAsync(VK_D, TimeSpan.FromMilliseconds(700));
            if (text is null)
            {
                StatusText.Text = elevationMismatch
                    ? "아이템을 복사하지 못했습니다. 게임이 관리자 권한으로 실행 중이라 이 프로그램도 관리자 권한으로 실행해야 합니다."
                    : "아이템을 복사하지 못했습니다. 마우스가 아이템 위에 있었나요? 계속 안 되면 '진단 정보 복사'를 눌러 보내주세요.";
                return;
            }
            ShowItem(text);
        }
        catch (Exception ex)
        {
            Log.Error("단축키 처리 중 오류", ex);
            StatusText.Text = "오류: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Warns early if PoE is already running as administrator while this app isn't.</summary>
    private void CheckRunningGameElevation()
    {
        if (ItemClipboardReader.SelfElevated)
        {
            ShowElevationWarning(false);
            return;
        }
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                using (p)
                {
                    if (!p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var elevated = NativeMethods.IsProcessElevated((uint)p.Id);
                    Log.Info($"실행 중인 게임 발견: {p.ProcessName} pid={p.Id} 관리자 권한={elevated?.ToString() ?? "?"}");
                    ShowElevationWarning(elevated == true);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("게임 프로세스 확인 실패: " + ex.Message);
        }
    }

    private void ShowElevationWarning(bool show)
    {
        ElevationPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RunAsAdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath!;
            Log.Info("관리자 권한으로 다시 실행: " + exe);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // The user pressed "No" on the UAC prompt, or the launch failed.
            Log.Warn("관리자 권한 실행 취소 또는 실패: " + ex.Message);
        }
    }

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PoE Trade Search 진단 정보 ===");
        sb.AppendLine($"버전: {typeof(MainWindow).Assembly.GetName().Version}  실행 파일: {Environment.ProcessPath}");
        sb.AppendLine($"OS: {Environment.OSVersion}  64비트: {Environment.Is64BitProcess}  관리자 권한: {ItemClipboardReader.SelfElevated}");
        sb.AppendLine($"단축키: {_priceCheckHotkey?.Description ?? "등록 실패"}  PoE 창에서만: {OnlyInGameCheck.IsChecked}");
        sb.AppendLine($"서버: {SelectedServer.Id}  리그: {SelectedLeague ?? "없음"}");
        foreach (var s in _servers.Values)
            sb.AppendLine($"옵션 데이터 {s.Server.Id}: {s.StatusText}");
        sb.AppendLine($"상태: {StatusText.Text}");
        sb.AppendLine();
        sb.AppendLine("=== 최근 로그 ===");
        sb.AppendLine(Log.RecentText());
        try
        {
            Clipboard.SetText(sb.ToString());
            StatusText.Text = "진단 정보를 클립보드에 복사했습니다. 그대로 붙여넣어 보내주세요.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "복사 실패: " + ex.Message;
        }
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Log.Directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.Directory) { UseShellExecute = true });
    }

    private void ParseClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        string? text = null;
        try { text = Clipboard.GetText(); } catch { }

        if (!ItemTextParser.LooksLikeItem(text))
        {
            StatusText.Text = "클립보드에 PoE 아이템 텍스트가 없습니다";
            return;
        }
        ShowItem(text!);
    }

    private void CopyOutputButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(OutputText.Text);
            StatusText.Text = "결과를 클립보드에 복사했습니다";
        }
        catch (Exception ex)
        {
            StatusText.Text = "복사 실패: " + ex.Message;
        }
    }

    private async void ShowItem(string text)
    {
        try
        {
            var item = ItemTextParser.Parse(text);
            bool switched = await SwitchServerForAsync(item);
            var ctx = Current;

            IReadOnlyList<MatchedMod>? matches = null;
            try
            {
                matches = (await ctx.GetMatcherAsync()).Match(item);
            }
            catch (Exception ex)
            {
                Log.Warn($"{ctx.Server.Id} 옵션 데이터가 없어 매칭하지 못함: {ex.Message}");
            }

            int matched = matches?.Count(m => m.IsMatched) ?? 0;
            Log.Info($"아이템 파싱 ({item.Language}, {ctx.Server.Id}): {item.Rarity} '{item.Name ?? item.BaseType}' " +
                     $"종류='{item.ItemClass}' 카테고리={ItemCategories.FromItemClass(item.ItemClass) ?? "없음"} " +
                     $"옵션 {matched}/{matches?.Count ?? item.Mods.Count}개 매칭");
            foreach (var m in matches?.Where(m => !m.IsMatched) ?? [])
                Log.Warn("매칭 실패 옵션: " + m.DisplayText);

            StatusText.Text = matches is null
                ? $"파싱 완료: {item.Name ?? item.BaseType} ({DateTime.Now:HH:mm:ss})"
                : $"파싱 완료: {item.Name ?? item.BaseType} — 옵션 {matched}/{matches.Count}개 매칭 ({DateTime.Now:HH:mm:ss})";
            if (switched)
                StatusText.Text += $" · 서버를 {ctx.Server.DisplayName}(으)로 바꿨습니다";
            OutputText.Text = Describe(item, matches);

            if (matches is null)
                StatusText.Text += " — 옵션 데이터가 없어 검색할 수 없습니다";
            else if (SelectedLeague is not { } league)
                StatusText.Text += " — 리그를 선택해야 검색할 수 있습니다";
            else
                OpenSearchWindow(item, matches, ctx, await ctx.GetGemsAsync(), league);
        }
        catch (Exception ex)
        {
            Log.Error("아이템 처리 실패. 원본:\n" + text, ex);
            StatusText.Text = "아이템을 읽지 못했습니다: " + ex.Message;
            OutputText.Text = ex + Environment.NewLine + Environment.NewLine + text;
        }
    }

    /// <summary>Only one search window at a time: a new hotkey press replaces the previous one.</summary>
    private void OpenSearchWindow(ParsedItem item, IReadOnlyList<MatchedMod> matches, ServerContext ctx, GemCatalog gems, string league)
    {
        _searchWindow?.Close();
        var leagueText = (LeagueCombo.SelectedItem as League)?.Text ?? league;
        _searchWindow = new SearchWindow(item, matches, ctx.Api, CurrenciesFor(ctx.Server), gems, league, leagueText) { Owner = null };
        _searchWindow.Closed += (s, _) => { if (ReferenceEquals(_searchWindow, s)) _searchWindow = null; };
        _searchWindow.PlaceNearCursor();
        _searchWindow.Show();
        _searchWindow.Activate();
    }

    private static string Describe(ParsedItem item, IReadOnlyList<MatchedMod>? matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Class     : {item.ItemClass}");
        sb.AppendLine($"Rarity    : {item.Rarity}");
        if (item.Name is not null) sb.AppendLine($"Name      : {item.Name}");
        sb.AppendLine($"Base      : {item.BaseType}");
        if (item.ItemLevel is not null) sb.AppendLine($"Item Lv   : {item.ItemLevel}");
        if (item.GemLevel is not null) sb.AppendLine($"Gem Lv    : {item.GemLevel}");
        if (item.Quality is not null) sb.AppendLine($"Quality   : {item.Quality}%");
        if (item.Sockets is not null) sb.AppendLine($"Sockets   : {item.Sockets}");
        if (item.Influences.Count > 0) sb.AppendLine($"Influence : {string.Join(", ", item.Influences)}");

        var flags = new List<string>();
        if (item.Corrupted) flags.Add("Corrupted");
        if (item.Unidentified) flags.Add("Unidentified");
        if (item.Mirrored) flags.Add("Mirrored");
        if (item.Synthesised) flags.Add("Synthesised");
        if (flags.Count > 0) sb.AppendLine($"Flags     : {string.Join(", ", flags)}");

        foreach (var (key, value) in item.Properties)
            sb.AppendLine($"  {key}: {value}");

        sb.AppendLine();
        if (matches is null)
        {
            sb.AppendLine("Mods (stat 데이터 없음, 매칭 안 함):");
            foreach (var mod in item.Mods)
                sb.AppendLine($"  [{mod.Type,-9}] {mod.Template,-50} {string.Join(", ", mod.Values)}");
        }
        else
        {
            sb.AppendLine("Mods → 거래소 stat:");
            foreach (var m in matches)
            {
                var type = m.Source[0].Type;
                if (m.Stat is null)
                {
                    sb.AppendLine($"  [{type,-9}] ✗ 매칭 실패: {m.DisplayText}");
                    continue;
                }
                sb.AppendLine($"  [{type,-9}] ✓ {m.Stat.Text.Replace("\n", " / ")}");
                sb.Append($"              {m.Stat.Id}");
                if (m.SearchValue is not null) sb.Append($"  값={m.SearchValue}");
                if (m.AlternativeIds.Count > 0) sb.Append($"  (동일 텍스트: {string.Join(", ", m.AlternativeIds)})");
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("──── 원본 아이템 텍스트 ────");
        sb.AppendLine(item.RawText.Trim());
        return sb.ToString();
    }
}
