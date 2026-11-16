using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Services;

/// <summary>One day of use, as it is kept in usage.json.</summary>
public sealed class UsageDay
{
    /// <summary>Process name → seconds in the foreground.</summary>
    [JsonPropertyName("apps")] public Dictionary<string, double> Apps { get; set; } = [];
    /// <summary>Seconds of use in each hour of the day (24 numbers).</summary>
    [JsonPropertyName("hours")] public double[] Hours { get; set; } = new double[24];
    /// <summary>The longest stretch at the computer without a break, seconds.</summary>
    [JsonPropertyName("longest")] public double Longest { get; set; }
    /// <summary>First and last minute of the day with activity (minutes since midnight, -1: none).</summary>
    [JsonPropertyName("first")] public int First { get; set; } = -1;
    [JsonPropertyName("last")] public int Last { get; set; } = -1;
}

public sealed record AppTime(string Process, string Name, UsageCategory Category, TimeSpan Time);

/// <summary>A day at a glance.</summary>
public sealed record DaySummary(
    DateTime Day, TimeSpan Total, IReadOnlyList<AppTime> Apps, IReadOnlyDictionary<UsageCategory, TimeSpan> ByCategory,
    IReadOnlyList<TimeSpan> Hours, TimeSpan LongestStretch, TimeSpan? First, TimeSpan? Last);

/// <summary>Seven days ending with a given day, and the seven days before them for comparison.</summary>
public sealed record WeekSummary(
    IReadOnlyList<(DateTime Day, TimeSpan Total)> Days, TimeSpan Total, TimeSpan PreviousTotal, TimeSpan DailyAverage,
    IReadOnlyDictionary<UsageCategory, TimeSpan> ByCategory, DateTime? BusiestDay);

/// <summary>
/// How long each app was in the foreground, per day, plus when during the day and for how long without a break.
/// Only the process name is kept (never window titles or text), only on this PC (%APPDATA%\DynamicIsland\usage.json),
/// and nothing is ever sent anywhere. Time while you are away (no keyboard/mouse for 2 minutes) is not counted,
/// unless you are watching something full screen. Call <see cref="Tick"/> about once a second.
/// </summary>
public sealed class UsageStats
{
    private const int KeepDays = 90;
    /// <summary>No input for this long: you are away.</summary>
    internal const double IdleLimitSec = 120;
    /// <summary>A pause this long is a break: the "without a break" stretch starts again.</summary>
    internal const double BreakGapSec = 180;
    /// <summary>The most one observation can add (a stall or a sleep must not count as use).</summary>
    internal const double MaxStepSec = 5;

