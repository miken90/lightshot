using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Audio;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class AudioLevelMeterTests
{
    [Fact]
    [Unit]
    public void ThreadSafeLevelUpdatesAndReads()
    {
        var meter = new AudioLevelMeter();

        Parallel.For(0, 1000, i =>
        {
            float val = (i % 100) / 100f;
            meter.Update(val);
            float read = meter.Level;
            Assert.InRange(read, 0f, 1f);
        });
    }

    [Fact]
    [Unit]
    public void ClampsLevelBetweenZeroAndOne()
    {
        var meter = new AudioLevelMeter();

        meter.Update(-0.5f);
        Assert.Equal(0f, meter.Level);

        meter.Update(1.5f);
        Assert.Equal(1f, meter.Level);

        meter.Update(0.75f);
        Assert.Equal(0.75f, meter.Level);
    }

    [Fact]
    [Unit]
    public void FeedsMutedMicrophoneDetectorCorrectly()
    {
        var meter = new AudioLevelMeter();
        var detector = new MutedMicrophoneDetector(3.0); // 3 seconds threshold

        // Silent audio below 0.04 silence floor
        meter.Update(0.01f);
        Assert.False(detector.ShowsWarning);

        // 1 second elapsed
        meter.ObserveInto(ref detector, 1.0);
        Assert.False(detector.ShowsWarning);

        // 2 seconds elapsed
        meter.ObserveInto(ref detector, 1.0);
        Assert.False(detector.ShowsWarning);

        // 3 seconds elapsed: warning should trigger
        meter.ObserveInto(ref detector, 1.0);
        Assert.True(detector.ShowsWarning);

        // Audible sound detected: warning clears immediately
        meter.Update(0.25f);
        meter.ObserveInto(ref detector, 0.1);
        Assert.False(detector.ShowsWarning);
    }

    [Fact]
    [Unit]
    public void ResetClearsLevelToZero()
    {
        var meter = new AudioLevelMeter();
        meter.Update(0.8f);
        Assert.Equal(0.8f, meter.Level);

        meter.Reset();
        Assert.Equal(0f, meter.Level);
    }
}
