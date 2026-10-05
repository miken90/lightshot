using System.Diagnostics;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class QpcClockTests
{
    [Fact]
    [Unit]
    public void ConvertsToSourceSeconds()
    {
        long startTicks = 100_000;
        long elapsedTicks = (long)(2.5 * Stopwatch.Frequency);
        long currentTicks = startTicks + elapsedTicks;

        double sourceSeconds = QpcClock.ToSourceSeconds(currentTicks, startTicks);

        Assert.Equal(2.5, sourceSeconds, 6);
        Assert.Equal(10_000_000, QpcClock.ToHns(Stopwatch.Frequency));
        Assert.Equal(1.0, QpcClock.ToSeconds(Stopwatch.Frequency), 6);
        Assert.Equal((long)(3.0 * Stopwatch.Frequency), QpcClock.FromSeconds(3.0));
    }
}
