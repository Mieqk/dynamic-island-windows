using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The Tasks tab: a short list for today with ticks, reminders ("позвонить в 18:30") and, for a little while after
/// a change, the progress in the pill. The list itself is Services/Core/TasksStore.cs (kept on this PC).
/// </summary>
public partial class MainWindow
{
    private readonly TasksStore _tasks = new();
    private DateTime _tasksFlashUntil;

    /// <summary>The app whose tasks can be mirrored here (see docs/app-bridge.md).</summary>
    private const string TasksApp = "Daily Note";

    private void RenderTasks()
    {
        var today = _tasks.Today(DateTime.Now);
        int done = today.Count(t => t.Done);
        TasksCount.Text = today.Count > 0 ? L.F("{0} из {1}", done, today.Count) : "";
        TasksBar.ScaleX = today.Count > 0 ? (double)done / today.Count : 0;

        TasksList.Children.Clear();
        foreach (var task in today) TasksList.Children.Add(TaskRow(task));
        TasksEmpty.Visibility = today.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TasksClearDone.Visibility = today.Any(t => t.Done && !t.IsLinked) ? Visibility.Visible : Visibility.Collapsed;
        Remeasure();
    }

    /// <summary>A row: the tick, the text, the reminder time, where it comes from, delete.</summary>
    private FrameworkElement TaskRow(TaskItem task)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tick = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
        };
        if (task.Done)
        {
            tick.SetResourceReference(Border.BackgroundProperty, "Accent");
            tick.SetResourceReference(Border.BorderBrushProperty, "Accent");
            var mark = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            mark.SetResourceReference(TextBlock.ForegroundProperty, "PlayFg");
            tick.Child = mark;
        }
        else
        {
            tick.Background = Brushes.Transparent;   // the whole circle takes the click, not only its outline
            tick.SetResourceReference(Border.BorderBrushProperty, "Fg3");
        }

        var text = new TextBlock { Text = task.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        if (task.Done)
        {
            text.TextDecorations = TextDecorations.Strikethrough;
            text.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
        }

        void Toggle(object _, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_justDragged) return;
            _tasks.Toggle(task.Id, DateTime.Now);
            TasksChanged();
        }
        tick.MouseLeftButtonUp += Toggle;
        text.MouseLeftButtonUp += Toggle;

        // "18:30 · Daily Note"
        var notes = new List<string>();
        if (task.RemindAt >= 0) notes.Add($"{task.RemindAt / 60:00}:{task.RemindAt % 60:00}");
        if (task.IsLinked) notes.Add(task.Source);
        var note = new TextBlock { Text = string.Join(" · ", notes), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");

        Grid.SetColumn(tick, 0); Grid.SetColumn(text, 1); Grid.SetColumn(note, 2);
        row.Children.Add(tick); row.Children.Add(text); row.Children.Add(note);

        if (!task.IsLinked)   // a linked task is deleted in its own app
        {
            var del = new Button { Style = (Style)FindResource("IconButton"), Content = "", Width = 26, Height = 26, FontSize = 11, ToolTip = L.T("Удалить"), VerticalAlignment = VerticalAlignment.Top };
            del.Click += (_, _) => { _tasks.Remove(task.Id); TasksChanged(); };
            Grid.SetColumn(del, 3);
            row.Children.Add(del);
        }
        return new Border { Padding = new Thickness(2, 5, 0, 5), Child = row };
    }

    /// <summary>Something changed in the list: redraw the tab and show the progress in the pill for a moment.</summary>
    private void TasksChanged()
    {
        _tasksFlashUntil = DateTime.Now.AddSeconds(20);
        if (TabTasks.IsChecked == true) RenderTasks();
        UpdateLive();
    }

    private void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var now = DateTime.Now;
        // while the linked app is in touch, new tasks go to it (and so appear in both places)
        if (_tasks.Add(NewTaskBox.Text, now, _tasks.IsLinked(TasksApp, now) ? TasksApp : "") == null) return;
        NewTaskBox.Clear();
        TasksChanged();
    }

    private void TasksClearDone_Click(object sender, RoutedEventArgs e)
    {
        _tasks.ClearDone(DateTime.Now);
        TasksChanged();
    }

    /// <summary>Every few seconds (from SlowTick): tasks whose time has come.</summary>
    private void CheckTaskReminders()
    {
        if (!Shows("tasks") || _liveDemo || _demoMedia != null) return;
        foreach (var task in _tasks.DueReminders(DateTime.Now))
        {
            var t = task;
            WhenNotTyping(() => ShowToast("", L.T("Задача"), t.Text, () => OpenTab(TabTasks), (Brush)FindResource("Accent"), ms: 8000));
        }
    }

    /// <summary>For the pill: the progress, shown for a short while after a change.</summary>
    private (int Done, int Total)? TasksForPill()
    {
        if (DateTime.Now >= _tasksFlashUntil || !Shows("tasks")) return null;
        var progress = _tasks.Progress(DateTime.Now);
        return progress.Total > 0 ? progress : null;
    }
}
