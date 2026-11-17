using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

/// <summary>One task of the day.</summary>
public sealed class TaskItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("done")] public bool Done { get; set; }
    /// <summary>The day it belongs to, "yyyy-MM-dd". Unfinished tasks of the island move on to the next day.</summary>
    [JsonPropertyName("day")] public string Day { get; set; } = "";
    /// <summary>Remind at this minute of the day (-1: no reminder).</summary>
    [JsonPropertyName("remindAt")] public int RemindAt { get; set; } = -1;
    [JsonPropertyName("reminded")] public bool Reminded { get; set; }
    /// <summary>"" for a task made on the island; otherwise the app it comes from (e.g. "Daily Note").</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    /// <summary>The task's id in that app.</summary>
    [JsonPropertyName("externalId")] public string ExternalId { get; set; } = "";

    [JsonIgnore] public bool IsLinked => Source.Length > 0;
}

/// <summary>A task as another app sends it.</summary>
public sealed record ExternalTask(string Id, string Text, bool Done);

/// <summary>What the island asks the other app to do: "toggle" (Id, Done) or "add" (Text).</summary>
public sealed record TaskAction(string Type, string Id, string Text, bool Done);

/// <summary>
/// Tasks for today, kept in %APPDATA%\\DynamicIsland\\tasks.json. Tasks made on the island are its own: unfinished ones
/// move on to the next day, finished ones go away at midnight. Tasks of a linked app (Daily Note) are a mirror of that
/// app's list for today: a tick or a new task made here is queued and handed to the app on its next sync.
/// </summary>
public sealed class TasksStore
{
    public const int MaxTasks = 200;
    public const int MaxText = 300;

