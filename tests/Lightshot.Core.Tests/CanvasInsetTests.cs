// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class CanvasInsetTests
{
    [Fact]
    public void InsetExpandsFrameDimensions()
    {
        var style = new CanvasStyle(
            Enabled: true,
            Padding: 20,
            Inset: 10,
            Aspect: AspectPreset.Auto);

        var frame = CanvasLayout.Compute(100, 80, style);

        // fw = 100 + 2*10 = 120, fh = 80 + 2*10 = 100
        // CanvasFrame Width = 120 + 2*20 = 160, Height = 100 + 2*20 = 140
        Assert.Equal(160, frame.Width);
        Assert.Equal(140, frame.Height);
        Assert.Equal(20.0, frame.ImageRect.MinX);
        Assert.Equal(20.0, frame.ImageRect.MinY);
        Assert.Equal(120.0, frame.ImageRect.Width);
        Assert.Equal(100.0, frame.ImageRect.Height);
        Assert.Equal(1.0, frame.Scale);
    }

    [Fact]
    public void ZeroInsetPreservesStandardFrame()
    {
        var styleWithZeroInset = new CanvasStyle(
            Enabled: true,
            Padding: 20,
            Inset: 0,
            Aspect: AspectPreset.Auto);

        var styleDefault = new CanvasStyle(
            Enabled: true,
            Padding: 20,
            Aspect: AspectPreset.Auto);

        var frameZero = CanvasLayout.Compute(100, 80, styleWithZeroInset);
        var frameDefault = CanvasLayout.Compute(100, 80, styleDefault);

        Assert.Equal(frameDefault.Width, frameZero.Width);
        Assert.Equal(frameDefault.Height, frameZero.Height);
        Assert.Equal(frameDefault.ImageRect, frameZero.ImageRect);
        Assert.Equal(frameDefault.Scale, frameZero.Scale);

        Assert.Equal(140, frameZero.Width);
        Assert.Equal(120, frameZero.Height);
        Assert.Equal(100.0, frameZero.ImageRect.Width);
        Assert.Equal(80.0, frameZero.ImageRect.Height);
    }
}
