namespace DynamicIsland.Services;

/// <summary>What kind of program it is (for the totals by category on the Statistics tab).</summary>
public enum UsageCategory { Work, Communication, Browser, Creative, Entertainment, Other }

/// <summary>
/// Friendly names and categories for process names ("WINWORD" → "Word", work). Only a lookup table shipped with the
/// app: nothing is looked up online. Unknown programs keep their process name and go to "Other"; the user can move
/// any program to another category (the overrides: process name → category id).
/// </summary>
public static class UsageCatalog
{
    /// <summary>Category ids as they are stored in the settings.</summary>
    public static readonly (UsageCategory Category, string Id)[] Ids =
    [
        (UsageCategory.Work, "work"), (UsageCategory.Communication, "talk"), (UsageCategory.Browser, "web"),
        (UsageCategory.Creative, "create"), (UsageCategory.Entertainment, "fun"), (UsageCategory.Other, "other"),
    ];

    public static string IdOf(UsageCategory c) => Ids.First(i => i.Category == c).Id;

    public static UsageCategory? ParseCategory(string? id)
    {
        foreach (var (category, known) in Ids)
            if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase)) return category;
        return null;
    }

    private static readonly Dictionary<string, (string Name, UsageCategory Category)> Known = Build();

    private static Dictionary<string, (string, UsageCategory)> Build()
    {
        var d = new Dictionary<string, (string, UsageCategory)>(StringComparer.OrdinalIgnoreCase);
        void Add(UsageCategory c, params (string Process, string Name)[] apps) { foreach (var (p, n) in apps) d[p] = (n, c); }

        Add(UsageCategory.Browser,
            ("chrome", "Chrome"), ("msedge", "Edge"), ("firefox", "Firefox"), ("opera", "Opera"), ("brave", "Brave"),
            ("vivaldi", "Vivaldi"), ("browser", "Яндекс Браузер"), ("arc", "Arc"), ("iexplore", "Internet Explorer"));
        Add(UsageCategory.Communication,
            ("Telegram", "Telegram"), ("Discord", "Discord"), ("WhatsApp", "WhatsApp"), ("Viber", "Viber"), ("slack", "Slack"),
            ("ms-teams", "Teams"), ("Teams", "Teams"), ("Zoom", "Zoom"), ("Skype", "Skype"), ("OUTLOOK", "Outlook"),
            ("olk", "Outlook"), ("thunderbird", "Thunderbird"));
        Add(UsageCategory.Work,
            ("Code", "VS Code"), ("Cursor", "Cursor"), ("devenv", "Visual Studio"), ("rider64", "Rider"), ("idea64", "IntelliJ IDEA"),
            ("pycharm64", "PyCharm"), ("webstorm64", "WebStorm"), ("clion64", "CLion"), ("WINWORD", "Word"), ("EXCEL", "Excel"),
            ("POWERPNT", "PowerPoint"), ("ONENOTE", "OneNote"), ("soffice.bin", "LibreOffice"), ("notepad", "Блокнот"),
            ("notepad++", "Notepad++"), ("sublime_text", "Sublime Text"), ("Obsidian", "Obsidian"), ("Notion", "Notion"),
            ("Acrobat", "Acrobat"), ("AcroRd32", "Acrobat"), ("WindowsTerminal", "Терминал"), ("cmd", "Командная строка"),
            ("powershell", "PowerShell"), ("pwsh", "PowerShell"), ("Daily Note", "Daily Note"), ("claude", "Claude"), ("ChatGPT", "ChatGPT"));
        Add(UsageCategory.Creative,
            ("Photoshop", "Photoshop"), ("Illustrator", "Illustrator"), ("Lightroom", "Lightroom"), ("Figma", "Figma"),
            ("blender", "Blender"), ("krita", "Krita"), ("Adobe Premiere Pro", "Premiere Pro"), ("AfterFX", "After Effects"),
            ("Resolve", "DaVinci Resolve"), ("CapCut", "CapCut"), ("obs64", "OBS"), ("audacity", "Audacity"), ("FL64", "FL Studio"),
            ("paintdotnet", "Paint.NET"), ("mspaint", "Paint"));
        Add(UsageCategory.Entertainment,
            ("steam", "Steam"), ("steamwebhelper", "Steam"), ("EpicGamesLauncher", "Epic Games"), ("Battle.net", "Battle.net"),
            ("vlc", "VLC"), ("mpc-hc64", "MPC-HC"), ("Spotify", "Spotify"), ("Яндекс Музыка", "Яндекс Музыка"), ("AIMP", "AIMP"),
            ("Netflix", "Netflix"), ("cs2", "Counter-Strike 2"), ("dota2", "Dota 2"), ("GenshinImpact", "Genshin Impact"),
            ("RobloxPlayerBeta", "Roblox"), ("Minecraft.Windows", "Minecraft"), ("javaw", "Minecraft (Java)"),
            ("VALORANT-Win64-Shipping", "VALORANT"), ("League of Legends", "League of Legends"), ("osu!", "osu!"));
        Add(UsageCategory.Other,
            ("explorer", "Проводник"), ("ApplicationFrameHost", "Приложение Windows"), ("SystemSettings", "Параметры"),
            ("Taskmgr", "Диспетчер задач"), ("SearchHost", "Поиск"), ("LockApp", "Экран блокировки"), ("DynamicIsland", "Atoll"));
        return d;
    }

    /// <summary>The friendly name and the category of a process; <paramref name="overrides"/> (process → category id) win.</summary>
    public static (string Name, UsageCategory Category) Describe(string process, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var (name, category) = Known.TryGetValue(process, out var known) ? known : (process, UsageCategory.Other);
        if (overrides != null)
            foreach (var (p, id) in overrides)
                if (string.Equals(p, process, StringComparison.OrdinalIgnoreCase) && ParseCategory(id) is { } chosen) { category = chosen; break; }
        return (name, category);
    }
}
