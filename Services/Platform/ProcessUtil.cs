using System.Diagnostics;

namespace DynamicIsland.Services;

/// <summary>
/// Process lookups that always release the handles. Every <see cref="Process"/> returned by
/// <c>GetProcessesByName</c> holds a system handle until it is disposed, and the island asks every few seconds.
/// </summary>
public static class ProcessUtil
{
    /// <summary>Is a process with this name running?</summary>
    public static bool IsRunning(string name)
    {
        var all = Process.GetProcessesByName(name);
        try { return all.Length > 0; }
        finally { foreach (var p in all) p.Dispose(); }
    }

    /// <summary>The main window titles of the processes with this name (empty titles included, in process order).</summary>
    public static List<string> WindowTitles(string name)
    {
        var titles = new List<string>();
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { titles.Add(p.MainWindowTitle); }
            catch { titles.Add(""); }   // it closed just now
            finally { p.Dispose(); }
        }
        return titles;
    }
}
