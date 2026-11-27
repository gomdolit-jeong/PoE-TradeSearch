using System.Text;
using System.Windows;
using PoETradeSearch.App.Native;
using PoETradeSearch.App.Services;
using PoETradeSearch.Core.Parsing;

namespace PoETradeSearch.App;

public partial class MainWindow : Window
{
    private const uint VK_D = 0x44;

    private GlobalHotkey? _priceCheckHotkey;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RegisterHotkeys();
        Closed += (_, _) => _priceCheckHotkey?.Dispose();
    }

    private void RegisterHotkeys()
    {
        try
        {
            _priceCheckHotkey = new GlobalHotkey(this, NativeMethods.MOD_CONTROL, VK_D, "Ctrl+D");
            _priceCheckHotkey.Pressed += async (_, _) => await OnPriceCheckHotkeyAsync();
            StatusText.Text = $"대기 중 — 게임에서 아이템에 마우스를 올리고 {_priceCheckHotkey.Description}";
            HintText.Text = "단축키를 누르면 Ctrl+C를 한 번 보내 아이템 정보를 복사하고 파싱합니다. " +
                            "(거래소 검색은 다음 단계에서 붙습니다)";
        }
        catch (Exception ex)
        {
            StatusText.Text = "단축키 등록 실패";
            HintText.Text = ex.Message;
        }
    }

    private async Task OnPriceCheckHotkeyAsync()
    {
        if (_busy)
            return;
        if (OnlyInGameCheck.IsChecked == true && !ItemClipboardReader.IsGameInForeground())
            return;

        _busy = true;
        try
        {
            var text = await ItemClipboardReader.CopyItemUnderCursorAsync(TimeSpan.FromMilliseconds(500));
            if (text is null)
            {
                StatusText.Text = "아이템을 복사하지 못했습니다 (마우스가 아이템 위에 있나요?)";
                return;
            }
            ShowItem(text);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ParseClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        string? text = null;
        try { text = Clipboard.GetText(); } catch { }

        if (!ItemTextParser.LooksLikeItem(text))
        {
            StatusText.Text = "클립보드에 PoE 아이템 텍스트가 없습니다";
            return;
        }
        ShowItem(text!);
    }

    private void ShowItem(string text)
    {
        try
        {
            var item = ItemTextParser.Parse(text);
            StatusText.Text = $"파싱 완료: {item.Name ?? item.BaseType} ({DateTime.Now:HH:mm:ss})";
            OutputText.Text = Describe(item);
        }
        catch (Exception ex)
        {
            StatusText.Text = "파싱 실패";
            OutputText.Text = ex + Environment.NewLine + Environment.NewLine + text;
        }
    }

    private static string Describe(ParsedItem item)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Class     : {item.ItemClass}");
        sb.AppendLine($"Rarity    : {item.Rarity}");
        if (item.Name is not null) sb.AppendLine($"Name      : {item.Name}");
        sb.AppendLine($"Base      : {item.BaseType}");
        if (item.ItemLevel is not null) sb.AppendLine($"Item Lv   : {item.ItemLevel}");
        if (item.Quality is not null) sb.AppendLine($"Quality   : {item.Quality}%");
        if (item.Sockets is not null) sb.AppendLine($"Sockets   : {item.Sockets}");
        if (item.Influences.Count > 0) sb.AppendLine($"Influence : {string.Join(", ", item.Influences)}");

        var flags = new List<string>();
        if (item.Corrupted) flags.Add("Corrupted");
        if (item.Unidentified) flags.Add("Unidentified");
        if (item.Mirrored) flags.Add("Mirrored");
        if (item.Synthesised) flags.Add("Synthesised");
        if (flags.Count > 0) sb.AppendLine($"Flags     : {string.Join(", ", flags)}");

        foreach (var (key, value) in item.Properties)
            sb.AppendLine($"  {key}: {value}");

        sb.AppendLine();
        sb.AppendLine("Mods:");
        foreach (var mod in item.Mods)
            sb.AppendLine($"  [{mod.Type,-9}] {mod.Template,-50} {string.Join(", ", mod.Values)}");

        return sb.ToString();
    }
}
