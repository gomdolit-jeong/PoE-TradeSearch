using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PoETradeSearch.App.Native;
using PoETradeSearch.App.Services;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;

namespace PoETradeSearch.App;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Dictionary<string, ServerContext> _servers =
        TradeServer.All.ToDictionary(s => s.Id, s => new ServerContext(Http, s));
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CurrencyCatalog> _currencies = new();
    private readonly DispatcherTimer _gameWatcher = new() { Interval = TimeSpan.FromSeconds(3) };

    private GlobalHotkey? _hotkey;
    private SearchWindow? _searchWindow;
    private bool _busy;
    private bool _changingServer;
    private bool _gameSeen;

    public MainWindow()
    {
        InitializeComponent();

        _changingServer = true;
        ServerCombo.ItemsSource = TradeServer.All;
        ServerCombo.SelectedItem = TradeServer.FromId(_settings.Server);
        OnlyInGameCheck.IsChecked = _settings.OnlyInGame;
        ExitWithGameCheck.IsChecked = _settings.ExitWithGame;
        _changingServer = false;

        SourceInitialized += (_, _) => RegisterHotkey();
        Loaded += async (_, _) =>
        {
            await Task.WhenAll(LoadAllStatsAsync(), LoadLeaguesAsync(), LoadCurrenciesAsync());
            await OpenItemFromCommandLineAsync();
        };
        // Check whenever the user comes back to this window (e.g. after starting the game).
        Activated += (_, _) => CheckRunningGameElevation();
        _gameWatcher.Tick += (_, _) => WatchGame();
        _gameWatcher.Start();
        Closed += (_, _) =>
        {
            _gameWatcher.Stop();
            _hotkey?.Dispose();
            Application.Current.Shutdown();
        };
    }

    private TradeServer SelectedServer => ServerCombo.SelectedItem as TradeServer ?? TradeServer.Kakao;
    private ServerContext Current => _servers[SelectedServer.Id];
    private string? SelectedLeague => (LeagueCombo.SelectedItem as League)?.Id;

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GGG asks tools to identify themselves; requests without a User-Agent are rejected.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoETradeSearch/0.5");
        return http;
    }

    // ---------------- Hotkey ----------------

    private string HotkeyDescription => HotkeyText.Describe(_settings.HotkeyModifiers, _settings.HotkeyKey);

    private bool RegisterHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        try
        {
            _hotkey = new GlobalHotkey(this, _settings.HotkeyModifiers, _settings.HotkeyKey, HotkeyDescription);
            _hotkey.Pressed += async (_, _) => await OnHotkeyAsync();
            HotkeyBox.Text = HotkeyDescription;
            StatusText.Text = $"대기 중 — 게임에서 아이템에 마우스를 올리고 {HotkeyDescription}를 누르세요";
            return true;
        }
        catch (Exception ex)
        {
            HotkeyBox.Text = HotkeyDescription;
            StatusText.Text = $"단축키 {HotkeyDescription}를 등록하지 못했습니다. 다른 프로그램이 쓰고 있을 수 있어요. 다른 조합으로 바꿔 주세요.";
            Log.Error("단축키 등록 실패", ex);
            return false;
        }
    }

    // While the box has focus the global hotkey is released, so pressing the current combination reaches the box.
    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        HotkeyBox.Text = "키 조합을 누르세요...";
        HotkeyHintText.Text = "Esc: 취소";
    }

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyHintText.Text = "클릭 후 키 조합 입력";
        if (_hotkey is null)
            RegisterHotkey();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus(); // restores the previous hotkey
            FocusManager.SetFocusedElement(this, this);
            return;
        }
        if (HotkeyText.IsModifierKey(key))
            return; // wait for the actual key

        uint modifiers = HotkeyText.ToModifiers(Keyboard.Modifiers);
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!HotkeyText.IsAllowed(modifiers, key))
        {
            HotkeyBox.Text = "Ctrl/Alt/Shift와 함께, 또는 F1~F12";
            return;
        }

        var previous = (_settings.HotkeyModifiers, _settings.HotkeyKey);
        _settings.HotkeyModifiers = modifiers;
        _settings.HotkeyKey = vk;
        if (RegisterHotkey())
        {
            _settings.Save();
        }
        else
        {
            (_settings.HotkeyModifiers, _settings.HotkeyKey) = previous;
            RegisterHotkey();
        }
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    private void HotkeyResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.HotkeyModifiers = AppSettings.DefaultHotkeyModifiers;
        _settings.HotkeyKey = AppSettings.DefaultHotkeyKey;
        _settings.Save();
        RegisterHotkey();
    }

    private void OptionCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_changingServer)
            return;
        _settings.OnlyInGame = OnlyInGameCheck.IsChecked == true;
        _settings.ExitWithGame = ExitWithGameCheck.IsChecked == true;
        _settings.Save();
    }

    // ---------------- Game process ----------------

    private static bool IsGameProcess(Process p) =>
        p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase);

    /// <summary>Closes this app after the game it saw running has exited (if the option is on).</summary>
    private void WatchGame()
    {
        bool running;
        try
        {
            var processes = Process.GetProcesses();
            running = processes.Any(IsGameProcess);
            foreach (var p in processes) p.Dispose();
        }
        catch
        {
            return;
        }

        if (running)
        {
            _gameSeen = true;
            return;
        }
        if (_gameSeen && ExitWithGameCheck.IsChecked == true)
        {
            Log.Info("게임이 종료되어 함께 종료");
            Close();
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
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (!IsGameProcess(p))
                        continue;
                    ShowElevationWarning(NativeMethods.IsProcessElevated((uint)p.Id) == true);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("게임 프로세스 확인 실패: " + ex.Message);
        }
    }

    private void ShowElevationWarning(bool show) =>
        ElevationPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    private void RunAsAdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // The user pressed "No" on the UAC prompt, or the launch failed.
            Log.Warn("관리자 권한 실행 취소 또는 실패: " + ex.Message);
        }
    }

    // ---------------- Data ----------------

    /// <summary>Currency names and icons per server (Kakao has Korean names). Prices still show without them.</summary>
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
            // The API lists the current challenge league first, then Standard etc. League ids are the same on both servers.
            LeagueCombo.SelectedItem = leagues.FirstOrDefault(l => l.Id == _settings.League) ?? leagues.FirstOrDefault();
            LeagueStatusText.Text = "";
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
        await LoadLeaguesAsync();
    }

    /// <summary>Selects the server that matches the copied item's language (Korean → Kakao).</summary>
    private async Task<bool> SwitchServerForAsync(ParsedItem item)
    {
        var wanted = TradeServer.ForLanguage(item.Language);
        if (wanted == SelectedServer)
            return false;
        _changingServer = true;
        ServerCombo.SelectedItem = wanted;
        _changingServer = false;
        _settings.Server = wanted.Id;
        _settings.Save();
        await LoadLeaguesAsync();
        return true;
    }

    /// <summary>Loads both servers' option data so either client language works right away.</summary>
    private async Task LoadAllStatsAsync()
    {
        await Task.WhenAll(_servers.Values.Select(async ctx =>
        {
            try { await ctx.GetMatcherAsync(); } catch { /* shown below */ }
            await ctx.GetGemsAsync();
        }));

        bool allOk = _servers.Values.All(s => s.Matcher is not null);
        StatsText.Text = allOk
            ? "거래소 데이터 준비 완료"
            : "거래소 데이터 일부를 받지 못했습니다: " + string.Join(" · ",
                _servers.Values.Where(s => s.Matcher is null).Select(s => $"{s.Server.DisplayName} {s.StatusText}"));
        StatsDot.Fill = allOk ? Palette.Online : (System.Windows.Media.Brush)FindResource("Danger");
    }

    /// <summary>
    /// For development: "PoETradeSearch.exe --item item.txt" opens the search window for the item text in the
    /// file, so the window can be checked without the game.
    /// </summary>
    private async Task OpenItemFromCommandLineAsync()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--item");
        if (i >= 0 && i + 1 < args.Length && System.IO.File.Exists(args[i + 1]))
            await ShowItemAsync(await System.IO.File.ReadAllTextAsync(args[i + 1]));

        // "--snapshot dir": render the windows to PNG files (works even with a locked screen), then exit.
        int s = Array.IndexOf(args, "--snapshot");
        if (s >= 0 && s + 1 < args.Length)
        {
            await Task.Delay(TimeSpan.FromSeconds(6)); // let the search finish
            System.IO.Directory.CreateDirectory(args[s + 1]);
            SaveSnapshot(this, System.IO.Path.Combine(args[s + 1], "main.png"));
            if (_searchWindow is not null)
                SaveSnapshot(_searchWindow, System.IO.Path.Combine(args[s + 1], "search.png"));
            Close();
        }
    }

    private static void SaveSnapshot(Window window, string path)
    {
        if (window.Content is not System.Windows.FrameworkElement root)
            return;
        root.UpdateLayout();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        // Opaque background so transparent window corners don't come out black.
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20)), null,
                new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    // ---------------- Search ----------------

    private async Task OnHotkeyAsync()
    {
        var fg = ForegroundInfo.Current();
        if (_busy)
            return;
        if (OnlyInGameCheck.IsChecked == true && !fg.IsGame)
            return;

        bool elevationMismatch = fg.Elevated == true && !ItemClipboardReader.SelfElevated;
        ShowElevationWarning(elevationMismatch);

        _busy = true;
        try
        {
            var text = await ItemClipboardReader.CopyItemUnderCursorAsync((ushort)_settings.HotkeyKey, TimeSpan.FromMilliseconds(700));
            if (text is null)
            {
                StatusText.Text = elevationMismatch
                    ? "아이템을 복사하지 못했습니다. 게임이 관리자 권한으로 실행 중이라 이 프로그램도 관리자 권한으로 실행해야 합니다."
                    : "아이템을 복사하지 못했습니다. 마우스가 아이템 위에 있었나요?";
                return;
            }
            await ShowItemAsync(text);
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

    private async Task ShowItemAsync(string text)
    {
        try
        {
            var item = ItemTextParser.Parse(text);
            await SwitchServerForAsync(item);
            var ctx = Current;

            IReadOnlyList<MatchedMod> matches;
            try
            {
                matches = (await ctx.GetMatcherAsync()).Match(item);
            }
            catch (Exception ex)
            {
                StatusText.Text = "거래소 옵션 데이터가 없어 검색할 수 없습니다: " + ex.Message;
                return;
            }

            if (SelectedLeague is not { } league)
            {
                StatusText.Text = "리그를 선택해야 검색할 수 있습니다";
                return;
            }

            StatusText.Text = $"검색: {item.Name ?? item.BaseType} ({DateTime.Now:HH:mm:ss})";
            OpenSearchWindow(item, matches, ctx, await ctx.GetGemsAsync(), league);
        }
        catch (Exception ex)
        {
            Log.Error("아이템 처리 실패. 원본:\n" + text, ex);
            StatusText.Text = "아이템을 읽지 못했습니다: " + ex.Message;
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
}
