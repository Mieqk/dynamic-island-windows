using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The browser extension (extension/): saves selected text into the island's notes and shows the result of a
/// LanguageTool check in the pill. Off until switched on in Settings › Behavior; see ExtensionBridge and docs/extension-bridge.md.
/// </summary>
public partial class MainWindow
{
    private void InitExtension()
    {
        var ext = _bridge.Extension;
        ext.IsEnabled = () => _settings.ExtensionEnabled;
        ext.NoteReceived += n => Dispatcher.BeginInvoke(() => OnExtNote(n));
        ext.CheckReceived += c => Dispatcher.BeginInvoke(() => OnExtCheck(c));
    }

    private void OnExtNote(ExtensionBridge.ExtNote n)
    {
        var text = n.Text;
        var source = string.Join(" · ", new[] { n.Title, n.Url }.Where(s => !string.IsNullOrEmpty(s)));
        if (source.Length > 0) text += "\n— " + source;
        NotesStore.Notes.Insert(0, new Note { Text = text });
        NotesStore.Save();
        if (_mode == Mode.Expanded && TabNotes.IsChecked == true) RenderNotes();
        ShowToast("", L.T("Сохранено в заметки"), Shorten(n.Text.Replace('\n', ' '), 60), () => OpenTab(TabNotes),
            (Brush)FindResource("Accent"), ms: 3500);
    }

    private void OnExtCheck(ExtensionBridge.ExtCheck c)
    {
        var text = c.Issues == 0 ? L.T("LanguageTool · ошибок нет") : L.F("LanguageTool · замечаний: {0}", c.Issues);
        // shown like any other app's live activity for a short while (and hidden with "Другие приложения")
        OnAppActivity(new ClaudeBridge.AppActivity("LanguageTool", "check", text, -1, 0, 0, 25, false));
    }

    private void ExtSwitch_Click(object sender, RoutedEventArgs e)
    {
        _settings.ExtensionEnabled = ExtSwitch.IsChecked == true;
        _settings.Save();
    }

    private void ExtCopyKey_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ExtensionBridge.Token()); }
        catch { return; }
        ShowToast("", L.T("Ключ скопирован"), L.T("Вставьте его в настройки расширения"), ms: 3000);
    }

    private void ExtNewKey_Click(object sender, RoutedEventArgs e)
    {
        ExtensionBridge.RotateToken();
        ShowToast("", L.T("Новый ключ создан"), L.T("Старый больше не работает: вставьте новый в расширение"), ms: 3500);
    }
}
