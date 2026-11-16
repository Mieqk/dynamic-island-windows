using DynamicIsland.Services;

namespace DynamicIsland.Tests;

/// <summary>"через 20 мин", "завтра 9:00"… typed into the island; whatever is typed, it must not throw.</summary>
public class ReminderParsingTests
{
    private static void Near(DateTime expected, DateTime? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange((actual!.Value - expected).TotalSeconds, -5, 5);
    }

    [Fact]
    public void In_some_minutes_or_hours()
    {
        Near(DateTime.Now.AddMinutes(20), CalendarService.ParseWhen("через 20 мин"));
        Near(DateTime.Now.AddHours(2), CalendarService.ParseWhen("in 2 h"));
        Near(DateTime.Now.AddHours(3), CalendarService.ParseWhen("позвонить через 3 ч"));
    }

    [Fact]
    public void Tomorrow_at_a_time()
    {
        var when = CalendarService.ParseWhen("завтра 9:00");
        Assert.NotNull(when);
        Assert.Equal(DateTime.Today.AddDays(1).AddHours(9), when!.Value);
    }

    [Fact]
    public void A_bare_time_is_today_or_tomorrow_but_never_in_the_past()
    {
        var when = CalendarService.ParseWhen("18:30");
        Assert.NotNull(when);
        Assert.Equal(new TimeSpan(18, 30, 0), when!.Value.TimeOfDay);
        Assert.True(when.Value >= DateTime.Now.AddSeconds(-5));
    }

    [Theory]
    [InlineData("через 99999999999 мин")]      // too long for a number: used to close the app while typing
    [InlineData("через 999999 ч")]
    [InlineData("через 20000 ч")]              // more than a year away
    [InlineData("in 99999999999999999999 min")]
    [InlineData("31.02 10:00")]                // no such date
    [InlineData("25:00")]
    [InlineData("12:99")]
    [InlineData("просто текст")]
    [InlineData("")]
    public void Nonsense_is_not_a_time_and_does_not_throw(string text) =>
        Assert.Null(CalendarService.ParseWhen(text));
}
