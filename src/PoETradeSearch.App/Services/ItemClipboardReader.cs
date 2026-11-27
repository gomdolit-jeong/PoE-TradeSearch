using System.Windows;
using PoETradeSearch.App.Native;
using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.App.Services;

/// <summary>
/// Copies the item under the mouse cursor in PoE by sending one Ctrl+C, then reads the clipboard.
/// One hotkey press = one Ctrl+C, which stays within what GGG allows for third-party tools.
/// </summary>
public static class ItemClipboardReader
{
    public const string GameWindowTitle = "Path of Exile";

    public static bool IsGameInForeground() =>
        NativeMethods.GetForegroundWindowTitle() == GameWindowTitle;

    /// <summary>Returns the copied item text, or null if nothing item-like reached the clipboard in time.</summary>
    public static async Task<string?> CopyItemUnderCursorAsync(TimeSpan timeout)
    {
        var previous = TryGetClipboardText();
        TryClearClipboard();

        // The user is still holding the hotkey's modifiers (e.g. Ctrl of Ctrl+D). Release Shift/Alt
        // so the game sees a plain Ctrl+C (Ctrl+Alt+C is the advanced copy, which we also accept).
        var release = new List<(ushort, bool)>();
        if (NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT)) release.Add((NativeMethods.VK_SHIFT, true));
        if (NativeMethods.IsKeyDown(NativeMethods.VK_MENU)) release.Add((NativeMethods.VK_MENU, true));
        if (release.Count > 0) NativeMethods.SendKeys([.. release]);

        bool ctrlHeld = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
        var keys = new List<(ushort, bool)>();
        if (!ctrlHeld) keys.Add((NativeMethods.VK_CONTROL, false));
        keys.Add((NativeMethods.VK_C, false));
        keys.Add((NativeMethods.VK_C, true));
        if (!ctrlHeld) keys.Add((NativeMethods.VK_CONTROL, true));
        NativeMethods.SendKeys([.. keys]);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            var text = TryGetClipboardText();
            if (ItemTextParser.LooksLikeItem(text))
            {
                if (previous is not null) TrySetClipboardText(previous);
                return text;
            }
        }

        if (previous is not null) TrySetClipboardText(previous);
        return null;
    }

    // The clipboard can be briefly locked by another process, so every access is guarded.
    private static string? TryGetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    private static void TryClearClipboard()
    {
        try { Clipboard.Clear(); } catch { }
    }

    private static void TrySetClipboardText(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }
}
