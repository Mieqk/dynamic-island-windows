using DynamicIsland.Services;

namespace DynamicIsland.Tests;

/// <summary>The counting itself: what is time at the computer, what is a break, when a break is due.</summary>
public class UsageEngineTests
{
    private static readonly DateTime Start = new(2026, 3, 10, 9, 0, 0);

    private static UsageStats New(TempFolder tmp) => new(tmp.File("usage.json"));

    /// <summary>Somebody works in <paramref name="app"/> for a while: one observation a second, input every second.</summary>
    private static DateTime Work(UsageStats stats, DateTime from, TimeSpan howLong, string app = "code")
    {
        var t = from;
        for (int i = 0; i <= (int)howLong.TotalSeconds; i++, t = from.AddSeconds(i)) stats.Observe(t, app, 0);
        return from + howLong;
    }

    /// <summary>Nobody touches the PC: observations go on, the idle time grows.</summary>
    private static DateTime Away(UsageStats stats, DateTime from, TimeSpan howLong, string app = "code")
    {
        for (int i = 1; i <= (int)howLong.TotalSeconds; i++) stats.Observe(from.AddSeconds(i), app, i);
        return from + howLong;
    }

    [Fact]
    public void Counts_the_seconds_between_observations()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        Work(stats, Start, TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), stats.Total(Start));
        Assert.Equal(("code", TimeSpan.FromSeconds(10)), stats.Top(8, Start)[0]);
    }

    [Fact]
    public void Time_away_is_not_counted_but_the_first_two_minutes_of_silence_are()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var t = Work(stats, Start, TimeSpan.FromSeconds(60));
        Away(stats, t, TimeSpan.FromMinutes(10));

        // 60 s of work, then silence: up to the idle limit it still counts (reading, thinking), after it no more
        Assert.Equal(TimeSpan.FromSeconds(60 + UsageStats.IdleLimitSec), stats.Total(Start));
    }

    [Fact]
    public void Watching_full_screen_counts_without_any_input()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        stats.Observe(Start, "vlc", 0);
        for (int i = 1; i <= 600; i++) stats.Observe(Start.AddSeconds(i), "vlc", i, watching: true);

        Assert.Equal(TimeSpan.FromSeconds(600), stats.Total(Start));
    }

    [Fact]
    public void Nothing_in_front_is_not_counted()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        for (int i = 0; i <= 30; i++) stats.Observe(Start.AddSeconds(i), null, 0);

        Assert.Equal(TimeSpan.Zero, stats.Total(Start));
    }

    [Fact]
    public void A_stall_adds_no_more_than_a_few_seconds()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        stats.Observe(Start, "code", 0);
        stats.Observe(Start.AddSeconds(60), "code", 0);   // the app hung for a minute

        Assert.Equal(TimeSpan.FromSeconds(UsageStats.MaxStepSec), stats.Total(Start));
    }

    [Fact]
    public void Time_goes_to_the_hour_it_happened_in()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        Work(stats, new DateTime(2026, 3, 10, 9, 59, 50), TimeSpan.FromSeconds(20));

        var hours = stats.Summary(Start).Hours;
        Assert.Equal(24, hours.Count);
        Assert.Equal(TimeSpan.FromSeconds(9), hours[9]);     // 09:59:51 … 09:59:59
        Assert.Equal(TimeSpan.FromSeconds(11), hours[10]);   // 10:00:00 … 10:00:10
        Assert.Equal(TimeSpan.FromSeconds(20), stats.Total(Start));
    }

    [Fact]
    public void Remembers_the_first_and_last_minute_of_the_day()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        Work(stats, new DateTime(2026, 3, 10, 9, 12, 0), TimeSpan.FromSeconds(30));
        Work(stats, new DateTime(2026, 3, 10, 21, 40, 0), TimeSpan.FromSeconds(30));

        var day = stats.Summary(Start);
        Assert.Equal(new TimeSpan(9, 12, 0), day.First);
        Assert.Equal(new TimeSpan(21, 40, 0), day.Last);
        Assert.Null(stats.Summary(Start.AddDays(1)).First);
    }

    // ------------------------------------------------------------------ stretches and breaks

    [Fact]
    public void A_stretch_grows_while_you_work()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var t = Work(stats, Start, TimeSpan.FromMinutes(30));

        Assert.Equal(TimeSpan.FromMinutes(30), stats.CurrentStretch(t));
        Assert.Equal(TimeSpan.FromMinutes(30), stats.Summary(Start).LongestStretch);
    }

    [Fact]
    public void A_short_pause_does_not_end_the_stretch()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var t = Work(stats, Start, TimeSpan.FromMinutes(10));
        t = Away(stats, t, TimeSpan.FromSeconds(150));       // 2.5 minutes: went for a glass of water
        t = Work(stats, t, TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromSeconds(600 + 150 + 300), stats.CurrentStretch(t));
    }

    [Fact]
    public void A_pause_of_three_minutes_is_a_break()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var t = Work(stats, Start, TimeSpan.FromMinutes(40));
        t = Away(stats, t, TimeSpan.FromSeconds(200));
        Assert.Equal(TimeSpan.Zero, stats.CurrentStretch(t));   // still away: no stretch

        var back = t;
        t = Work(stats, back, TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(5), stats.CurrentStretch(t));
        Assert.Equal(TimeSpan.FromMinutes(40), stats.Summary(Start).LongestStretch);   // the longest of the day is kept
    }

    [Fact]
    public void A_sleeping_pc_is_a_break_too()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var t = Work(stats, Start, TimeSpan.FromMinutes(20));
        var woke = t.AddMinutes(30);                             // no observations at all in between
        t = Work(stats, woke, TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), stats.CurrentStretch(t));
        // the sleep itself is not counted as use: 20 + 2 minutes (plus one capped step when it woke)
        Assert.InRange(stats.Total(Start).TotalSeconds, 22 * 60, 22 * 60 + UsageStats.MaxStepSec);
    }

    [Fact]
    public void A_stretch_over_midnight_gives_each_day_its_part()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var night = new DateTime(2026, 3, 10, 23, 50, 0);
        Work(stats, night, TimeSpan.FromMinutes(25));

        Assert.Equal(TimeSpan.FromSeconds(10 * 60 - 1), stats.Summary(night).LongestStretch);          // 23:50:00 … 23:59:59
        Assert.Equal(TimeSpan.FromMinutes(15), stats.Summary(night.AddDays(1)).LongestStretch);        // 00:00 … 00:15
    }

    [Fact]
    public void A_break_is_due_once_per_interval_of_the_stretch()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var every = TimeSpan.FromMinutes(50);

        var t = Work(stats, Start, TimeSpan.FromMinutes(49));
        Assert.False(stats.BreakDue(t, every));

        t = Work(stats, t, TimeSpan.FromMinutes(1));
        Assert.True(stats.BreakDue(t, every));
        Assert.False(stats.BreakDue(t.AddSeconds(1), every));    // said once

        t = Work(stats, t, TimeSpan.FromMinutes(49));
        Assert.False(stats.BreakDue(t, every));
        t = Work(stats, t, TimeSpan.FromMinutes(1));
        Assert.True(stats.BreakDue(t, every));                   // still no break: again after another interval
    }

    [Fact]
    public void A_break_resets_the_reminder()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var every = TimeSpan.FromMinutes(50);

        var t = Work(stats, Start, TimeSpan.FromMinutes(45));
        t = Away(stats, t, TimeSpan.FromMinutes(5));
        Assert.False(stats.BreakDue(t, every));                  // away: nobody to tell

        t = Work(stats, t, TimeSpan.FromMinutes(10));
        Assert.False(stats.BreakDue(t, every));                  // 10 minutes after a real break, not 60
        t = Work(stats, t, TimeSpan.FromMinutes(40));
        Assert.True(stats.BreakDue(t, every));
    }

    [Fact]
    public void A_due_break_stays_due_until_it_is_asked_for()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var t = Work(stats, Start, TimeSpan.FromMinutes(70));     // nobody asked at 50 minutes (a film was on)

        Assert.True(stats.BreakDue(t, TimeSpan.FromMinutes(50)));
        Assert.False(stats.BreakDue(t, TimeSpan.Zero));           // switched off
    }

    // ------------------------------------------------------------------ the file

    [Fact]
    public void Hours_and_the_longest_stretch_survive_a_restart()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("usage.json");
        var stats = new UsageStats(file);
        Work(stats, Start, TimeSpan.FromMinutes(15));
        stats.Save(Start);

        var day = new UsageStats(file).Summary(Start);
        Assert.Equal(TimeSpan.FromMinutes(15), day.Total);
        Assert.Equal(TimeSpan.FromMinutes(15), day.Hours[9]);
        Assert.Equal(TimeSpan.FromMinutes(15), day.LongestStretch);
        Assert.Equal(new TimeSpan(9, 0, 0), day.First);
    }

    [Fact]
    public void Reads_the_file_of_the_first_version()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("usage.json");
        File.WriteAllText(file, """{"2026-03-10":{"chrome":120.5,"code":30},"2026-03-09":{"chrome":10}}""");

        var stats = new UsageStats(file);
        Assert.Equal(TimeSpan.FromSeconds(150.5), stats.Total(Start));
        Assert.Equal(TimeSpan.FromSeconds(10), stats.Total(Start.AddDays(-1)));
        Assert.Equal(TimeSpan.Zero, stats.Summary(Start).LongestStretch);
        Assert.All(stats.Summary(Start).Hours, h => Assert.Equal(TimeSpan.Zero, h));

        // and writes the new one, which reads back the same
        stats.Add("code", 1, Start);
        stats.Save(Start);
        Assert.Contains("\"v\":2", File.ReadAllText(file));
        Assert.Equal(TimeSpan.FromSeconds(151.5), new UsageStats(file).Total(Start));
    }

    [Fact]
    public void A_file_with_broken_days_is_repaired_on_reading()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("usage.json");
        File.WriteAllText(file, """{"v":2,"days":{"2026-03-10":{"apps":{"chrome":60},"hours":[1,2,3]},"2026-03-09":null}}""");

        var day = new UsageStats(file).Summary(Start);
        Assert.Equal(TimeSpan.FromSeconds(60), day.Total);
        Assert.Equal(24, day.Hours.Count);
    }

    // ------------------------------------------------------------------ summaries

    [Fact]
    public void A_day_is_split_by_category_with_friendly_names()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        stats.Add("chrome", 600, Start);
        stats.Add("WINWORD", 1200, Start);
        stats.Add("Telegram", 300, Start);
        stats.Add("mygame", 900, Start);

        var day = stats.Summary(Start);
        Assert.Equal(new[] { "Word", "mygame", "Chrome", "Telegram" }, day.Apps.Select(a => a.Name));
        Assert.Equal(TimeSpan.FromSeconds(1200), day.ByCategory[UsageCategory.Work]);
        Assert.Equal(TimeSpan.FromSeconds(600), day.ByCategory[UsageCategory.Browser]);
        Assert.Equal(TimeSpan.FromSeconds(300), day.ByCategory[UsageCategory.Communication]);
        Assert.Equal(TimeSpan.FromSeconds(900), day.ByCategory[UsageCategory.Other]);
        Assert.False(day.ByCategory.ContainsKey(UsageCategory.Creative));
    }

    [Fact]
    public void The_user_can_move_a_program_to_another_category()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        stats.Add("mygame", 900, Start);
        stats.Add("chrome", 600, Start);
        var overrides = new Dictionary<string, string> { ["MyGame"] = "fun", ["chrome"] = "work", ["code"] = "nonsense" };

        var day = stats.Summary(Start, overrides);
        Assert.Equal(TimeSpan.FromSeconds(900), day.ByCategory[UsageCategory.Entertainment]);
        Assert.Equal(TimeSpan.FromSeconds(600), day.ByCategory[UsageCategory.Work]);
        Assert.False(day.ByCategory.ContainsKey(UsageCategory.Other));
    }

    [Fact]
    public void An_empty_day_has_an_empty_summary()
    {
        using var tmp = new TempFolder();
        var day = New(tmp).Summary(Start);

        Assert.Equal(TimeSpan.Zero, day.Total);
        Assert.Empty(day.Apps);
        Assert.Empty(day.ByCategory);
        Assert.Equal(24, day.Hours.Count);
        Assert.Null(day.Last);
    }

    [Fact]
    public void A_week_has_seven_days_a_total_and_the_week_before()
    {
        using var tmp = new TempFolder();
        var stats = New(tmp);
        var sunday = new DateTime(2026, 3, 15);
        stats.Add("code", 3600, sunday.AddDays(-6));    // Monday
        stats.Add("code", 7200, sunday.AddDays(-2));    // Friday: the busiest
        stats.Add("chrome", 1800, sunday);
        stats.Add("code", 5000, sunday.AddDays(-7));    // the week before
        stats.Add("code", 400, sunday.AddDays(-13));
        stats.Add("code", 9999, sunday.AddDays(-14));   // older than two weeks: not in the comparison

        var week = stats.Week(sunday);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(sunday.AddDays(-6), week.Days[0].Day);
        Assert.Equal(sunday, week.Days[6].Day);
        Assert.Equal(TimeSpan.FromSeconds(3600), week.Days[0].Total);
        Assert.Equal(TimeSpan.Zero, week.Days[1].Total);
        Assert.Equal(TimeSpan.FromSeconds(12600), week.Total);
        Assert.Equal(TimeSpan.FromSeconds(5400), week.PreviousTotal);
        Assert.Equal(TimeSpan.FromSeconds(4200), week.DailyAverage);   // over the three days that were used
        Assert.Equal(sunday.AddDays(-2), week.BusiestDay);
        Assert.Equal(TimeSpan.FromSeconds(10800), week.ByCategory[UsageCategory.Work]);
        Assert.Equal(TimeSpan.FromSeconds(1800), week.ByCategory[UsageCategory.Browser]);
    }

    [Fact]
    public void An_empty_week_has_no_average_and_no_busiest_day()
    {
        using var tmp = new TempFolder();
        var week = New(tmp).Week(Start);

        Assert.Equal(TimeSpan.Zero, week.Total);
        Assert.Equal(TimeSpan.Zero, week.DailyAverage);
        Assert.Null(week.BusiestDay);
        Assert.Equal(7, week.Days.Count);
    }
}
