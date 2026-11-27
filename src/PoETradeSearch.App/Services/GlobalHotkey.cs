using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PoETradeSearch.App.Native;

namespace PoETradeSearch.App.Services;

/// <summary>
/// System-wide hotkey registered with RegisterHotKey. Raises <see cref="Pressed"/> on the UI thread.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private static int _nextId = 0x1000;

    private readonly int _id = _nextId++;
    private readonly HwndSource _source;

    public event EventHandler? Pressed;

    public string Description { get; }

    public GlobalHotkey(Window owner, uint modifiers, uint virtualKey, string description)
    {
        Description = description;
        var handle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window has no HwndSource.");
        _source.AddHook(WndProc);

        if (!NativeMethods.RegisterHotKey(handle, _id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            _source.RemoveHook(WndProc);
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Could not register hotkey {description}. Another program may already use it.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(_source.Handle, _id);
        _source.RemoveHook(WndProc);
    }
}
