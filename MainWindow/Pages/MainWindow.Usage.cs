using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Usage statistics (time per app today). The data stays on this PC, see PRIVACY.md.</summary>
public partial class MainWindow
{
    private readonly UsageStats _usage = new();

    /// <summary>Every second (from SlowTick).</summary>
    private void TrackUsage()
    {
        if (!_settings.UsageStats || _liveDemo || _demoMedia != null) return;
        // watching a film full screen: no keyboard or mouse for a long time, but it is still time at the screen
        bool watching = _lastMedia?.IsPlaying == true && _hwnd != IntPtr.Zero && Native.IsFullscreenAppActive(_hwnd);
        _usage.Tick(watching);
        // the open tab refreshes itself every 5 seconds
        if (_mode == Mode.Expanded && TabStats.IsChecked == true && _slowCount % 5 == 0) RenderUsage();
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? L.F("{0} ч {1} мин", (int)t.TotalHours, t.Minutes) : t.TotalMinutes >= 1 ? L.F("{0} мин", t.Minutes) : L.F("{0} с", (int)t.TotalSeconds);

    /// <summary>Fill the "Статистика" tab: the top apps today, each with a bar relative to the longest.</summary>
    private void RenderUsage()
    {
        StatsToggle.Content = _settings.UsageStats ? L.T("Выключить учёт") : L.T("Включить учёт");
        StatsList.Children.Clear();
        var top = _usage.Top(8);
        StatsTotal.Text = L.F("Всего: {0}", Fmt(_usage.Total()));
        StatsEmpty.Text = !_settings.UsageStats ? L.T("Учёт выключен.") : top.Count == 0 ? L.T("Пока нет данных. Поработайте за компьютером, и здесь появится время по программам.") : "";
        StatsEmpty.Visibility = StatsEmpty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        double max = top.Count > 0 ? top[0].Time.TotalSeconds : 1;
        foreach (var (app, time) in top)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var name = new TextBlock { Text = app, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            double frac = Math.Clamp(time.TotalSeconds / max, 0.03, 1);
            var track = new Grid { Height = 5, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var back = new Border { CornerRadius = new CornerRadius(2.5) };
            back.SetResourceReference(Border.BackgroundProperty, "TrackBg");
            track.Children.Add(back);
            // the bar fills a share of the track: two proportional columns
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac, GridUnitType.Star) });
            var bar = new Border { CornerRadius = new CornerRadius(2.5) };
            bar.SetResourceReference(Border.BackgroundProperty, "Accent");
            fill.Children.Add(bar);
            track.Children.Add(fill);
            var span = new TextBlock { Text = Fmt(time), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            span.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");

            Grid.SetColumn(name, 0); Grid.SetColumn(track, 1); Grid.SetColumn(span, 2);
            row.Children.Add(name); row.Children.Add(track); row.Children.Add(span);
            StatsList.Children.Add(row);
        }
        Remeasure();
    }

    private void StatsToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.UsageStats = !_settings.UsageStats;
        _settings.Save();
        RenderUsage();
    }

    private void ResetUsage_Click(object sender, RoutedEventArgs e)
    {
        _usage.Reset();
        if (TabStats.IsChecked == true) RenderUsage();
        ShowToast("", L.T("Статистика сброшена"), L.T("Время по приложениям начнётся заново"));
    }
}
