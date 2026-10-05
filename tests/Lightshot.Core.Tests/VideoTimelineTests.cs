// Ported from LightshotKit/Tests/LightshotKitTests/VideoTimelineTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class VideoTimelineTests
{
    [Fact]
    [Unit]
    public void TimeAndPositionMapBothWaysClampedToTheClip()
    {
        var scale = new TimelineScale(8, 800);
        Assert.Equal(400, scale.X(4));
        Assert.True(scale.X(-1) == 0 && scale.X(20) == 800);
        Assert.Equal(2, scale.Time(200));
        Assert.True(scale.Time(-5) == 0 && scale.Time(900) == 8);

        Assert.Equal(0, new TimelineScale(0, 800).X(3));
        Assert.Equal(0, new TimelineScale(8, 0).Time(3));
    }

    [Fact]
    [Unit]
    public void TheRulerPicksTheFinestStepThatKeepsLabelsApart()
    {
        Assert.Equal(1, new TimelineScale(8, 800).TickStep());
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], new TimelineScale(8, 800).Ticks());

        Assert.Equal(10, new TimelineScale(60, 600).TickStep());
        Assert.Equal(1, new TimelineScale(60, 4800).TickStep());
        Assert.Equal(3600, new TimelineScale(36000, 100).TickStep());
    }

    [Fact]
    [Unit]
    public void LabelsReadAsMinutesAndSecondsAndHoursPastAnHour()
    {
        Assert.Equal("00:04", TimelineScale.Label(4.9));
        Assert.Equal("01:15", TimelineScale.Label(75));
        Assert.Equal("1:02:03", TimelineScale.Label(3723));
        Assert.Equal("00:00", TimelineScale.Label(-2));
    }

    [Fact]
    [Unit]
    public void FilmstripSlotsTakeTheNearestFrameAndRepeatWhenThereAreMoreSlots()
    {
        Assert.Equal([5, 15, 25, 35], TimelineScale.FilmstripFrames(4, 40));
        Assert.Equal([0, 0, 1, 1], TimelineScale.FilmstripFrames(4, 2));
        Assert.Empty(TimelineScale.FilmstripFrames(0, 10));
        Assert.Empty(TimelineScale.FilmstripFrames(3, 0));
    }
}
