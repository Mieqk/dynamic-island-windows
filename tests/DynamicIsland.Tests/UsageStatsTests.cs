using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class UsageStatsTests
{
    private static readonly DateTime Day = new(2026, 3, 10, 12, 0, 0);

    [Fact]
    public void Adds_up_time_per_app_and_sorts_the_longest_first()
    {
        using var tmp = new TempFolder();
        var stats = new UsageStats(tmp.File("usage.json"));
        stats.Add("chrome", 60, Day);
        stats.Add("code", 300, Day);
        stats.Add("chrome", 30, Day);

        var top = stats.Top(8, Day);
        Assert.Equal(2, top.Count);
        Assert.Equal(("code", TimeSpan.FromSeconds(300)), top[0]);
        Assert.Equal(("chrome", TimeSpan.FromSeconds(90)), top[1]);
        Assert.Equal(TimeSpan.FromSeconds(390), stats.Total(Day));
    }

    [Fact]
    public void Top_is_cut_to_the_requested_count()
    {
        using var tmp = new TempFolder();
        var stats = new UsageStats(tmp.File("usage.json"));
        for (int i = 1; i <= 5; i++) stats.Add("app" + i, i * 10, Day);

        var top = stats.Top(3, Day);
        Assert.Equal(new[] { "app5", "app4", "app3" }, top.Select(t => t.App));
    }

    [Fact]
    public void Days_are_kept_apart()
    {
        using var tmp = new TempFolder();
        var stats = new UsageStats(tmp.File("usage.json"));
        stats.Add("chrome", 100, Day);
        stats.Add("chrome", 40, Day.AddDays(1));

        Assert.Equal(TimeSpan.FromSeconds(100), stats.Total(Day));
        Assert.Equal(TimeSpan.FromSeconds(40), stats.Total(Day.AddDays(1)));
        Assert.Empty(stats.Top(8, Day.AddDays(2)));
        Assert.Equal(TimeSpan.Zero, stats.Total(Day.AddDays(2)));
    }

    [Fact]
    public void Ignores_empty_names_and_non_positive_time()
    {
        using var tmp = new TempFolder();
        var stats = new UsageStats(tmp.File("usage.json"));
        stats.Add("", 10, Day);
        stats.Add("chrome", 0, Day);
        stats.Add("chrome", -5, Day);

        Assert.Empty(stats.Top(8, Day));
    }

    [Fact]
    public void Saved_totals_are_read_back()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("usage.json");
        var stats = new UsageStats(file);
        stats.Add("chrome", 75, Day);
        stats.Save(Day);

        var again = new UsageStats(file);
        Assert.Equal(TimeSpan.FromSeconds(75), again.Total(Day));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void Saving_drops_days_older_than_90_days()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("usage.json");
        var stats = new UsageStats(file);
        stats.Add("old", 10, Day.AddDays(-91));
        stats.Add("edge", 10, Day.AddDays(-90));
        stats.Add("new", 10, Day);
        stats.Save(Day);

        var again = new UsageStats(file);
        Assert.Equal(TimeSpan.Zero, again.Total(Day.AddDays(-91)));
        Assert.Equal(TimeSpan.FromSeconds(10), again.Total(Day.AddDays(-90)));
        Assert.Equal(TimeSpan.FromSeconds(10), again.Total(Day));
    }

    [Fact]
    public void Reset_forgets_everything_and_deletes_the_file()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("usage.json");
        var stats = new UsageStats(file);
        stats.Add("chrome", 75, Day);
        stats.Save(Day);
        Assert.True(File.Exists(file));

        stats.Reset();
        Assert.False(File.Exists(file));
        Assert.Empty(stats.Top(8, Day));
    }

    [Fact]
    public void A_broken_file_is_treated_as_empty()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("usage.json");
        File.WriteAllText(file, "{ this is not json");

        var stats = new UsageStats(file);
        Assert.Empty(stats.Top(8, Day));
        stats.Add("chrome", 5, Day);
        stats.Save(Day);
        Assert.Equal(TimeSpan.FromSeconds(5), new UsageStats(file).Total(Day));
    }
}
