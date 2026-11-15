using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Other apps on this PC (e.g. Daily Note) can show things on the island through the local bridge:
/// a short notice (/app/notify) and a live activity in the compact pill (/app/activity). See docs/app-bridge.md.
/// Everything stays on this PC; the bridge only answers calls that carry the secret token.
/// </summary>
public partial class MainWindow
{
    private sealed class AppLive
    {
        public string App = "", Text = "";
        public double Progress = -1, DurationSec;
        public long EndsAtMs;
        public DateTime Expires;
    }

    private const int MaxAppActivities = 8;
    private readonly Dictionary<string, AppLive> _appLive = [];

    private void InitAppLink()
    {
        _bridge.AppNoticed += n => Dispatcher.BeginInvoke(() => OnAppNotice(n));
        _bridge.AppActivityChanged += a => Dispatcher.BeginInvoke(() => OnAppActivity(a));
    }

    private void OnAppNotice(ClaudeBridge.AppNotice n)
    {
        if (FocusMode.IsOn) return;   // quiet: do not disturb is on
        var title = string.IsNullOrWhiteSpace(n.Title) ? n.App : n.Title;
        Action? click = n.Url == null ? null : () => { SafeShell.OpenUrl(n.Url); };
        WhenNotTyping(() => ShowToast("", title, n.Text, click, (Brush)FindResource("AccentBlue"), ms: 6000));
    }

    private void OnAppActivity(ClaudeBridge.AppActivity a)
    {
        string key = (a.App + ":" + a.Id).ToLowerInvariant();
        if (a.Clear) { _appLive.Remove(key); return; }
        if (!_appLive.ContainsKey(key) && _appLive.Count >= MaxAppActivities) return;   // a runaway sender must not flood the pill

        var live = new AppLive { App = a.App, Text = a.Text, Progress = a.Progress, DurationSec = a.DurationSec, EndsAtMs = a.EndsAtMs };
        live.Expires = a.EndsAtMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(a.EndsAtMs).LocalDateTime.AddSeconds(4)
            : DateTime.Now.AddSeconds(a.TtlSec);
        _appLive[key] = live;
    }

    /// <summary>Called from UpdateLive: what other apps want in the pill now (expired ones are dropped here).</summary>
    private IEnumerable<(string Id, string Text, double Ring)> AppLiveItems()
    {
        if (_appLive.Count == 0) yield break;
        var now = DateTime.Now;
        foreach (var key in _appLive.Where(kv => kv.Value.Expires <= now).Select(kv => kv.Key).ToList()) _appLive.Remove(key);

        foreach (var (key, a) in _appLive)
        {
            string text = a.Text;
            double ring = a.Progress >= 0 ? Math.Clamp(a.Progress, 0, 1) : -1;
            if (a.EndsAtMs > 0)
            {
                var left = DateTimeOffset.FromUnixTimeMilliseconds(a.EndsAtMs).LocalDateTime - now;
                if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                text = (text.Length > 0 ? text + " · " : "") + $"{(int)left.TotalMinutes}:{left.Seconds:00}";
                if (a.DurationSec > 0) ring = Math.Clamp(1 - left.TotalSeconds / a.DurationSec, 0, 1);
            }
            yield return ("app:" + key, Shorten(text, 34), ring);
        }
    }
}
