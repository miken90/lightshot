// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class CanvasLayoutTests
{
    [Fact]
    [Unit]
    public void DisabledCanvasIsTheImageItself()
    {
        var style = new CanvasStyle(Enabled: false, Padding: 40, Aspect: AspectPreset.SixteenNine);
        var frame = CanvasLayout.Compute(300, 200, style);
        Assert.Equal(300, frame.Width);
        Assert.Equal(200, frame.Height);
        Assert.Equal(new Rect(0, 0, 300, 200), frame.ImageRect);
        Assert.Equal(1.0, frame.Scale);
    }

    [Fact]
    [Unit]
    public void AutoAddsPaddingAround()
    {
        var style = new CanvasStyle(Enabled: true, Aspect: AspectPreset.Auto, Padding: 40);
        var frame = CanvasLayout.Compute(100, 100, style);
        Assert.Equal(180, frame.Width);
        Assert.Equal(180, frame.Height);
        Assert.Equal(new Rect(40, 40, 100, 100), frame.ImageRect);
        Assert.Equal(1.0, frame.Scale);
    }

    [Fact]
    [Unit]
    public void WideRatioExpandsWidth()
    {
        var style = new CanvasStyle(Enabled: true, Aspect: AspectPreset.SixteenNine, Padding: 40);
        var frame = CanvasLayout.Compute(100, 100, style);
        Assert.Equal(320, frame.Width);
        Assert.Equal(180, frame.Height);
        Assert.Equal(new Rect(110, 40, 100, 100), frame.ImageRect);
        Assert.Equal(1.0, frame.Scale);
    }

    [Fact]
    [Unit]
    public void TallRatioExpandsHeight()
    {
        var style = new CanvasStyle(Enabled: true, Aspect: AspectPreset.NineSixteen, Padding: 40);
        var frame = CanvasLayout.Compute(100, 100, style);
        Assert.Equal(180, frame.Width);
        Assert.Equal(320, frame.Height);
        Assert.Equal(new Rect(40, 110, 100, 100), frame.ImageRect);
        Assert.Equal(1.0, frame.Scale);
    }

    [Fact]
    [Unit]
    public void SmallImageIsCenteredOnFixedSize()
    {
        var style = new CanvasStyle(
            Enabled: true,
            SizeMode: CanvasSizeMode.FixedSize,
            TargetWidth: 1920,
            TargetHeight: 1080,
            Padding: 40);
        var frame = CanvasLayout.Compute(100, 100, style);
        Assert.Equal(1920, frame.Width);
        Assert.Equal(1080, frame.Height);
        Assert.Equal(1.0, frame.Scale);
        Assert.Equal(new Rect(910, 490, 100, 100), frame.ImageRect);
    }

    [Fact]
    [Unit]
    public void LargeImageExpandsToTargetRatioByDefault()
    {
        var style = new CanvasStyle(
            Enabled: true,
            SizeMode: CanvasSizeMode.FixedSize,
            TargetWidth: 1920,
            TargetHeight: 1080,
            DownscaleToFit: false,
            Padding: 40);
        var frame = CanvasLayout.Compute(2000, 2000, style);
        Assert.Equal(3698, frame.Width);
        Assert.Equal(2080, frame.Height);
        Assert.Equal(1.0, frame.Scale);
        Assert.Equal(new Rect((3698 - 2000) / 2.0, (2080 - 2000) / 2.0, 2000, 2000), frame.ImageRect);
    }

    [Fact]
    [Unit]
    public void LargeImageDownscalesWhenAsked()
    {
        var style = new CanvasStyle(
            Enabled: true,
            SizeMode: CanvasSizeMode.FixedSize,
            TargetWidth: 1920,
            TargetHeight: 1080,
            DownscaleToFit: true,
            Padding: 40);
        var frame = CanvasLayout.Compute(2000, 2000, style);
        Assert.Equal(1920, frame.Width);
        Assert.Equal(1080, frame.Height);
        Assert.Equal(0.5, frame.Scale);
        Assert.Equal(new Rect(460, 40, 1000, 1000), frame.ImageRect);
    }

    [Fact]
    [Unit]
    public void EveryAspectPresetProducesItsRatio()
    {
        AspectPreset[] presets =
        [
            AspectPreset.Square,
            AspectPreset.FourThree,
            AspectPreset.ThreeTwo,
            AspectPreset.SixteenNine,
            AspectPreset.FiveThree,
            AspectPreset.NineSixteen,
            AspectPreset.ThreeFour,
            AspectPreset.TwoThree
        ];

        foreach (var preset in presets)
        {
            var style = new CanvasStyle(Enabled: true, Aspect: preset, Padding: 20);
            var frame = CanvasLayout.Compute(300, 300, style);
            double targetRatio = AspectPresets.Ratio(preset)!.Value;
            double actualRatio = frame.Width / (double)frame.Height;
            Assert.True(Math.Abs(actualRatio - targetRatio) < 0.02, $"Preset {preset} expected {targetRatio} but got {actualRatio}");
        }

        var autoStyle = new CanvasStyle(Enabled: true, Aspect: AspectPreset.Auto, Padding: 20);
        var autoFrame = CanvasLayout.Compute(300, 200, autoStyle);
        Assert.Equal(340, autoFrame.Width);
        Assert.Equal(240, autoFrame.Height);
    }
}
