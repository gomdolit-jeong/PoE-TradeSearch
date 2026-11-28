using System.IO;
using System.Text.Json;

namespace PoETradeSearch.App.Services;

/// <summary>User settings saved to %LOCALAPPDATA%\PoETradeSearch\settings.json.</summary>
public sealed class AppSettings
{
    public const uint DefaultHotkeyModifiers = 0x0002; // MOD_CONTROL
    public const uint DefaultHotkeyKey = 0x44;         // D

    public string? League { get; set; }

    /// <summary>"kakao" or "global". Switches automatically to match the language of copied items.</summary>
    public string? Server { get; set; }

    /// <summary>RegisterHotKey modifiers (MOD_ALT/MOD_CONTROL/MOD_SHIFT) and virtual-key code.</summary>
    public uint HotkeyModifiers { get; set; } = DefaultHotkeyModifiers;
    public uint HotkeyKey { get; set; } = DefaultHotkeyKey;

    public bool OnlyInGame { get; set; } = true;

    /// <summary>Close this app once the game process it saw has exited.</summary>
    public bool ExitWithGame { get; set; } = true;

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
