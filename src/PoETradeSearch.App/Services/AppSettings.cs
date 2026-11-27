using System.IO;
using System.Text.Json;

namespace PoETradeSearch.App.Services;

/// <summary>User settings saved to %LOCALAPPDATA%\PoETradeSearch\settings.json.</summary>
public sealed class AppSettings
{
    public string? League { get; set; }

    /// <summary>"kakao" or "global". Switches automatically to match the language of copied items.</summary>
    public string? Server { get; set; }

    private static string FilePath => Path.Combine(TradeDataCache.CacheDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(TradeDataCache.CacheDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings are a convenience; failing to save must not break the app.
        }
    }
}
