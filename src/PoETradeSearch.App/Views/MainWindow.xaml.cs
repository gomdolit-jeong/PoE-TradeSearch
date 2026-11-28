using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using PoETradeSearch.App.Services;
using PoETradeSearch.App.ViewModels;

namespace PoETradeSearch.App.Views;

/// <summary>
/// Settings window. Settings, data and the search flow are in <see cref="MainViewModel"/>; this class does the
/// parts that need a real window: the global hotkey (it needs a window handle), capturing a new hotkey from the
/// keyboard, opening search windows and the "running" overlay.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new(AppSettings.Load());
    private readonly StatusOverlay _overlay;
    private GlobalHotkey? _hotkey;
    private SearchWindow? _searchWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _overlay = new StatusOverlay { DataContext = _vm };
        if (Settings.OverlayLeft is { } left && Settings.OverlayTop is { } top)
            _overlay.Position = new Point(left, top);
        _overlay.Moved += p =>
        {
            Settings.OverlayLeft = p.X;
            Settings.OverlayTop = p.Y;
            Settings.Save();
        };

        _vm.SearchRequested += OpenSearchWindow;
        _vm.ExitRequested += Close;
        _vm.PropertyChanged += Vm_PropertyChanged;

        SourceInitialized += (_, _) =>
        {
            RegisterHotkey();
            UpdateOverlay();
        };
        Loaded += async (_, _) =>
        {
            await _vm.InitializeAsync();
            await DevTools.RunCommandLineAsync(this, _vm, () => _searchWindow);
        };
        // Check whenever the user comes back to this window (e.g. after starting the game).
        Activated += (_, _) => _vm.CheckRunningGameElevation();
        Closed += (_, _) =>
        {
            _vm.Stop();
            _hotkey?.Dispose();
            Application.Current.Shutdown();
        };
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ShowOverlay):
                UpdateOverlay();
                break;
            case nameof(MainViewModel.SelectedScale):
                _searchWindow?.ApplyScale(_vm.SelectedScale.Value);
                break;
        }
    }

    private void UpdateOverlay()
    {
        if (_vm.ShowOverlay)
            _overlay.ShowBadge();
        else
            _overlay.Hide();
    }

    private void MoveOverlayButton_Click(object sender, RoutedEventArgs e) => _overlay.BeginMove();

    private void ResetOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        Settings.OverlayLeft = Settings.OverlayTop = null;
        Settings.Save();
        _overlay.Position = null;
    }

    /// <summary>Only one search window at a time: a new hotkey press replaces the previous one.</summary>
    private void OpenSearchWindow(SearchViewModel search)
    {
        _searchWindow?.Close();
        _searchWindow = new SearchWindow(search) { Owner = null };
        _searchWindow.Closed += (s, _) => { if (ReferenceEquals(_searchWindow, s)) _searchWindow = null; };
        _searchWindow.ApplyScale(_vm.SelectedScale.Value);
        _searchWindow.PlaceNearCursor();
        _searchWindow.Show();
        _searchWindow.Activate();
    }

    // ---------------- Hotkey ----------------

    private AppSettings Settings => _vm.Settings;
    private string HotkeyDescription => HotkeyText.Describe(Settings.HotkeyModifiers, Settings.HotkeyKey);

    private bool RegisterHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        _vm.HotkeyText = HotkeyDescription;
        HotkeyBox.Text = HotkeyDescription;
        try
        {
            _hotkey = new GlobalHotkey(this, Settings.HotkeyModifiers, Settings.HotkeyKey, HotkeyDescription);
            _hotkey.Pressed += async (_, _) => await _vm.OnHotkeyAsync();
            _vm.StatusText = $"대기 중 — 게임에서 아이템에 마우스를 올리고 {HotkeyDescription}를 누르세요";
            return true;
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"단축키 {HotkeyDescription}를 등록하지 못했습니다. 다른 프로그램이 쓰고 있을 수 있어요. 다른 조합으로 바꿔 주세요.";
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
            ClearHotkeyFocus(); // restores the previous hotkey
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

        var previous = (Settings.HotkeyModifiers, Settings.HotkeyKey);
        Settings.HotkeyModifiers = modifiers;
        Settings.HotkeyKey = vk;
        if (RegisterHotkey())
        {
            Settings.Save();
        }
        else
        {
            (Settings.HotkeyModifiers, Settings.HotkeyKey) = previous;
            RegisterHotkey();
        }
        ClearHotkeyFocus();
    }

    private void ClearHotkeyFocus()
    {
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    private void HotkeyResetButton_Click(object sender, RoutedEventArgs e)
    {
        Settings.HotkeyModifiers = AppSettings.DefaultHotkeyModifiers;
        Settings.HotkeyKey = AppSettings.DefaultHotkeyKey;
        Settings.Save();
        RegisterHotkey();
    }
}
