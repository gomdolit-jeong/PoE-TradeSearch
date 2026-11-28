using System.Diagnostics;

namespace PoETradeSearch.App.Services;

/// <summary>
/// Development trace only: messages go to the debugger output (Visual Studio "Output" window).
/// Nothing is written to disk.
/// </summary>
public static class Log
{
    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message) =>
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {level} {message}");
}
