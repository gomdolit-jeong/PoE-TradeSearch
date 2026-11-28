using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoETradeSearch.App.Native;
using PoETradeSearch.App.Services;
using PoETradeSearch.Core.Data;
using PoETradeSearch.Core.Parsing;
using PoETradeSearch.Core.Trade;
using PoETradeSearch.Core.Update;

namespace PoETradeSearch.App.ViewModels;

public sealed record ScaleOption(double Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// The settings window and the app's background work: trade data, the hotkey → copy → search flow,
/// and watching the game process. Windows are opened by the view through <see cref="SearchRequested"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly Dictionary<string, ServerContext> _servers =
        TradeServer.All.ToDictionary(s => s.Id, s => new ServerContext(Http, s));
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CurrencyCatalog> _currencies = new();
    private readonly DispatcherTimer _gameWatcher = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _loading = true;
    private bool _busy;
    private bool _gameSeen;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;

        // League-dependent words and filters: the user's game-data.json if there is one, otherwise the built-in file.
        DataNote = GameData.Load(TradeDataCache.CacheDirectory)
                   ?? (GameData.Source == "내장" ? "" : $"게임 데이터: {GameData.Source}");

        Servers = TradeServer.All;
        _selectedServer = TradeServer.FromId(settings.Server);
        ScaleOptions = AppSettings.UiScales.Select(s => new ScaleOption(s, $"{s * 100:0}%")).ToList();
        _selectedScale = ScaleOptions.FirstOrDefault(o => o.Value == settings.UiScale) ?? ScaleOptions[0];
        _onlyInGame = settings.OnlyInGame;
        _exitWithGame = settings.ExitWithGame;
        _showOverlay = settings.ShowOverlay;
        _loading = false;

        _gameWatcher.Tick += (_, _) => WatchGame();
        _gameWatcher.Start();
    }

    public AppSettings Settings { get; }

    /// <summary>A search window should open for this item.</summary>
    public event Action<SearchViewModel>? SearchRequested;

    /// <summary>The game was closed and "게임을 종료하면 같이 종료" is on.</summary>
    public event Action? ExitRequested;

    // ---------------- Settings ----------------

    public IReadOnlyList<TradeServer> Servers { get; }
    [ObservableProperty] private TradeServer _selectedServer;

    public ObservableCollection<League> Leagues { get; } = [];
    [ObservableProperty] private League? _selectedLeague;
    [ObservableProperty] private string _leagueStatus = "";

    public IReadOnlyList<ScaleOption> ScaleOptions { get; }
    [ObservableProperty] private ScaleOption _selectedScale;

    [ObservableProperty] private bool _onlyInGame;
    [ObservableProperty] private bool _exitWithGame;
    [ObservableProperty] private bool _showOverlay;

    partial void OnSelectedServerChanged(TradeServer value)
    {
        if (_loading)
            return;
        Settings.Server = value.Id;
        Settings.Save();
        _ = LoadLeaguesAsync();
    }

    partial void OnSelectedLeagueChanged(League? value)
    {
        if (_loading || value is null || value.Id == Settings.League)
            return;
        Settings.League = value.Id;
        Settings.Save();
    }

    partial void OnSelectedScaleChanged(ScaleOption value) => Save(() => Settings.UiScale = value.Value);
    partial void OnOnlyInGameChanged(bool value) => Save(() => Settings.OnlyInGame = value);
    partial void OnExitWithGameChanged(bool value) => Save(() => Settings.ExitWithGame = value);
    partial void OnShowOverlayChanged(bool value) => Save(() => Settings.ShowOverlay = value);

    private void Save(Action change)
    {
        if (_loading)
            return;
        change();
        Settings.Save();
    }

    // ---------------- Status ----------------

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(OverlayText))] private string _hotkeyText = "";
    [ObservableProperty] private string _statusText = "준비 중...";
    [ObservableProperty] private string _statsText = "거래소 데이터 불러오는 중...";
    [ObservableProperty] private Brush _statsBrush = Palette.TextFaint;
    [ObservableProperty] private bool _showElevationWarning;

    /// <summary>Note about a user game-data.json (where it was loaded from, or why it couldn't be).</summary>
    public string DataNote { get; }
    public bool HasDataNote => DataNote.Length > 0;

    /// <summary>Text of the small "running" badge at the top of the screen.</summary>
    public string OverlayText => $"PoE 검색기 실행 중 · {HotkeyText}";

    // ---------------- Data ----------------

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GGG asks tools to identify themselves; requests without a User-Agent are rejected.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoETradeSearch/" + Updater.CurrentVersion);
        return http;
    }

    private ServerContext Current => _servers[SelectedServer.Id];

    /// <summary>Loads stats (both servers), leagues and currency data, and checks for a new version.</summary>
    public Task InitializeAsync() =>
        Task.WhenAll(LoadAllStatsAsync(), LoadLeaguesAsync(), LoadCurrenciesAsync(), CheckForUpdateAsync());

    // ---------------- Update ----------------

    public string VersionText { get; } = "v" + Updater.CurrentVersion;

    /// <summary>Set when GitHub has a newer release; shows the update bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateText))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private ReleaseInfo? _availableUpdate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyPropertyChangedFor(nameof(UpdateButtonText))]
    private bool _isUpdating;

    [ObservableProperty] private string _updateStatus = "";

    public bool HasUpdate => AvailableUpdate is not null;
    public string UpdateText => AvailableUpdate is { } r ? $"새 버전이 나왔습니다: {r.Tag} (지금 {VersionText})" : "";
    public string UpdateButtonText => IsUpdating ? "받는 중..." : "업데이트";

    /// <summary>Opens a URL in the browser (set by the view; tests leave it alone).</summary>
    public Action<string> OpenUrl { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private async Task CheckForUpdateAsync()
    {
        if (System.Windows.Application.Current is App && App.JustUpdated)
            UpdateStatus = $"{VersionText} 업데이트 완료";
        AvailableUpdate = await Updater.CheckAsync();
    }

    private bool CanInstallUpdate() => AvailableUpdate is not null && !IsUpdating;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (AvailableUpdate is not { } release)
            return;
        if (!Updater.CanReplaceExe())
        {
            UpdateStatus = "이 폴더에는 파일을 바꿀 권한이 없어 릴리스 페이지를 열었습니다. 직접 받아 주세요.";
            OpenUrl(release.PageUrl);
            return;
        }

        IsUpdating = true;
        UpdateStatus = "새 버전을 받는 중... 0%";
        try
        {
            var progress = new Progress<double>(p => UpdateStatus = $"새 버전을 받는 중... {p * 100:0}%");
            await Updater.InstallAsync(release, progress);
            UpdateStatus = "새 버전으로 다시 시작합니다";
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("업데이트 실패", ex);
            UpdateStatus = "업데이트하지 못했습니다: " + ex.Message;
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void OpenRepository() => OpenUrl($"https://github.com/{ReleaseInfo.Repository}");

    [RelayCommand]
    private void OpenReleasePage() => OpenUrl(AvailableUpdate?.PageUrl ?? ReleaseInfo.ReleasesPageUrl);

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
            _loading = true;
            Leagues.Clear();
            foreach (var l in leagues)
                Leagues.Add(l);
            // The API lists the current challenge league first, then Standard etc. League ids are the same on both servers.
            SelectedLeague = leagues.FirstOrDefault(l => l.Id == Settings.League) ?? leagues.FirstOrDefault();
            _loading = false;
            LeagueStatus = "";
        }
        catch (Exception ex)
        {
            _loading = false;
            Log.Error("리그 목록 로드 실패", ex);
            LeagueStatus = "리그 목록을 받지 못했습니다: " + ex.Message;
        }
    }

    private async Task LoadAllStatsAsync()
    {
        await Task.WhenAll(_servers.Values.Select(async ctx =>
        {
            try { await ctx.GetMatcherAsync(); } catch { /* shown below */ }
            await ctx.GetGemsAsync();
        }));

        bool allOk = _servers.Values.All(s => s.Matcher is not null);
        StatsText = allOk
            ? "거래소 데이터 준비 완료"
            : "거래소 데이터 일부를 받지 못했습니다: " + string.Join(" · ",
                _servers.Values.Where(s => s.Matcher is null).Select(s => $"{s.Server.DisplayName} {s.StatusText}"));
        StatsBrush = allOk ? Palette.Online : Palette.Danger;
    }

    // ---------------- Search ----------------

    /// <summary>Hotkey pressed: copy the item under the cursor and open a search for it.</summary>
    public async Task OnHotkeyAsync()
    {
        var fg = ForegroundInfo.Current();
        if (_busy)
            return;
        if (OnlyInGame && !fg.IsGame)
            return;

        bool elevationMismatch = fg.Elevated == true && !ItemClipboardReader.SelfElevated;
        ShowElevationWarning = elevationMismatch;

        _busy = true;
        try
        {
            var text = await ItemClipboardReader.CopyItemUnderCursorAsync((ushort)Settings.HotkeyKey, TimeSpan.FromMilliseconds(700));
            if (text is null)
            {
                StatusText = elevationMismatch
                    ? "아이템을 복사하지 못했습니다. 게임이 관리자 권한으로 실행 중이라 이 프로그램도 관리자 권한으로 실행해야 합니다."
                    : "아이템을 복사하지 못했습니다. 마우스가 아이템 위에 있었나요?";
                return;
            }
            await ShowItemAsync(text);
        }
        catch (Exception ex)
        {
            Log.Error("단축키 처리 중 오류", ex);
            StatusText = "오류: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Parses the item text, picks the server for its language and asks the view to open a search window.</summary>
    public async Task ShowItemAsync(string text)
    {
        try
        {
            var item = ItemTextParser.Parse(text);
            var wanted = TradeServer.ForLanguage(item.Language);
            if (wanted != SelectedServer)
            {
                _loading = true;
                SelectedServer = wanted;
                _loading = false;
                Settings.Server = wanted.Id;
                Settings.Save();
                await LoadLeaguesAsync();
            }
            var ctx = Current;

            IReadOnlyList<MatchedMod> matches;
            try
            {
                matches = (await ctx.GetMatcherAsync()).Match(item);
            }
            catch (Exception ex)
            {
                StatusText = "거래소 옵션 데이터가 없어 검색할 수 없습니다: " + ex.Message;
                return;
            }

            if (SelectedLeague is not { } league)
            {
                StatusText = "리그를 선택해야 검색할 수 있습니다";
                return;
            }

            StatusText = $"검색: {item.Name ?? item.BaseType}";
            var search = new SearchViewModel(item, matches, ctx.Api, CurrenciesFor(ctx.Server), await ctx.GetGemsAsync(),
                                             league.Id, league.Text);
            SearchRequested?.Invoke(search);
        }
        catch (Exception ex)
        {
            Log.Error("아이템 처리 실패. 원본:\n" + text, ex);
            StatusText = "아이템을 읽지 못했습니다: " + ex.Message;
        }
    }

    // ---------------- Game process ----------------

    private static bool IsGameProcess(Process p) =>
        p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase);

    /// <summary>Asks the app to close after the game it saw running has exited (if the option is on).</summary>
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
        if (_gameSeen && ExitWithGame)
        {
            Log.Info("게임이 종료되어 함께 종료");
            _gameWatcher.Stop();
            ExitRequested?.Invoke();
        }
    }

    /// <summary>Warns early if PoE is already running as administrator while this app isn't.</summary>
    public void CheckRunningGameElevation()
    {
        if (ItemClipboardReader.SelfElevated)
        {
            ShowElevationWarning = false;
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
                    ShowElevationWarning = NativeMethods.IsProcessElevated((uint)p.Id) == true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("게임 프로세스 확인 실패: " + ex.Message);
        }
    }

    [RelayCommand]
    private static void RunAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // The user pressed "No" on the UAC prompt, or the launch failed.
            Log.Warn("관리자 권한 실행 취소 또는 실패: " + ex.Message);
        }
    }

    public void Stop() => _gameWatcher.Stop();
}
