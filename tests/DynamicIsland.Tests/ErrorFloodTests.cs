using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class ErrorFloodTests
{
    private static readonly DateTime T = new(2026, 3, 10, 12, 0, 0);

    [Fact]
    public void A_few_errors_are_let_through()
    {
        var flood = new ErrorFlood();
        for (int i = 0; i < 10; i++) Assert.True(flood.Allow(T.AddMilliseconds(i * 100)));
    }

    [Fact]
    public void A_flood_is_not()
    {
        var flood = new ErrorFlood();
        for (int i = 0; i < 10; i++) flood.Allow(T);
        Assert.False(flood.Allow(T.AddSeconds(1)));
        Assert.False(flood.Allow(T.AddSeconds(2)));
    }

    [Fact]
    public void After_a_quiet_while_errors_are_let_through_again()
    {
        var flood = new ErrorFlood();
        for (int i = 0; i < 11; i++) flood.Allow(T);
        Assert.True(flood.Allow(T.AddSeconds(11)));
    }

    [Fact]
    public void Errors_spread_over_time_never_count_as_a_flood()
    {
        var flood = new ErrorFlood(max: 3, windowSeconds: 10);
        for (int i = 0; i < 50; i++) Assert.True(flood.Allow(T.AddSeconds(i * 6)));
    }
}
