using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PoETradeSearch.App.Native;

namespace PoETradeSearch.App.Views;

/// <summary>
/// "PoE 검색기 실행 중 · Ctrl+D" badge. By default at the top centre of the main screen; the user can drag it
/// elsewhere in move mode. Normally it never takes focus and mouse clicks go through it to the game.
/// Shown over the game in windowed / borderless fullscreen; an exclusive fullscreen game covers every other window.
/// </summary>
public partial class StatusOverlay : Window
{
    private IntPtr _hwnd;
    private Point? _position;
    private bool _moving;

    public StatusOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetClickThrough(_hwnd, true);
        };
        SizeChanged += (_, _) => Place();
    }

    /// <summary>The user dropped the badge at a new place (left, top in screen units).</summary>
    public event Action<Point>? Moved;

    /// <summary>Saved position, or null for the default (top centre).</summary>
    public Point? Position
    {
        get => _position;
        set
        {
            _position = value;
            Place();
        }
    }

    public void ShowBadge()
    {
        Show();
        Place();
    }

    /// <summary>Lets the user drag the badge once; it goes back to click-through after the drop.</summary>
    public void BeginMove()
    {
        ShowBadge();
        _moving = true;
        NativeMethods.SetClickThrough(_hwnd, false);
        MoveHint.Visibility = Visibility.Visible;
        Badge.BorderBrush = (Brush)FindResource("Gold");
        Badge.BorderThickness = new Thickness(1);
        Badge.CornerRadius = new CornerRadius(8);
        Cursor = Cursors.SizeAll;
    }

    private void EndMove()
    {
        _moving = false;
        NativeMethods.SetClickThrough(_hwnd, true);
        MoveHint.Visibility = Visibility.Collapsed;
        Badge.BorderBrush = (Brush)FindResource("BorderStrong");
        Cursor = null;
        _position = new Point(Left, Top);
        ApplyShape();
        Moved?.Invoke(_position.Value);
    }

    private void Badge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_moving)
            return;
        DragMove(); // returns when the button is released
        EndMove();
    }

    private void Place()
    {
        var screen = SystemParameters.WorkArea;
        if (_position is { } p)
        {
            // Keep it on screen even if the resolution changed since it was saved.
            Left = Math.Clamp(p.X, SystemParameters.VirtualScreenLeft,
                Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth));
            Top = Math.Clamp(p.Y, SystemParameters.VirtualScreenTop,
                Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight));
        }
        else
        {
            Left = screen.Left + (screen.Width - ActualWidth) / 2;
            Top = 0;
        }
        if (!_moving)
            ApplyShape();
    }

    /// <summary>Flat top edge when it touches the top of the screen, rounded all round elsewhere.</summary>
    private void ApplyShape()
    {
        bool atTop = Top <= SystemParameters.VirtualScreenTop + 1;
        Badge.CornerRadius = atTop ? new CornerRadius(0, 0, 8, 8) : new CornerRadius(8);
        Badge.BorderThickness = atTop ? new Thickness(1, 0, 1, 1) : new Thickness(1);
    }
}
