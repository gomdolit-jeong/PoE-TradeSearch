using System.Runtime.InteropServices;
using System.Text;

namespace PoETradeSearch.App.Native;

internal static partial class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_MENU = 0x12; // Alt
    public const ushort VK_C = 0x43;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    public static string GetForegroundWindowTitle()
    {
        var buffer = new char[256];
        int length = GetWindowText(GetForegroundWindow(), buffer, buffer.Length);
        return new string(buffer, 0, length);
    }

    public static bool IsKeyDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Sends key presses to the foreground window. Each entry is (virtual key, isKeyUp).</summary>
    public static void SendKeys(params (ushort Vk, bool Up)[] keys)
    {
        var inputs = new INPUT[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = keys[i].Vk,
                        dwFlags = keys[i].Up ? KEYEVENTF_KEYUP : 0,
                    },
                },
            };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // MOUSEINPUT is the largest member; it must be in the union so INPUT has the size SendInput expects.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
