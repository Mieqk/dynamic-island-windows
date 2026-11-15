using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DynamicIsland.Services;

/// <summary>
/// A personal message for whoever the island was made for: a first-run greeting, an "about this version" card,
/// a theme with her name and greetings on special dates. The words are NOT in the source code or the repository:
/// they live in a local file, dedication.json (see docs/dedication.md), so a public fork stays private.
/// Without the file nothing changes in the app.
/// </summary>
public sealed class Dedication
{
    public sealed class SpecialDate
    {
        /// <summary>"MM-dd", e.g. "03-14". An empty or wrong date is ignored.</summary>
        public string Date { get; set; } = "";
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>The name shown as the title of the greeting.</summary>
    public string Title { get; set; } = "";
    /// <summary>The short text of the first-run greeting (a toast).</summary>
    public string Toast { get; set; } = "";
    /// <summary>The longer message on the "about this version" card in Settings.</summary>
    public string Message { get; set; } = "";
    public string Signature { get; set; } = "";
    /// <summary>The name of the personal theme (empty: no such theme).</summary>
    public string ThemeName { get; set; } = "";
    /// <summary>The tray tooltip, e.g. "Atoll · для …" (empty: just "Atoll").</summary>
    public string VersionLabel { get; set; } = "";
    public List<SpecialDate> SpecialDates { get; set; } = [];

    public static Dedication? Current { get; private set; } = Load();

    private static IEnumerable<string> Places()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "dedication.json");
        yield return Path.Combine(AppContext.BaseDirectory, "dedication.json");   // or right next to the program
    }

    private static Dedication? Load()
    {
        foreach (var path in Places())
            if (Read(path) is { } d) return d;
        return null;
    }

    /// <summary>The file as a dedication, or null when it is missing, broken or has no words in it.</summary>
    private static Dedication? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var d = JsonSerializer.Deserialize<Dedication>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (d == null || (d.Title.Length == 0 && d.Message.Length == 0 && d.Toast.Length == 0)) return null;
            d.SpecialDates ??= [];
            return d;
        }
        catch { return null; }   // a broken file is simply not used
    }

    /// <summary>A file the user dropped on the island: "dedication.json", or what a browser makes of it ("dedication (1).json").</summary>
    public static bool LooksLikeDedicationFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("dedication", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Takes the file in: checks it, keeps a copy in %APPDATA%\DynamicIsland and starts using it at once.</summary>
    public static bool Import(string path)
    {
        var d = Read(path);
        if (d == null) return false;
        try
        {
            var target = Places().First();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(path, target, true);
        }
        catch { return false; }
        Current = d;
        return true;
    }

    /// <summary>The special date that is today, if any.</summary>
    public SpecialDate? Today(DateTime now)
    {
        var key = now.ToString("MM-dd", CultureInfo.InvariantCulture);
        return SpecialDates.FirstOrDefault(s => s != null && s.Date == key);
    }
}
