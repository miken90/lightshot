using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class PauseClockTests
{
    [Fact]
    [Unit]
    public void ShiftsSamplesAfterResume()
    {
        var clock = new PauseClock();

        // Sample before pause (1.0s in HNS)
        long sample1 = 10_000_000;
        Assert.Equal(sample1, clock.AdjustTimestamp(sample1));
        Assert.False(clock.ShouldDrop(sample1));

        // Pause at 2.0s
        clock.Pause(20_000_000);
        Assert.True(clock.IsPaused);

        // Samples while paused should be dropped
        Assert.True(clock.ShouldDrop(25_000_000));
        Assert.True(clock.ShouldDrop(49_000_000));

        // Resume at 5.0s (3.0s pause duration)
        clock.Resume(50_000_000);
        Assert.False(clock.IsPaused);
        Assert.Equal(30_000_000, clock.TotalPauseDurationHns);

        // Sample after resume at 6.0s should be shifted by 3.0s -> 3.0s
        long sample2 = 60_000_000;
        Assert.False(clock.ShouldDrop(sample2));
        Assert.Equal(30_000_000, clock.AdjustTimestamp(sample2));

        // Second pause at 8.0s, resume at 10.0s (additional 2.0s pause)
        clock.Pause(80_000_000);
        clock.Resume(100_000_000);
        Assert.Equal(50_000_000, clock.TotalPauseDurationHns);

        // Sample at 11.0s shifted by 5.0s -> 6.0s
        long sample3 = 110_000_000;
        Assert.Equal(60_000_000, clock.AdjustTimestamp(sample3));
    }
}
