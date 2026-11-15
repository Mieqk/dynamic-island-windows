using System.Windows.Controls;

namespace DynamicIsland;

/// <summary>
/// "What the island shows" (Settings › Behavior): the user picks which sources may put something in the pill,
/// open the island by themselves (smart island) or send a notice. Everything is on until switched off.
/// </summary>
public partial class MainWindow
{
    public static readonly (string Id, string Name)[] IslandSources =
    [
        ("music", "Музыка"), ("timer", "Таймер"), ("discord", "Звонок Discord"), ("clip", "Буфер"),
        ("download", "Загрузки"), ("event", "Календарь"), ("game", "Игровой режим"),
        ("focus", "Не беспокоить"), ("messages", "Сообщения"), ("apps", "Другие приложения"),
    ];

    private bool Shows(string source) => _settings.ShowsSource(source);

    /// <summary>The smart island must not open a tab for a source that is switched off.</summary>
    private bool TabAllowed(RadioButton tab) =>
        tab == TabMusic ? Shows("music") : tab == TabTimer ? Shows("timer") : tab == TabClip ? Shows("clip") : true;

    private void BuildSourceChips()
    {
        SourceList.Children.Clear();
        foreach (var (id, name) in IslandSources)
        {
            var box = new CheckBox { Style = (Style)FindResource("Switch"), Content = L.T(name), IsChecked = Shows(id), Tag = id };
            box.Click += (_, _) =>
            {
                var hidden = _settings.HiddenSources;
                if (box.IsChecked == true) hidden.Remove(id);
                else if (!hidden.Contains(id)) hidden.Add(id);
                _settings.Save();
                UpdateLive();   // the pill follows at once
            };
            SourceList.Children.Add(box);
        }
    }
}
