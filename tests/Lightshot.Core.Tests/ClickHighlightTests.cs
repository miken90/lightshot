// Ported from LightshotKit/Tests/LightshotKitTests/ClickHighlightTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class ClickHighlightTests
{
    [Fact]
    [Unit]
    public void TheHaloFollowsThePointerAndUsesTheSizeAndStyle()
    {
        var m = new ClickHighlightModel(new ClickHighlightSettings(CursorHighlightStyle.Filled, CursorHighlightSize.Large));
        Assert.Empty(m.Circles(0));

        m.PointerMoved(new Point(100, 50));
        var halo = m.Circles(0);
        Assert.Single(halo);
        Assert.Equal(new HighlightCircle(new Point(100, 50), 32, ClickHighlightModel.HaloOpacity, true, 0), halo[0]);

        m.PointerMoved(new Point(120, 50));
        Assert.Equal(new Point(120, 50), m.Circles(1)[0].Center);
    }

    [Fact]
    [Unit]
    public void TheThreeStylesDrawDifferentHalos()
    {
        static HighlightCircle Halo(CursorHighlightStyle style)
        {
            var m = new ClickHighlightModel(new ClickHighlightSettings(style, CursorHighlightSize.Medium));
            m.PointerMoved(new Point(0, 0));
            return m.Circles(0)[0];
        }

        var ring = Halo(CursorHighlightStyle.Ring);
        var filled = Halo(CursorHighlightStyle.Filled);
        var outline = Halo(CursorHighlightStyle.Outline);

        Assert.False(ring.Filled);
        Assert.Equal(22 * 0.18, ring.StrokeWidth);

        Assert.True(filled.Filled);
        Assert.Equal(0.0, filled.StrokeWidth);

        Assert.True(outline.Filled);
        Assert.Equal(22 * 0.18, outline.StrokeWidth);

        Assert.Equal(2.0, HighlightCircle.CalculateStrokeWidth(5));
        Assert.Equal(new Rect(-22, -22, 44, 44), ring.Bounds);
    }

    [Fact]
    [Unit]
    public void AClickSpawnsARingThatGrowsAndFadesThenDisappears()
    {
        var m = new ClickHighlightModel(new ClickHighlightSettings(CursorHighlightStyle.Ring, CursorHighlightSize.Small, animateClicks: true));
        m.Clicked(new Point(10, 10), 5);
        var start = m.Circles(5);
        Assert.Equal(2, start.Count);
        Assert.Equal(new HighlightCircle(new Point(10, 10), 14, 1.0, false, 14 * 0.18), start[1]);

        var half = m.Circles(5.2)[1];
        Assert.True(Math.Abs(half.Radius - 14 * (1 + 1.4 * 0.5)) < 0.001);
        Assert.True(Math.Abs(half.Opacity - 0.5) < 0.001);

        Assert.Single(m.Circles(5.4));
        m.Prune(5.4);
        Assert.Equal(new Point(10, 10), m.Pointer);
    }

    [Fact]
    [Unit]
    public void AnimationOffMeansAClickOnlyMovesTheHalo()
    {
        var m = new ClickHighlightModel(new ClickHighlightSettings(animateClicks: false));
        m.Clicked(new Point(3, 4), 0);
        Assert.Single(m.Circles(0));
        Assert.Equal(new Point(3, 4), m.Pointer);
    }

    [Fact]
    [Unit]
    public void MappingConvertsScreenPointsToFramePixels()
    {
        var mapping = new FrameMapping(new Point(100, 50), 2, 2);
        Assert.Equal(new Point(0, 0), mapping.PixelPoint(new Point(100, 50)));
        Assert.Equal(new Point(400, 300), mapping.PixelPoint(new Point(300, 200)));

        var circle = mapping.PixelCircle(new HighlightCircle(new Point(110, 60), 22, 1, false, 3));
        Assert.Equal(new HighlightCircle(new Point(20, 20), 44, 1, false, 6), circle);

        var capped = new FrameMapping(new Point(0, 0), 0.5, 0.5);
        Assert.Equal(new Point(1280, 720), capped.PixelPoint(new Point(2560, 1440)));
    }
}
