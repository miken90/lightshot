using System;
using Lightshot.Platform.Windows.Audio;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class AudioClockDriftFilterTests
{
    [Fact]
    [Unit]
    public void FiltersJitterMaintainingSmoothMonotonicTimestamps()
    {
        var filter = new AudioClockDriftFilter(alpha: 0.05);
        long packetDurationHns = 100_000; // 10 ms in 100ns units
        long startHns = 10_000_000; // 1 second

        long lastOutput = 0;
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            long nominalTime = startHns + i * packetDurationHns;
            // Add +/- 2ms jitter (-20,000 to +20,000 HNS)
            long jitter = (long)((random.NextDouble() * 40_000) - 20_000);
            long jitteryTime = nominalTime + jitter;

            long smoothed = filter.SmoothTimestamp(jitteryTime, packetDurationHns);

            if (i > 0)
            {
                // Must be strictly monotonic
                Assert.True(smoothed >= lastOutput, $"Timestamp stepped backwards at frame {i}: {smoothed} < {lastOutput}");
            }
            lastOutput = smoothed;
        }

        // Final output should be very close to nominal end time
        long expectedEnd = startHns + 99 * packetDurationHns;
        long diff = Math.Abs(lastOutput - expectedEnd);
        Assert.True(diff < 50_000, $"Smoothed output drifted too far: diff = {diff} HNS");
    }

    [Fact]
    [Unit]
    public void TracksClockFrequencyDrift()
    {
        var filter = new AudioClockDriftFilter(alpha: 0.1);
        long packetDurationHns = 100_000;
        long startHns = 10_000_000;

        // Clock running 200 ppm faster (each packet advances 99,980 HNS instead of 100,000)
        long driftPerPacket = 20;

        long lastOutput = 0;
        for (int i = 0; i < 200; i++)
        {
            long actualTime = startHns + i * (packetDurationHns - driftPerPacket);
            long smoothed = filter.SmoothTimestamp(actualTime, packetDurationHns);

            if (i > 0)
            {
                Assert.True(smoothed >= lastOutput);
            }
            lastOutput = smoothed;
        }

        long actualFinal = startHns + 199 * (packetDurationHns - driftPerPacket);
        long diff = Math.Abs(lastOutput - actualFinal);
        Assert.True(diff < 50_000, $"Filter failed to track clock frequency: diff = {diff} HNS");
    }

    [Fact]
    [Unit]
    public void SnapsOnLargeDiscontinuity()
    {
        var filter = new AudioClockDriftFilter(discontinuityThresholdHns: 2_000_000); // 200 ms
        long packetDurationHns = 100_000;

        long t0 = filter.SmoothTimestamp(10_000_000, packetDurationHns);
        Assert.Equal(10_000_000, t0);

        // Advance normally 5 packets
        long last = t0;
        for (int i = 1; i <= 5; i++)
        {
            last = filter.SmoothTimestamp(10_000_000 + i * packetDurationHns, packetDurationHns);
        }

        // Jump 500 ms (5,000,000 HNS)
        long jumpTarget = last + 5_000_000;
        long afterJump = filter.SmoothTimestamp(jumpTarget, packetDurationHns);

        Assert.Equal(jumpTarget, afterJump);
    }

    [Fact]
    [Unit]
    public void UpdatesRenderClockAndAppliesOffset()
    {
        var filter = new AudioClockDriftFilter(alpha: 0.5);

        Assert.False(filter.HasRenderClock);

        // Device rendered 48,000 frames at 48,000 Hz = exactly 1.0 second (10,000,000 HNS)
        // QPC time when sampled was 9,500,000 HNS (offset = +500,000 HNS)
        filter.UpdateRenderClock(devicePosition: 48000, qpcPositionHns: 9_500_000, frequency: 48000);

        Assert.True(filter.HasRenderClock);
        Assert.Equal(500_000, filter.SmoothedOffsetHns, 1);

        // Packet at QPC 10,000,000 should get offset added
        long packetDurationHns = 100_000;
        long smoothed = filter.SmoothTimestamp(10_000_000, packetDurationHns);

        Assert.Equal(10_500_000, smoothed);
    }

    [Fact]
    [Unit]
    public void ResetClearsState()
    {
        var filter = new AudioClockDriftFilter();
        filter.UpdateRenderClock(48000, 10_000_000, 48000);
        filter.SmoothTimestamp(10_000_000, 100_000);

        Assert.True(filter.IsInitialized);
        Assert.True(filter.HasRenderClock);

        filter.Reset();

        Assert.False(filter.IsInitialized);
        Assert.False(filter.HasRenderClock);
        Assert.Equal(0, filter.LastOutputHns);
    }
}
