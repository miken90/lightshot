// Ported from LightshotKit/Tests/LightshotKitTests/MutedMicrophoneDetectorTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class MutedMicrophoneDetectorTests
{
    [Fact]
    [Unit]
    public void WarnsAfterThreeSilentSecondsAndClearsForGoodOnSound()
    {
        var d = new MutedMicrophoneDetector();
        for (int i = 0; i < 29; i++)
        {
            d.Observe(0.0f, 0.1);
        }
        Assert.False(d.ShowsWarning);

        d.Observe(0.01f, 0.1);
        Assert.True(d.ShowsWarning);

        d.Observe(0.3f, 0.1);
        Assert.False(d.ShowsWarning);

        for (int i = 0; i < 100; i++)
        {
            d.Observe(0.0f, 0.1);
        }
        Assert.False(d.ShowsWarning);
    }

    [Fact]
    [Unit]
    public void SoundBeforeTheDeadlineMeansNoWarning()
    {
        var d = new MutedMicrophoneDetector();
        for (int i = 0; i < 10; i++)
        {
            d.Observe(0.0f, 0.1);
        }
        d.Observe(0.5f, 0.1);
        for (int i = 0; i < 50; i++)
        {
            d.Observe(0.0f, 0.1);
        }
        Assert.False(d.ShowsWarning);
    }

    [Fact]
    [Unit]
    public void PausedReadingsDoNotCountTowardsTheDeadline()
    {
        var d = new MutedMicrophoneDetector(silentSeconds: 1.0);
        for (int i = 0; i < 20; i++)
        {
            d.Observe(0.0f, 0.1, paused: true);
        }
        Assert.False(d.ShowsWarning);

        for (int i = 0; i < 10; i++)
        {
            d.Observe(0.0f, 0.1);
        }
        Assert.True(d.ShowsWarning);
    }
}
