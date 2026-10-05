// MIT License, Copyright (c) 2026 Viet Le

using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class FrameCadencePlannerTests
{
    [Fact]
    [Unit]
    public void RepeatsLastFrameWhenSourceIdle()
    {
        var planner = new FrameCadencePlanner(10);
        Assert.Equal(10, planner.Fps);
        Assert.Equal(0.1, planner.IntervalSeconds);

        var sourceFrames = new List<(double Timestamp, string Frame)>
        {
            (0.0, "frame0"),
            (0.35, "frame1")
        };

        var planned = planner.Plan(sourceFrames, 0.5);
        Assert.Equal(5, planned.Count);

        // Frame 0 at t=0.0
        Assert.Equal("frame0", planned[0].Frame);
        Assert.False(planned[0].IsRepeat);

        // Frame 1 at t=0.1: idle, repeat frame0
        Assert.Equal("frame0", planned[1].Frame);
        Assert.True(planned[1].IsRepeat);

        // Frame 2 at t=0.2: idle, repeat frame0
        Assert.Equal("frame0", planned[2].Frame);
        Assert.True(planned[2].IsRepeat);

        // Frame 3 at t=0.3: idle, repeat frame0
        Assert.Equal("frame0", planned[3].Frame);
        Assert.True(planned[3].IsRepeat);

        // Frame 4 at t=0.4: frame1 arrived at 0.35
        Assert.Equal("frame1", planned[4].Frame);
        Assert.False(planned[4].IsRepeat);
    }
}
