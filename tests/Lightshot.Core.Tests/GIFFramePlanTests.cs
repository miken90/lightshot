// Ported from LightshotKit/Tests/LightshotKitTests/GIFFramePlanTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class GIFFramePlanTests
{
    [Fact]
    [Unit]
    public void TheDelayIsWholeCentisecondsNearestToTheFPS()
    {
        Assert.Equal(0.07, GIFFramePlan.Delay(15));
        Assert.Equal(0.10, GIFFramePlan.Delay(10));
        Assert.Equal(0.03, GIFFramePlan.Delay(30));
        Assert.Equal(0.02, GIFFramePlan.Delay(100));
        Assert.Equal(1.0, GIFFramePlan.Delay(0));
    }

    [Fact]
    [Unit]
    public void TheFrameCountCoversTheDurationAndIsNeverZero()
    {
        var settings = new GIFSettings(10, 1.0, null, false);
        Assert.Equal(20, new GIFFramePlan(2.0, new Size(100, 100), settings).FrameCount);
        Assert.Equal(21, new GIFFramePlan(2.05, new Size(100, 100), settings).FrameCount);
        Assert.Equal(1, new GIFFramePlan(0, new Size(100, 100), settings).FrameCount);
        Assert.Equal(9, new GIFFramePlan(0.9, new Size(100, 100), settings).FrameCount);
    }

    [Fact]
    [Unit]
    public void AFasterSourceIsSampledDownAndASlowerOneFillsOneFramePerSourceFrame()
    {
        var plan = new GIFFramePlan(1.0, new Size(100, 100), new GIFSettings(15, 1.0, null, false));
        int? last = null;
        var kept = new List<int>();
        for (int i = 0; i < 30; i++)
        {
            int? index = plan.OutputIndex((double)i / 30.0, last);
            if (index.HasValue)
            {
                kept.Add(i);
                last = index;
            }
        }
        Assert.Equal([0, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 24, 26, 28], kept);
        Assert.Equal(13, last);
        Assert.Equal(15, plan.FrameCount);

        int? slowLast = null;
        for (int i = 0; i < 5; i++)
        {
            slowLast = plan.OutputIndex((double)i / 5.0, slowLast) ?? slowLast;
        }
        Assert.Equal(4, slowLast);

        Assert.Null(plan.OutputIndex(5.0, 14));
        Assert.Equal(1, plan.OutputIndex(0.07 - 1e-9, 0));
    }

    [Fact]
    [Unit]
    public void TheOutputIsScaledDownToTheMaxWidthKeepingAspectButNeverUp()
    {
        var wide = new Size(1920, 1080);
        Assert.Equal(new Size(800, 450), new GIFFramePlan(1.0, wide, new GIFSettings(15, 1.0, 800, true)).OutputSize);
        Assert.Equal(wide, new GIFFramePlan(1.0, wide, new GIFSettings(15, 1.0, null, true)).OutputSize);
        Assert.Equal(new Size(400, 300), new GIFFramePlan(1.0, new Size(400, 300), new GIFSettings(15, 1.0, 800, true)).OutputSize);
        Assert.Equal(new Size(100, 33), new GIFFramePlan(1.0, new Size(1000, 333), new GIFSettings(15, 1.0, 100, true)).OutputSize);
    }

    [Fact]
    [Unit]
    public void QualityMapsToColourDepth()
    {
        Assert.Equal(8, GIFFramePlan.BitsPerChannel(1.0));
        Assert.Equal(7, GIFFramePlan.BitsPerChannel(0.8));
        Assert.Equal(6, GIFFramePlan.BitsPerChannel(0.5));
        Assert.Equal(4, GIFFramePlan.BitsPerChannel(0.0));
    }
}
