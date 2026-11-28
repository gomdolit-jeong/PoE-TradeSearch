using System.Diagnostics;
using System.Windows;
using PoETradeSearch.App.Native;
using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.App.Services;

/// <summary>What is in front when the hotkey is pressed.</summary>
public sealed record ForegroundInfo(string Title, uint ProcessId, string? ProcessName, bool? Elevated)
{
    /// <summary>
    /// PoE1 executables: PathOfExile.exe, PathOfExile_x64.exe, PathOfExileSteam.exe, PathOfExile_x64Steam.exe,
    /// PathOfExile_KG.exe (Kakao), PathOfExileEGS.exe (Epic)… all start with "PathOfExile".
    /// The window title is a fallback in case the process name can't be read.
    /// </summary>
    public bool IsGame =>
        ProcessName?.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase) == true ||
        Title.StartsWith("Path of Exile", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"title='{Title}' pid={ProcessId} process={ProcessName ?? "?"} elevated={Elevated?.ToString() ?? "?"}";

    public static ForegroundInfo Current()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        string? name = null;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
        return new ForegroundInfo(NativeMethods.GetForegroundWindowTitle(), pid, name, NativeMethods.IsProcessElevated(pid));
    }
}

/// <summary>
/// Copies the item under the mouse cursor in PoE by sending one Ctrl+C, then reads the clipboard.
/// One hotkey press = one Ctrl+C, which stays within what GGG allows for third-party tools.
/// </summary>
public static class ItemClipboardReader
{
    public static bool SelfElevated { get; } = Environment.IsPrivilegedProcess;

    /// <summary>Returns the copied item text, or null if nothing item-like reached the clipboard in time.</summary>
    /// <param name="hotkeyVk">The hotkey's main key (D of Ctrl+D); we wait until it's released.</param>
    public static async Task<string?> CopyItemUnderCursorAsync(ushort hotkeyVk, TimeSpan timeout)
    {
        // Don't clear the clipboard: Clipboard.Clear() can block for seconds (clipboard history/other apps),
        // and by then the mouse may have left the item. Instead remember the change counter and wait for it to move.
        var previous = TryGetClipboardText();
        var sequenceBefore = NativeMethods.GetClipboardSequenceNumber();

        // Wait (briefly) until the hotkey's own key is released, so the game sees Ctrl+C and not Ctrl+D+C.
        var releaseDeadline = DateTime.UtcNow.AddMilliseconds(300);
        while (NativeMethods.IsKeyDown(hotkeyVk) && DateTime.UtcNow < releaseDeadline)
            await Task.Delay(10);
        if (NativeMethods.IsKeyDown(hotkeyVk))
            Log.Warn("단축키의 D 키가 300ms 안에 떼어지지 않아 그대로 Ctrl+C를 보냅니다");

        // Release Shift so the game sees exactly Ctrl+Alt+C.
        if (NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT))
            NativeMethods.SendKeys((NativeMethods.VK_SHIFT, true));

        // Ctrl+Alt+C is the game's advanced copy: same text plus each mod's tier and roll range
        // ("97(85-109)%"), which the search window uses as the default min/max.
        bool ctrlHeld = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
        bool altHeld = NativeMethods.IsKeyDown(NativeMethods.VK_MENU);
        var keys = new List<(ushort, bool)>();
        if (!ctrlHeld) keys.Add((NativeMethods.VK_CONTROL, false));
        if (!altHeld) keys.Add((NativeMethods.VK_MENU, false));
        keys.Add((NativeMethods.VK_C, false));
        keys.Add((NativeMethods.VK_C, true));
        if (!altHeld) keys.Add((NativeMethods.VK_MENU, true));
        if (!ctrlHeld) keys.Add((NativeMethods.VK_CONTROL, true));
        var (sent, error) = NativeMethods.SendKeys([.. keys]);
        Log.Info($"Ctrl+Alt+C 전송: ctrl누름={ctrlHeld} alt누름={altHeld} 이벤트 {sent}/{keys.Count}개 성공" + (error != 0 ? $" 오류={error}" : ""));

        var started = DateTime.UtcNow;
        var deadline = started + timeout;
        string? lastSeen = null;
        bool changed = false;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
            if (NativeMethods.GetClipboardSequenceNumber() == sequenceBefore)
                continue;
            changed = true;
            var text = TryGetClipboardText();
            if (ItemTextParser.LooksLikeItem(text))
            {
                Log.Info($"클립보드에서 아이템 텍스트 확인 ({(DateTime.UtcNow - started).TotalMilliseconds:0}ms, {text!.Length}자)");
                RestoreLater(previous);
                return text;
            }
            lastSeen = text;
        }

        Log.Warn(!changed
            ? $"{timeout.TotalMilliseconds:0}ms 동안 클립보드가 바뀌지 않음 — 게임이 Ctrl+C를 받지 못했거나 마우스가 아이템 위에 없음"
            : $"클립보드에 아이템이 아닌 내용이 들어옴: {(lastSeen is null ? "(텍스트 아님)" : Shorten(lastSeen))}");
        return null;
    }

    /// <summary>
    /// Puts the user's previous clipboard text back, after the search window is up, so a slow clipboard
    /// never delays the search.
    /// </summary>
    private static void RestoreLater(string? previous)
    {
        if (previous is null || ItemTextParser.LooksLikeItem(previous))
            return;
        Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            var sw = Stopwatch.StartNew();
            TrySetClipboardText(previous);
            if (sw.ElapsedMilliseconds > 200)
                Log.Info($"이전 클립보드 복원에 {sw.ElapsedMilliseconds}ms 걸림");
        });
    }

    private static string Shorten(string s) => s.Length > 80 ? s[..80].Replace("\n", "\\n") + "…" : s.Replace("\n", "\\n");

    // The clipboard can be briefly locked by another process, so every access is guarded.
    private static string? TryGetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    private static void TrySetClipboardText(string text)
    {
        // copy:false skips OleFlushClipboard, which is the slow part; the text stays available while we run.
        try { Clipboard.SetDataObject(text, false); } catch { }
    }
}
