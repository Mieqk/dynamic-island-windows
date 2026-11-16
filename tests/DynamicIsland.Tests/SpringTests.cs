using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class SpringTests
{
    [Fact]
    public void Reaches_its_target_and_settles_exactly_on_it()
    {
        var s = new Spring(0) { Target = 100 };
        Assert.False(s.IsSettled);
        for (int i = 0; i < 60 * 5 && !s.IsSettled; i++) s.Step(1 / 60.0);

        Assert.True(s.IsSettled);
        Assert.Equal(100, s.Value);
        Assert.Equal(0, s.Velocity);
    }

    [Fact]
    public void Stays_finite_with_long_frames()
    {
        var s = new Spring(0) { Target = 500, Stiffness = 560, Damping = 23 };
        for (int i = 0; i < 200; i++)
        {
            s.Step(0.05);   // the frame loop never passes more than this
            Assert.True(double.IsFinite(s.Value));
            Assert.InRange(s.Value, -100, 700);
        }
        Assert.True(s.IsSettled);
    }

    [Fact]
    public void Snap_moves_at_once_and_stops()
    {
        var s = new Spring(10) { Target = 90 };
        s.Step(1 / 60.0);
        s.Snap(42);

        Assert.Equal(42, s.Value);
        Assert.Equal(42, s.Target);
        Assert.True(s.IsSettled);
    }
}