    private sealed class Pending
    {
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("done")] public bool Done { get; set; }
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        /// <summary>Handed to the app once already: kept (and shown) until the app's next sync, then dropped.</summary>
        [JsonPropertyName("delivered")] public bool Delivered { get; set; }
    }

    private sealed class TasksFile
    {
        [JsonPropertyName("v")] public int Version { get; set; } = 1;
        [JsonPropertyName("tasks")] public List<TaskItem> Tasks { get; set; } = [];
        [JsonPropertyName("pending")] public List<Pending> Pending { get; set; } = [];
    }

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "tasks.json");

    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private readonly string _file;
    private readonly object _lock = new();
    private List<TaskItem> _tasks = [];
    private List<Pending> _pending = [];
    private readonly Dictionary<string, DateTime> _lastSync = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="filePath">Where the tasks are kept (tests pass their own).</param>
    public TasksStore(string? filePath = null)
    {
        _file = filePath ?? DefaultPath;
        Load();
    }

    // ------------------------------------------------------------------ "позвонить в 18:30"

    private static readonly Regex TimeInText = new(
        @"(?:(?:^|\s+)(?:в|о|at|um|a las|às))?\s*(?<!\d)([01]?\d|2[0-3]):([0-5]\d)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"позвонить маме в 18:30" → ("позвонить маме", 1110). A time is "H:MM" only; no time: -1.</summary>
    public static (string Text, int RemindAt) ParseTime(string text)
    {
        text = text.Trim();
        var m = TimeInText.Match(text);
        if (!m.Success) return (text, -1);
        int minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var rest = (text[..m.Index] + text[(m.Index + m.Length)..]).Trim();
        return (rest.Length > 0 ? rest : text, minutes);
    }

    // ------------------------------------------------------------------ today

    /// <summary>Today's tasks: unfinished first, then finished, each in the order they were added.</summary>
    public IReadOnlyList<TaskItem> Today(DateTime now)
    {
        lock (_lock)
        {
            if (RollOver(now)) Save();
            string today = DayKey(now);
            return _tasks.Where(t => t.Day == today).OrderBy(t => t.Done).ToList();
        }
    }

    public (int Done, int Total) Progress(DateTime now)
    {
        var today = Today(now);
        return (today.Count(t => t.Done), today.Count);
    }

    /// <summary>A new day: the island's unfinished tasks move on, its finished ones and yesterday's mirror go away.</summary>
    private bool RollOver(DateTime now)
    {
        string today = DayKey(now);
        bool changed = false;
        foreach (var t in _tasks.ToList())
        {
            if (string.CompareOrdinal(t.Day, today) >= 0) continue;
            changed = true;
            if (t.IsLinked || t.Done) _tasks.Remove(t);
            else { t.Day = today; t.Reminded = false; }
        }
        changed |= _pending.RemoveAll(p => string.CompareOrdinal(p.Day, today) < 0) > 0;
        return changed;
    }

    /// <summary>
    /// Add a task for today. <paramref name="toSource"/>: add it to that linked app instead (it shows here at once and
    /// reaches the app on its next sync). Returns null for empty text or when the list is full.
    /// </summary>
    public TaskItem? Add(string text, DateTime now, string toSource = "")
    {
        lock (_lock)
        {
            RollOver(now);
            text = (text ?? "").Trim();
            if (text.Length > MaxText) text = text[..MaxText];
            if (text.Length == 0 || _tasks.Count >= MaxTasks) return null;

            string today = DayKey(now);
            var (clean, remindAt) = ParseTime(text);
            var task = new TaskItem { Id = Guid.NewGuid().ToString("N"), Day = today, Source = toSource ?? "" };
            if (task.IsLinked)
            {
                // the app keeps the text as typed; the time is only read from it
                task.Text = text;
                task.RemindAt = remindAt;
                _pending.Add(new Pending { Source = task.Source, Type = "add", Id = task.Id, Text = text, Day = today });
            }
            else
            {
                task.Text = clean;
                task.RemindAt = remindAt;
            }
            _tasks.Add(task);
            Save();
            return task;
        }
    }

    /// <summary>Tick or untick. For a linked task the change is queued for its app. False: no such task.</summary>
    public bool Toggle(string id, DateTime now)
    {
        lock (_lock)
        {
            RollOver(now);
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return false;
            task.Done = !task.Done;
            if (task.IsLinked)
            {
                var add = _pending.FirstOrDefault(p => p.Type == "add" && p.Id == task.Id && !p.Delivered);
                if (add != null) add.Done = task.Done;   // not in the app yet: it will be added already ticked
                else if (task.ExternalId.Length > 0)
                {
                    _pending.RemoveAll(p => p.Type == "toggle" && p.Source == task.Source && p.Id == task.ExternalId && !p.Delivered);
                    _pending.Add(new Pending { Source = task.Source, Type = "toggle", Id = task.ExternalId, Done = task.Done, Day = task.Day });
                }
            }
            Save();
            return true;
        }
    }

    /// <summary>Delete a task made on the island (a linked task is deleted in its app). False: not deleted.</summary>
    public bool Remove(string id)
    {
        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null || task.IsLinked) return false;
            _tasks.Remove(task);
            Save();
            return true;
        }
    }

    /// <summary>Remove the island's finished tasks. Returns how many.</summary>
    public int ClearDone(DateTime now)
    {
        lock (_lock)
        {
            RollOver(now);
            int n = _tasks.RemoveAll(t => t.Done && !t.IsLinked);
            if (n > 0) Save();
            return n;
        }
    }

    /// <summary>Unfinished tasks whose reminder time has come (each is returned once).</summary>
    public IReadOnlyList<TaskItem> DueReminders(DateTime now)
    {
        lock (_lock)
        {
            RollOver(now);
            string today = DayKey(now);
            int minute = now.Hour * 60 + now.Minute;
            var due = _tasks.Where(t => t.Day == today && !t.Done && !t.Reminded && t.RemindAt >= 0 && minute >= t.RemindAt).ToList();
            if (due.Count == 0) return due;
            foreach (var t in due) t.Reminded = true;
            Save();
            return due;
        }
    }

    // ------------------------------------------------------------------ a linked app

    /// <summary>Did this app sync within the last two minutes (so new tasks can go to it)?</summary>
    public bool IsLinked(string source, DateTime now)
    {
        lock (_lock) return _lastSync.TryGetValue(source, out var at) && (now - at).TotalSeconds is >= 0 and < 120;
    }

    /// <summary>
    /// The app sends its list for <paramref name="day"/>; the island mirrors it and answers with what was done here
    /// since the last sync (ticks, new tasks). A change stays visible here until the app's next sync brings it back.
    /// </summary>
    public IReadOnlyList<TaskAction> Sync(string source, string day, IReadOnlyList<ExternalTask> external, DateTime now)
    {
        lock (_lock)
        {
            RollOver(now);
            string today = DayKey(now);
            if (string.IsNullOrWhiteSpace(source) || day != today) return [];   // only today's list is mirrored
            _lastSync[source] = now;

            // what the app was told last time is now part of its list (or was refused): forget it
            _pending.RemoveAll(p => p.Source == source && p.Delivered);

            // the mirror: the app's list, keeping what the island knows about each task (its own id, "already reminded")
            var old = _tasks.Where(t => t.Source == source && t.Day == today).ToList();
            _tasks.RemoveAll(t => t.Source == source && t.Day == today);
            foreach (var e in external.Take(MaxTasks))
            {
                if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Text)) continue;
                var known = old.FirstOrDefault(t => t.ExternalId == e.Id);
                var text = e.Text.Length > MaxText ? e.Text[..MaxText] : e.Text;
                _tasks.Add(new TaskItem
                {
                    Id = known?.Id ?? Guid.NewGuid().ToString("N"), ExternalId = e.Id, Source = source, Day = today,
                    Text = text, Done = e.Done, RemindAt = ParseTime(text).RemindAt, Reminded = known?.Reminded ?? false,
                });
            }

            // what was done here and the app does not know yet: keep it on screen and hand it over
            var actions = new List<TaskAction>();
            foreach (var p in _pending.Where(p => p.Source == source).ToList())
            {
                if (p.Type == "toggle")
                {
                    var task = _tasks.FirstOrDefault(t => t.Source == source && t.ExternalId == p.Id);
                    if (task == null) { _pending.Remove(p); continue; }   // the app deleted it meanwhile
                    task.Done = p.Done;
                }
                else if (p.Type == "add")
                {
                    var known = old.FirstOrDefault(t => t.Id == p.Id);
                    _tasks.Add(new TaskItem
                    {
                        Id = p.Id, Source = source, Day = today, Text = p.Text, Done = p.Done,
                        RemindAt = ParseTime(p.Text).RemindAt, Reminded = known?.Reminded ?? false,
                    });
                }
                else { _pending.Remove(p); continue; }
                p.Delivered = true;
                actions.Add(new TaskAction(p.Type, p.Id, p.Text, p.Done));
            }
            Save();
            return actions;
        }
    }

    /// <summary>The app is gone for good (the link was switched off): its mirror and queue are dropped.</summary>
    public void Unlink(string source)
    {
        lock (_lock)
        {
            _tasks.RemoveAll(t => t.Source == source);
            _pending.RemoveAll(p => p.Source == source);
            _lastSync.Remove(source);
            Save();
        }
    }

    // ------------------------------------------------------------------ the file

    private void Save()
    {
        if (Settings.ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new TasksFile { Tasks = _tasks, Pending = _pending }));
            File.Move(tmp, _file, true);
        }
        catch { /* tasks are not worth a crash */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var file = JsonSerializer.Deserialize<TasksFile>(File.ReadAllText(_file));
            _tasks = (file?.Tasks ?? []).Where(t => t != null && !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Text)).ToList();
            foreach (var t in _tasks) { t.Day ??= ""; t.Source ??= ""; t.ExternalId ??= ""; }
            _pending = (file?.Pending ?? []).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Type)).ToList();
            foreach (var p in _pending) { p.Source ??= ""; p.Id ??= ""; p.Text ??= ""; p.Day ??= ""; }
        }
        catch { _tasks = []; _pending = []; }
    }
}
