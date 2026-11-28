using System.Windows.Input;
using PoETradeSearch.App.Native;

namespace PoETradeSearch.App.Services;

/// <summary>Converts between WPF keys, RegisterHotKey values and the text shown to the user ("Ctrl+D").</summary>
public static class HotkeyText
{
    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public static uint ToModifiers(ModifierKeys modifiers)
    {
        uint m = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;
        return m;
    }

    /// <summary>
    /// A plain letter would fire while typing in chat, so a modifier is required, except for F1–F12.
    /// Ctrl+C / Ctrl+V are refused because the tool itself sends Ctrl+Alt+C to the game.
    /// </summary>
    public static bool IsAllowed(uint modifiers, Key key)
    {
        bool function = key is >= Key.F1 and <= Key.F12;
        if (modifiers == 0 && !function)
            return false;
        if (modifiers == NativeMethods.MOD_CONTROL && key is Key.C or Key.V)
            return false;
        return true;
    }

    public static string Describe(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyName(KeyInterop.KeyFromVirtualKey((int)virtualKey)));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(key - Key.NumPad0),
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        _ => key.ToString(),
    };
}
