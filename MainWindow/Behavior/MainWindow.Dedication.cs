using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>The personal touches from dedication.json (see Services/Core/Dedication.cs and docs/dedication.md).</summary>
public partial class MainWindow
{
    /// <summary>The personal theme: a warm dark plum with a soft rose accent.</summary>
    private static ThemePreset? DedicationTheme() =>
        Dedication.Current is { ThemeName.Length: > 0 } d ? new ThemePreset("dedication", d.ThemeName, true, "#2A1A22", "#E8A0B4", "none") : null;

    private void InitDedication() => _ = ApplyDedicationAsync(4000);

    /// <summary>The tray tooltip (Windows allows at most 63 characters).</summary>
    private static string TrayText()
    {
        var label = Dedication.Current?.VersionLabel;
        return string.IsNullOrWhiteSpace(label) ? "Atoll" : label.Length > 63 ? label[..63] : label;
    }

    /// <summary>A dedication.json dropped on the island or picked in Settings: take it in and show the greeting right away.</summary>
    private void ImportDedication(string path)
    {
        if (!Dedication.Import(path))
        {
            ShowToast("", L.T("Не получилось"), L.T("Это не похоже на файл послания"));
            return;
        }
        _settings.DedicationSeen.Clear();   // the greeting is shown again for the new words
        _settings.Save();
        BuildThemeGallery();                // her theme appears
        if (_tray != null) _tray.Text = TrayText();
        _ = ApplyDedicationAsync(600);
    }

    private void Dedication_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json", Title = L.T("Загрузить послание…") };
        if (dialog.ShowDialog() == true) ImportDedication(dialog.FileName);
    }

    private async Task ApplyDedicationAsync(int delayMs)
    {
        if (Dedication.Current is not { } d) return;

        // the "about this version" card in Settings
        if (d.Message.Length > 0)
        {
            DedicationMessage.Text = d.Message;
            DedicationSignature.Text = d.Signature;
            DedicationSignature.Visibility = d.Signature.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            DedicationCard.Visibility = Visibility.Visible;
        }

        await Task.Delay(delayMs);   // let the island settle first
        if (_demoMedia != null || _liveDemo) return;

        // a special date wins over the first-run greeting; each is shown once (a date once a year)
        string? key = null, title = null, text = null;
        var now = DateTime.Now;
        if (d.Today(now) is { } day)
        {
            var k = $"{day.Date}/{now.Year}";
            if (!_settings.DedicationSeen.Contains(k)) { key = k; title = day.Title; text = day.Text; }
        }
        if (key == null && !_settings.DedicationSeen.Contains("first")) { key = "first"; title = d.Title; text = d.Toast; }
        if (key == null || string.IsNullOrWhiteSpace(text)) return;

        _settings.DedicationSeen.Add(key);
        _settings.Save();
        ShowToast("", string.IsNullOrWhiteSpace(title) ? d.Title : title, text, () => OpenTab(TabSettings), (Brush)FindResource("Accent"), ms: 15000);
    }
}
