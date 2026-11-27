using System.IO;
using System.Text;

namespace PoETradeSearch.App.Services;

/// <summary>
/// Simple file log in %LOCALAPPDATA%\PoETradeSearch\logs, one file per day, so problems on another PC
/// can be diagnosed from the "진단 정보 복사" button or the log file.
/// </summary>
public static class Log
{
    private const int MemoryLines = 400;
    private static readonly object Gate = new();
    private static readonly LinkedList<string> Recent = new();

    public static string Directory { get; } = Path.Combine(TradeDataCache.CacheDirectory, "logs");

    public static string CurrentFile => Path.Combine(Directory, $"log-{DateTime.Now:yyyyMMdd}.txt");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    /// <summary>The most recent lines, oldest first.</summary>
    public static string RecentText()
    {
        lock (Gate)
            return string.Join(Environment.NewLine, Recent);
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";
        lock (Gate)
        {
            Recent.AddLast(line);
            while (Recent.Count > MemoryLines)
                Recent.RemoveFirst();

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(CurrentFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never break the app.
            }
        }
    }
}
