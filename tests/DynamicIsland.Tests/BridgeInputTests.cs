using DynamicIsland.Services;

namespace DynamicIsland.Tests;

/// <summary>What other programs send to the bridge is never trusted.</summary>
public class BridgeInputTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly double NowMs = Now.ToUnixTimeMilliseconds();

    [Fact]
    public void A_countdown_end_soon_is_kept() =>
        Assert.Equal((long)(NowMs + 90_000), ClaudeBridge.SaneEndsAt(NowMs + 90_000, Now));

    [Fact]
    public void An_end_a_little_in_the_past_is_kept_too() =>   // the island then simply drops the finished countdown
        Assert.Equal((long)(NowMs - 3_600_000), ClaudeBridge.SaneEndsAt(NowMs - 3_600_000, Now));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e18)]                 // far beyond any date: used to throw on the UI thread
    [InlineData(253402300800000.0)]    // year 10000
    public void Garbage_means_no_countdown(double value) =>
        Assert.Equal(0, ClaudeBridge.SaneEndsAt(value, Now));

    [Fact]
    public void More_than_a_day_away_means_no_countdown()
    {
        const double twoDays = 2 * 24 * 60 * 60 * 1000.0;
        Assert.Equal(0, ClaudeBridge.SaneEndsAt(NowMs + twoDays, Now));
        Assert.Equal(0, ClaudeBridge.SaneEndsAt(NowMs - twoDays, Now));
    }
}
