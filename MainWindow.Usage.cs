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
        _usage.Tick();
    }

    private void ResetUsage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _usage.Reset();
        ShowToast("", L.T("Статистика сброшена"), L.T("Время по приложениям начнётся заново"));
    }
}
