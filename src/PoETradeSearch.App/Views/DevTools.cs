using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PoETradeSearch.App.ViewModels;

namespace PoETradeSearch.App.Views;

/// <summary>
/// Developer options for checking the windows without the game:
/// "--item file" opens the search window for the item text in the file;
/// "--snapshot dir" saves the windows as PNG files (works with a locked screen) and exits;
/// "--install-update" presses the update button as soon as a newer release is found.
/// </summary>
internal static class DevTools
{
    public static async Task RunCommandLineAsync(Window main, MainViewModel vm, Func<Window?> searchWindow)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--item");
        if (i >= 0 && i + 1 < args.Length && File.Exists(args[i + 1]))
            await vm.ShowItemAsync(await File.ReadAllTextAsync(args[i + 1]));

        // Runs the update button without clicking (to test the update flow with an older build).
        if (args.Contains("--install-update") && vm.InstallUpdateCommand.CanExecute(null))
            await vm.InstallUpdateCommand.ExecuteAsync(null);

        int s = Array.IndexOf(args, "--snapshot");
        if (s >= 0 && s + 1 < args.Length)
        {
            await Task.Delay(TimeSpan.FromSeconds(6)); // let the search finish
            Directory.CreateDirectory(args[s + 1]);
            SaveSnapshot(main, Path.Combine(args[s + 1], "main.png"));
            if (searchWindow() is { } search)
                SaveSnapshot(search, Path.Combine(args[s + 1], "search.png"));
            foreach (Window w in Application.Current.Windows)
                if (w is StatusOverlay overlay && overlay.IsVisible)
                    SaveSnapshot(overlay, Path.Combine(args[s + 1], "overlay.png"));
            main.Close();
        }
    }

    private static void SaveSnapshot(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
            return;
        root.UpdateLayout();
        // Include the "화면 크기" scale (LayoutTransform) so the PNG matches what is on screen.
        var size = root.LayoutTransform.TransformBounds(new Rect(root.RenderSize)).Size;
        if (size.Width < 1 || size.Height < 1)
            return;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        // Opaque background so transparent window corners don't come out black.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(size));
        }
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
