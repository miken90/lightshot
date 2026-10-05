// Ported from LightshotKit/Tests/LightshotKitTests/CanvasProjectionTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class CanvasProjectionTests
{
    [Fact]
    [Unit]
    public void FitsWideImageAndCentersVertically()
    {
        var p = new CanvasProjection(new Size(200, 100), new Size(200, 200));
        Assert.Equal(1.0, p.Scale);
        Assert.Equal(new Point(0, 50), p.ImageOrigin);
        Assert.Equal(new Point(0, 50), p.ToView(new Point(0, 0)));
    }

    [Fact]
    [Unit]
    public void ScalesDownToFitAndRoundTrips()
    {
        var p = new CanvasProjection(new Size(400, 400), new Size(200, 200));
        Assert.Equal(0.5, p.Scale);
        var imagePoint = new Point(120, 300);
        var viewPoint = p.ToView(imagePoint);
        var back = p.ToImage(viewPoint);
        Assert.True(Math.Abs(back.X - imagePoint.X) < 1e-9);
        Assert.True(Math.Abs(back.Y - imagePoint.Y) < 1e-9);
    }

    [Fact]
    [Unit]
    public void ProjectsRectExtentByScale()
    {
        var p = new CanvasProjection(new Size(100, 100), new Size(50, 50));
        var r = p.ToView(new Rect(10, 20, 40, 60));
        Assert.Equal(new Rect(5, 10, 20, 30), r);
    }

    [Fact]
    [Unit]
    public void InsetKeepsTheImageClearOfEveryEdge()
    {
        var p = new CanvasProjection(new Size(400, 200), new Size(240, 240), inset: 20);
        Assert.Equal(0.5, p.Scale);
        Assert.Equal(new Rect(20, 70, 200, 100), p.ToView(new Rect(0, 0, 400, 200)));
    }

    [Fact]
    [Unit]
    public void AnInsetLargerThanTheViewDegradesToZeroScale()
    {
        var p = new CanvasProjection(new Size(100, 100), new Size(30, 30), inset: 20);
        Assert.Equal(0.0, p.Scale);
        Assert.Equal(new Point(0, 0), p.ToImage(new Point(10, 10)));
    }

    [Fact]
    [Unit]
    public void DegenerateViewMapsToOriginInsteadOfDividingByZero()
    {
        var p = new CanvasProjection(new Size(100, 100), new Size(0, 0));
        Assert.Equal(new Point(0, 0), p.ToImage(new Point(10, 10)));
    }
}
