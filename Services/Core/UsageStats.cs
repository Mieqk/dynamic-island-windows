using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DynamicIsland.Services;

/// <summary>
/// How long each app was in the foreground, per day. Only the process name is kept (never window titles or
/// text), only on this PC (%APPDATA%\DynamicIsland\usage.json), and nothing is ever sent anywhere.
/// Time while you are away (no keyboard/mouse for 2 minutes) is not counted. Call <see cref="Tick"/> about once a second.
/// </summary>
public sealed class UsageStats
{
    private const int KeepDays = 90;
    private const double IdleLimitSec = 120;

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "usage.json");

    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private readonly string _file;
    // "yyyy-MM-dd" -> process name -> seconds
    private Dictionary<string, Dictionary<string, double>> _days = [];
    private DateTime _last = DateTime.Now;
    private DateTime _lastSave = DateTime.Now;
    private bool _dirty;

    /// <param name="filePath">Where the totals are kept; the default is %APPDATA%\DynamicIsland\usage.json (tests pass their own).</param>
    public UsageStats(string? filePath = null)
    {
        _file = filePath ?? DefaultPath;
        Load();
    }

    /// <summary>Add the time since the last call to the app that is in front now.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min((now - _last).TotalSeconds, 5);   // a sleep or a stall must not count as use
        _last = now;
        if (dt <= 0 || IdleSeconds() > IdleLimitSec) return;

        var app = ForegroundApp();
        if (app == null) return;

        Add(app, dt, now);
        if ((now - _lastSave).TotalSeconds >= 30) Save();
    }

    /// <summary>Count <paramref name="seconds"/> for an app on the day of <paramref name="when"/>.</summary>
    internal void Add(string app, double seconds, DateTime when)
    {
        if (seconds <= 0 || string.IsNullOrEmpty(app)) return;
        string key = DayKey(when);
        if (!_days.TryGetValue(key, out var day)) _days[key] = day = [];
        day[app] = day.GetValueOrDefault(app) + seconds;
        _dirty = true;
    }

    /// <summary>Apps for a day (default: today), the longest first.</summary>
    public IReadOnlyList<(string App, TimeSpan Time)> Top(int count = 8, DateTime? day = null)
    {
        if (!_days.TryGetValue(DayKey(day ?? DateTime.Now), out var d)) return [];
        return d.OrderByDescending(kv => kv.Value).Take(count)
                .Select(kv => (kv.Key, TimeSpan.FromSeconds(kv.Value))).ToList();
    }

    public TimeSpan Total(DateTime? day = null) =>
        _days.TryGetValue(DayKey(day ?? DateTime.Now), out var d)
            ? TimeSpan.FromSeconds(d.Values.Sum()) : TimeSpan.Zero;

    /// <summary>Forget everything (the "reset" button).</summary>
    public void Reset()
    {
        _days.Clear();
        _dirty = false;
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    public void Save() => Save(DateTime.Now);

    /// <summary>Write the totals (days older than 90 days before <paramref name="now"/> are dropped).</summary>
    internal void Save(DateTime now)
    {
        _lastSave = DateTime.Now;
        if (!_dirty) return;
        try
        {
            string oldest = DayKey(now.AddDays(-KeepDays));
            foreach (var k in _days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList()) _days.Remove(k);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_days));
            File.Move(tmp, _file, true);
            _dirty = false;
        }
        catch { /* statistics are not worth a crash */ }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_file))
                _days = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(File.ReadAllText(_file)) ?? [];
        }
        catch { _days = []; }
    }

    private static string? ForegroundApp()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return null; }   // the process closed or is protected
    }

    private static double IdleSeconds()
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref li)) return 0;
        return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