    private sealed class UsageFile
    {
        [JsonPropertyName("v")] public int Version { get; set; } = 2;
        [JsonPropertyName("days")] public Dictionary<string, UsageDay> Days { get; set; } = [];
    }

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "usage.json");

    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private readonly string _file;
    private Dictionary<string, UsageDay> _days = [];
    private DateTime _lastSave = DateTime.Now;
    private bool _dirty;

    private bool _seenOnce;
    private DateTime _lastSeen;        // the previous observation
    private DateTime? _stretchStart;   // since when you have been at the computer without a break
    private DateTime? _awaySince;      // since when you have been away (null while you are here)
    private DateTime? _reminded;       // when "take a break" was last due

    /// <param name="filePath">Where the totals are kept; the default is %APPDATA%\DynamicIsland\usage.json (tests pass their own).</param>
    public UsageStats(string? filePath = null)
    {
        _file = filePath ?? DefaultPath;
        Load();
    }

    // ------------------------------------------------------------------ counting

    /// <summary>Look at what is in front now. <paramref name="watching"/>: something plays full screen, so no input still counts.</summary>
    public void Tick(bool watching = false)
    {
        var now = DateTime.Now;
        Observe(now, ForegroundApp(), IdleSeconds(), watching);
        if ((now - _lastSave).TotalSeconds >= 30) Save();
    }

    /// <summary>One observation: at <paramref name="now"/> the app in front is <paramref name="app"/> and the last input was <paramref name="idleSeconds"/> ago.</summary>
    internal void Observe(DateTime now, string? app, double idleSeconds, bool watching = false)
    {
        double gap = _seenOnce ? Math.Max(0, (now - _lastSeen).TotalSeconds) : 0;
        _seenOnce = true;
        _lastSeen = now;

        // no observations for a while (the PC slept, Atoll was closed): that was time away
        if (gap >= BreakGapSec) _awaySince ??= now.AddSeconds(-gap);

        bool here = !string.IsNullOrEmpty(app) && (idleSeconds <= IdleLimitSec || watching);
        if (!here)
        {
            // away since the last input (or since now, when there is simply nothing in front)
            _awaySince ??= idleSeconds > IdleLimitSec ? now.AddSeconds(-idleSeconds) : now;
            return;
        }

        if (_awaySince is { } away && (now - away).TotalSeconds >= BreakGapSec) _stretchStart = null;   // that was a break
        _awaySince = null;
        _stretchStart ??= now;

        var day = DayOf(now);
        double step = Math.Min(gap, MaxStepSec);
        if (step > 0)
        {
            day.Apps[app!] = day.Apps.GetValueOrDefault(app!) + step;
            day.Hours[now.Hour] += step;
        }
        int minute = now.Hour * 60 + now.Minute;
        if (day.First < 0 || minute < day.First) day.First = minute;
        if (minute > day.Last) day.Last = minute;
        // the stretch reaches to the last sign of you (the last input, or now while something plays full screen);
        // over midnight each day gets its own part
        var until = watching ? now : now.AddSeconds(-Math.Max(0, idleSeconds));
        var since = _stretchStart.Value < now.Date ? now.Date : _stretchStart.Value;
        if (until > since) day.Longest = Math.Max(day.Longest, (until - since).TotalSeconds);
        _dirty = true;
    }

    private UsageDay DayOf(DateTime when)
    {
        string key = DayKey(when);
        if (!_days.TryGetValue(key, out var day)) _days[key] = day = new UsageDay();
        return day;
    }

    /// <summary>Count <paramref name="seconds"/> for an app on the day (and hour) of <paramref name="when"/>.</summary>
    internal void Add(string app, double seconds, DateTime when)
    {
        if (seconds <= 0 || string.IsNullOrEmpty(app)) return;
        var day = DayOf(when);
        day.Apps[app] = day.Apps.GetValueOrDefault(app) + seconds;
        day.Hours[when.Hour] += seconds;
        _dirty = true;
    }

    // ------------------------------------------------------------------ breaks

    /// <summary>How long you have been at the computer without a break (a pause of 3 minutes or more is a break).</summary>
    public TimeSpan CurrentStretch(DateTime now)
    {
        if (_stretchStart is not { } start) return TimeSpan.Zero;
        if (_awaySince is { } away && (now - away).TotalSeconds >= BreakGapSec) return TimeSpan.Zero;
        return now > start ? now - start : TimeSpan.Zero;
    }

    /// <summary>
    /// True once each time the current stretch grows by <paramref name="every"/>: time to suggest a break.
    /// Ask only when the suggestion can be shown: an unanswered "due" simply stays due.
    /// </summary>
    public bool BreakDue(DateTime now, TimeSpan every)
    {
        if (every <= TimeSpan.Zero || _stretchStart is not { } start || _awaySince != null) return false;
        var from = _reminded is { } r && r > start ? r : start;
        if (now - from < every) return false;
        _reminded = now;
        return true;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>Apps for a day (default: today), the longest first.</summary>
    public IReadOnlyList<(string App, TimeSpan Time)> Top(int count = 8, DateTime? day = null)
    {
        if (!_days.TryGetValue(DayKey(day ?? DateTime.Now), out var d)) return [];
        return d.Apps.OrderByDescending(kv => kv.Value).Take(count)
                .Select(kv => (kv.Key, TimeSpan.FromSeconds(kv.Value))).ToList();
    }

    public TimeSpan Total(DateTime? day = null) =>
        _days.TryGetValue(DayKey(day ?? DateTime.Now), out var d)
            ? TimeSpan.FromSeconds(d.Apps.Values.Sum()) : TimeSpan.Zero;

    /// <summary>A day at a glance: apps with friendly names and categories, the hours, the longest stretch.</summary>
    public DaySummary Summary(DateTime day, IReadOnlyDictionary<string, string>? categoryOverrides = null)
    {
        _days.TryGetValue(DayKey(day), out var d);
        d ??= new UsageDay();
        var apps = d.Apps.OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                var (name, category) = UsageCatalog.Describe(kv.Key, categoryOverrides);
                return new AppTime(kv.Key, name, category, TimeSpan.FromSeconds(kv.Value));
            }).ToList();
        return new DaySummary(
            day.Date, TimeSpan.FromSeconds(d.Apps.Values.Sum()), apps, ByCategory(apps),
            d.Hours.Select(h => TimeSpan.FromSeconds(h)).ToList(), TimeSpan.FromSeconds(d.Longest),
            d.First >= 0 ? TimeSpan.FromMinutes(d.First) : null, d.Last >= 0 ? TimeSpan.FromMinutes(d.Last) : null);
    }

    /// <summary>The seven days ending with <paramref name="lastDay"/> (oldest first) and the week before them.</summary>
    public WeekSummary Week(DateTime lastDay, IReadOnlyDictionary<string, string>? categoryOverrides = null)
    {
        var days = Enumerable.Range(0, 7).Select(i => lastDay.Date.AddDays(i - 6)).ToList();
        var summaries = days.Select(d => Summary(d, categoryOverrides)).ToList();
        var totals = summaries.Select(s => (s.Day, s.Total)).ToList();
        var total = TimeSpan.FromSeconds(totals.Sum(t => t.Total.TotalSeconds));
        var previous = TimeSpan.FromSeconds(Enumerable.Range(7, 7).Sum(i => Total(lastDay.Date.AddDays(-i)).TotalSeconds));
        int used = totals.Count(t => t.Total > TimeSpan.Zero);
        var busiest = totals.MaxBy(t => t.Total);
        return new WeekSummary(
            totals, total, previous, used > 0 ? total / used : TimeSpan.Zero,
            ByCategory(summaries.SelectMany(s => s.Apps)), busiest.Total > TimeSpan.Zero ? busiest.Day : null);
    }

    private static Dictionary<UsageCategory, TimeSpan> ByCategory(IEnumerable<AppTime> apps) =>
        apps.GroupBy(a => a.Category).ToDictionary(g => g.Key, g => TimeSpan.FromSeconds(g.Sum(a => a.Time.TotalSeconds)));

    // ------------------------------------------------------------------ keeping

    /// <summary>Forget everything (the "reset" button).</summary>
    public void Reset()
    {
        _days.Clear();
        _dirty = false;
        _stretchStart = null;
        _awaySince = null;
        _reminded = null;
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
            File.WriteAllText(tmp, JsonSerializer.Serialize(new UsageFile { Days = _days }));
            File.Move(tmp, _file, true);
            _dirty = false;
        }
        catch { /* statistics are not worth a crash */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var text = File.ReadAllText(_file);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("v", out _))
                _days = JsonSerializer.Deserialize<UsageFile>(text)?.Days ?? [];
            else   // the first format: day → app → seconds
                _days = (JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(text) ?? [])
                    .ToDictionary(kv => kv.Key, kv => new UsageDay { Apps = kv.Value ?? [] });

            foreach (var (key, day) in _days.ToList())
            {
                if (day == null) { _days.Remove(key); continue; }
                day.Apps ??= [];
                if (day.Hours is not { Length: 24 }) day.Hours = new double[24];
            }
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
