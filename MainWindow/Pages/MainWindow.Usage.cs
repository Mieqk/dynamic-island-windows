using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The Statistics tab: a day (total, when, by category, by hour, apps) and a week (seven days, average, the week
/// before). The counting itself is in Services/Core/UsageStats.cs; the data stays on this PC, see PRIVACY.md.
/// </summary>
public partial class MainWindow
{
    private readonly UsageStats _usage = new();
    private bool _statsWeek;

    /// <summary>How each category is called and coloured.</summary>
    private static readonly Dictionary<UsageCategory, (string Name, Color Color)> CategoryLook = new()
    {
        [UsageCategory.Work] = ("Работа", Color.FromRgb(0x6F, 0xC3, 0xA1)),
        [UsageCategory.Communication] = ("Общение", Color.FromRgb(0x5A, 0xA9, 0xE6)),
        [UsageCategory.Browser] = ("Интернет", Color.FromRgb(0xF2, 0xB5, 0x44)),
        [UsageCategory.Creative] = ("Творчество", Color.FromRgb(0xC7, 0x92, 0xEA)),
        [UsageCategory.Entertainment] = ("Развлечения", Color.FromRgb(0xFF, 0x8A, 0x65)),
        [UsageCategory.Other] = ("Другое", Color.FromRgb(0x8E, 0x8E, 0x93)),
    };

    private static SolidColorBrush CategoryBrush(UsageCategory c) => new(CategoryLook[c].Color);

    /// <summary>Every second (from SlowTick).</summary>
    private void TrackUsage()
    {
        if (!_settings.UsageStats || _liveDemo || _demoMedia != null) return;
        // watching a film full screen: no keyboard or mouse for a long time, but it is still time at the screen
        bool watching = _lastMedia?.IsPlaying == true && _hwnd != IntPtr.Zero && Native.IsFullscreenAppActive(_hwnd);
        _usage.Tick(watching);
        // the open tab refreshes itself now and then (not while the cursor is over it: a click must land where it was aimed)
        if (_mode == Mode.Expanded && TabStats.IsChecked == true && _slowCount % 10 == 0 && !StatsBody.IsMouseOver) RenderUsage();
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? L.F("{0} ч {1} мин", (int)t.TotalHours, t.Minutes) : t.TotalMinutes >= 1 ? L.F("{0} мин", t.Minutes) : L.F("{0} с", (int)t.TotalSeconds);

    /// <summary>Short, for chart labels: "3,2 ч" or "45 мин".</summary>
    private static string FmtShort(TimeSpan t) =>
        t.TotalHours >= 1 ? L.F("{0} ч", t.TotalHours.ToString("0.#", L.Culture)) : L.F("{0} мин", (int)t.TotalMinutes);

    private static string Clock(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";

    // ------------------------------------------------------------------ small building blocks

    private TextBlock StatsText(string text, string style = "Muted", double top = 0) =>
        new() { Text = text, Style = (Style)FindResource(style), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };

    /// <summary>One bar split by category, widest share first in the fixed category order.</summary>
    private static Grid CategoryBar(IReadOnlyDictionary<UsageCategory, TimeSpan> by)
    {
        var grid = new Grid { Height = 8, Margin = new Thickness(0, 10, 0, 8) };
        int col = 0;
        foreach (var c in Enum.GetValues<UsageCategory>())
        {
            if (!by.TryGetValue(c, out var t) || t <= TimeSpan.Zero) continue;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(t.TotalSeconds, GridUnitType.Star) });
            var part = new Border
            {
                Background = CategoryBrush(c), CornerRadius = new CornerRadius(3), Margin = new Thickness(col == 0 ? 0 : 2, 0, 0, 0),
                ToolTip = L.T(CategoryLook[c].Name) + " · " + Fmt(t),
            };
            Grid.SetColumn(part, col++);
            grid.Children.Add(part);
        }
        return grid;
    }

    private WrapPanel CategoryLegend(IReadOnlyDictionary<UsageCategory, TimeSpan> by)
    {
        var panel = new WrapPanel();
        foreach (var c in Enum.GetValues<UsageCategory>())
        {
            if (!by.TryGetValue(c, out var t) || t <= TimeSpan.Zero) continue;
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 4) };
            item.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = CategoryBrush(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var label = new TextBlock { Text = L.T(CategoryLook[c].Name) + " " + Fmt(t), FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
            item.Children.Add(label);
            panel.Children.Add(item);
        }
        return panel;
    }

    /// <summary>24 little columns: how much of each hour was spent at the computer.</summary>
    private FrameworkElement HoursChart(IReadOnlyList<TimeSpan> hours)
    {
        var bars = new Grid { Height = 52 };
        var labels = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        for (int h = 0; h < 24; h++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            labels.ColumnDefinitions.Add(new ColumnDefinition());
            double share = Math.Clamp(hours[h].TotalSeconds / 3600, 0, 1);
            var bar = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Height = share > 0 ? Math.Max(3, 50 * share) : 2,
                Margin = new Thickness(1.5, 0, 1.5, 0), CornerRadius = new CornerRadius(2),
                ToolTip = $"{h:00}:00 · {Fmt(hours[h])}",
            };
            bar.SetResourceReference(Border.BackgroundProperty, share > 0 ? "Accent" : "TrackBg");
            Grid.SetColumn(bar, h);
            bars.Children.Add(bar);

            if (h % 6 == 0)
            {
                var mark = new TextBlock { Text = h.ToString(), FontSize = 10 };
                mark.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
                Grid.SetColumn(mark, h);
                Grid.SetColumnSpan(mark, 3);
                labels.Children.Add(mark);
            }
        }
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(bars);
        stack.Children.Add(labels);
        return stack;
    }

    /// <summary>A row of the app list: category dot (click to change), name, bar, time.</summary>
    private Grid AppRow(AppTime app, double longestSeconds)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });

        var dot = new Border
        {
            Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = CategoryBrush(app.Category), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            ToolTip = L.F("{0}: нажмите, чтобы сменить категорию", L.T(CategoryLook[app.Category].Name)),
        };
        dot.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!_justDragged) CycleCategory(app.Process, app.Category);
        };

        var name = new TextBlock { Text = L.T(app.Name), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

        double share = longestSeconds > 0 ? Math.Clamp(app.Time.TotalSeconds / longestSeconds, 0.03, 1) : 0.03;
        var track = new Grid { Height = 5, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var back = new Border { CornerRadius = new CornerRadius(2.5) };
        back.SetResourceReference(Border.BackgroundProperty, "TrackBg");
        track.Children.Add(back);
        var fill = new Grid();
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
        fill.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = CategoryBrush(app.Category) });
        track.Children.Add(fill);

        var time = new TextBlock { Text = Fmt(app.Time), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");

        Grid.SetColumn(dot, 0); Grid.SetColumn(name, 1); Grid.SetColumn(track, 2); Grid.SetColumn(time, 3);
        row.Children.Add(dot); row.Children.Add(name); row.Children.Add(track); row.Children.Add(time);
        return row;
    }

    /// <summary>The dot of an app was clicked: move the app to the next category (the built-in one needs no entry).</summary>
    private void CycleCategory(string process, UsageCategory current)
    {
        var all = Enum.GetValues<UsageCategory>();
        var next = all[(Array.IndexOf(all, current) + 1) % all.Length];
        foreach (var key in _settings.UsageCategories.Keys.Where(k => string.Equals(k, process, StringComparison.OrdinalIgnoreCase)).ToList())
            _settings.UsageCategories.Remove(key);
        if (next != UsageCatalog.Describe(process).Category) _settings.UsageCategories[process] = UsageCatalog.IdOf(next);
        _settings.Save();
        RenderUsage();
    }

    // ------------------------------------------------------------------ the tab

    private void StatsRange_Checked(object sender, RoutedEventArgs e)
    {
        if (StatsBody == null) return;   // still loading the window
        _statsWeek = StatsWeekTab.IsChecked == true;
        RenderUsage();
    }

    private void RenderUsage()
    {
        StatsToggle.Content = _settings.UsageStats ? L.T("Выключить учёт") : L.T("Включить учёт");
        StatsBody.Children.Clear();
        bool any = _statsWeek ? RenderUsageWeek() : RenderUsageDay();
        StatsEmpty.Text = !_settings.UsageStats && !any ? L.T("Учёт выключен.")
            : any ? "" : L.T("Пока нет данных. Поработайте за компьютером, и здесь появится время по программам.");
        StatsEmpty.Visibility = StatsEmpty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Remeasure();
    }

    /// <summary>Today. Returns false when there is nothing to show yet.</summary>
    private bool RenderUsageDay()
    {
        var day = _usage.Summary(DateTime.Now, _settings.UsageCategories);
        StatsTotal.Text = L.F("Всего: {0}", Fmt(day.Total));
        if (day.Total < TimeSpan.FromSeconds(1)) return false;

        var facts = new List<string>();
        if (day.First is { } first && day.Last is { } last) facts.Add(L.F("с {0} до {1}", Clock(first), Clock(last)));
        if (day.LongestStretch.TotalMinutes >= 1) facts.Add(L.F("дольше всего без перерыва: {0}", Fmt(day.LongestStretch)));
        if (facts.Count > 0) StatsBody.Children.Add(StatsText(string.Join(" · ", facts)));

        StatsBody.Children.Add(CategoryBar(day.ByCategory));
        StatsBody.Children.Add(CategoryLegend(day.ByCategory));

        StatsBody.Children.Add(StatsText(L.T("ПО ЧАСАМ"), "Caption", 10));
        StatsBody.Children.Add(HoursChart(day.Hours));

        StatsBody.Children.Add(StatsText(L.T("ПРИЛОЖЕНИЯ"), "Caption", 12));
        var apps = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        double longest = day.Apps.Count > 0 ? day.Apps[0].Time.TotalSeconds : 0;
        foreach (var app in day.Apps.Take(6)) apps.Children.Add(AppRow(app, longest));
        StatsBody.Children.Add(apps);
        return true;
    }

    /// <summary>The last seven days. Returns false when there is nothing to show yet.</summary>
    private bool RenderUsageWeek()
    {
        var week = _usage.Week(DateTime.Now, _settings.UsageCategories);
        StatsTotal.Text = L.F("Всего: {0}", Fmt(week.Total));
        if (week.Total < TimeSpan.FromSeconds(1)) return false;

        // seven columns, today on the right
        double max = Math.Max(1, week.Days.Max(d => d.Total.TotalSeconds));
        var chart = new Grid { Height = 104, Margin = new Thickness(0, 2, 0, 6) };
        for (int i = 0; i < week.Days.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var (date, total) = week.Days[i];
            bool today = date.Date == DateTime.Today;

            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            var value = new TextBlock { Text = total > TimeSpan.Zero ? FmtShort(total) : "", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            value.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
            var bar = new Border
            {
                Width = 28, Height = total > TimeSpan.Zero ? Math.Max(3, 62 * total.TotalSeconds / max) : 2,
                CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 3, 0, 4), Opacity = today ? 1 : 0.55,
                ToolTip = date.ToString("d MMMM", L.Culture) + " · " + Fmt(total),
            };
            bar.SetResourceReference(Border.BackgroundProperty, total > TimeSpan.Zero ? "Accent" : "TrackBg");
            var label = new TextBlock { Text = date.ToString("ddd", L.Culture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, today ? "Fg" : "Fg2");
            column.Children.Add(value);
            column.Children.Add(bar);
            column.Children.Add(label);
            Grid.SetColumn(column, i);
            chart.Children.Add(column);
        }
        StatsBody.Children.Add(chart);

        var facts = new List<string> { L.F("В среднем {0} в день", Fmt(week.DailyAverage)) };
        if (week.PreviousTotal > TimeSpan.Zero)
        {
            int pct = (int)Math.Round((week.Total.TotalSeconds - week.PreviousTotal.TotalSeconds) / week.PreviousTotal.TotalSeconds * 100);
            if (pct != 0)
                facts.Add(pct > 0 ? L.F("на {0} % больше, чем на прошлой неделе", pct) : L.F("на {0} % меньше, чем на прошлой неделе", -pct));
        }
        if (week.BusiestDay is { } busiest) facts.Add(L.F("больше всего: {0}", busiest.ToString("dddd", L.Culture)));
        StatsBody.Children.Add(StatsText(string.Join(" · ", facts)));

        StatsBody.Children.Add(CategoryBar(week.ByCategory));
        StatsBody.Children.Add(CategoryLegend(week.ByCategory));
        return true;
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
