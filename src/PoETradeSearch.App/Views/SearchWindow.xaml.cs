using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PoETradeSearch.App.ViewModels;

namespace PoETradeSearch.App.Views;

/// <summary>
/// The search window. All state and commands are in <see cref="SearchViewModel"/>; this class only handles
/// what belongs to the window itself: position, size scale, dragging and closing.
/// </summary>
public partial class SearchWindow : Window
{
    // Window size at 100% (matches the XAML).
    private const double BaseWidth = 480, BaseHeight = 740, BaseMinWidth = 440, BaseMinHeight = 400;

    public SearchWindow(SearchViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => viewModel.Cancel();
        Loaded += async (_, _) => await viewModel.SearchAsync();
    }

    public SearchViewModel ViewModel { get; }

    /// <summary>Scales text and controls (setting "화면 크기"), keeping the window inside the screen.</summary>
    public void ApplyScale(double scale)
    {
        RootBorder.LayoutTransform = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(BaseMinWidth * scale, area.Width);
        MinHeight = Math.Min(BaseMinHeight * scale, area.Height);
        Width = Math.Min(BaseWidth * scale, area.Width);
        Height = Math.Min(BaseHeight * scale, area.Height);
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

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
