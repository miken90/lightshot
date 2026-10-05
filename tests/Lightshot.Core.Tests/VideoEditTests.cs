// Ported from LightshotKit/Tests/LightshotKitTests/VideoEditTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class VideoEditTests
{
    [Fact]
    [Unit]
    public void ATrimIsClampedToTheClipAndNeverShorterThanHalfASecond()
    {
        var t = new TrimRange(10);
        Assert.True(t.IsWholeClip && t.Length == 10);

        t.SetStart(-3);
        Assert.Equal(0.0, t.Start);

        t.SetEnd(42);
        Assert.Equal(10.0, t.End);

        t.SetStart(9.8);
        Assert.Equal(9.5, t.Start);

        t.SetEnd(9.6);
        Assert.Equal(10.0, t.End);

        t.SetStart(2);
        t.SetEnd(4);
        Assert.True(t.Start == 2 && t.End == 4 && !t.IsWholeClip);

        Assert.Equal(new TrimRange(5, 5.5, 10), new TrimRange(5, 3, 10));

        var @short = new TrimRange(0.1, 0.2, 0.3);
        Assert.True(@short.Start == 0 && @short.End == 0.3);
    }

    [Fact]
    [Unit]
    public void PresetsCapTheLongestEdgeKeepingAspectWithEvenEdgesNeverUp()
    {
        var source = new Size(2560, 1440);
        Assert.Equal(source, VideoDimensions.CalculateSize(DimensionPreset.Original, source));
        Assert.Equal(new Size(1920, 1080), VideoDimensions.CalculateSize(DimensionPreset.P1080, source));
        Assert.Equal(new Size(1280, 720), VideoDimensions.CalculateSize(DimensionPreset.P720, source));
        Assert.Equal(new Size(854, 480), VideoDimensions.CalculateSize(DimensionPreset.P480, source));
        Assert.Equal(new Size(800, 600), VideoDimensions.CalculateSize(DimensionPreset.P1080, new Size(800, 600)));

        var tall = new Size(1080, 2340);
        Assert.Equal(new Size(590, 1280), VideoDimensions.CalculateSize(DimensionPreset.P720, tall));
        Assert.Equal(new Size(800, 600), VideoDimensions.CalculateSize(DimensionPreset.Original, new Size(801, 601)));
    }

    [Fact]
    [Unit]
    public void ATypedEdgeKeepsTheAspectAndIsClampedToTheSource()
    {
        var source = new Size(1600, 900);
        Assert.Equal(new Size(800, 450), VideoDimensions.CalculateSize(800, null, source));
        Assert.Equal(new Size(532, 300), VideoDimensions.CalculateSize(null, 300, source));
        Assert.Equal(new Size(1600, 900), VideoDimensions.CalculateSize(4000, null, source));
        Assert.Equal(new Size(640, 360), VideoDimensions.CalculateSize(640, 640, source));
        Assert.Equal(new Size(800, 450), VideoDimensions.CalculateSize(3200, 450, source));
        Assert.Equal(source, VideoDimensions.CalculateSize(null, null, source));
        Assert.Equal(new Size(2, 2), VideoDimensions.CalculateSize(1, null, source));
    }

    [Fact]
    [Unit]
    public void TheBitRateCurvePassesThroughTheRecordersRateAtTheDefaultQuality()
    {
        Assert.Equal(0.1, VideoBitRate.BitsPerPixel(VideoBitRate.DefaultQuality));
        Assert.Equal(0.025, VideoBitRate.BitsPerPixel(0));
        Assert.Equal(0.2, VideoBitRate.BitsPerPixel(1));
        Assert.True(VideoBitRate.BitsPerPixel(0.25) < VideoBitRate.BitsPerPixel(0.75));

        double rate = VideoBitRate.VideoBitsPerSecond(new Size(1920, 1080), 30, 0.5);
        Assert.Equal(1920.0 * 1080 * 30 * 0.1, rate);
        Assert.Equal(VideoBitRate.MinimumBitsPerSecond, VideoBitRate.VideoBitsPerSecond(new Size(100, 100), 1, 0));
    }

    [Fact]
    [Unit]
    public void TheEstimateIsBitRateTimesTheCutPlusOverhead()
    {
        var size = new Size(1280, 720);
        var settings = new VideoEditSettings(new TrimRange(2, 12, 30), size, 0.5, AudioEdit.Unchanged.Instance);
        double video = 1280.0 * 720 * 30 * 0.1;
        Assert.Equal((video + 2 * 64_000) * 10 / 8 + SizeEstimator.ContainerOverheadBytes, SizeEstimator.EstimatedBytes(settings, 30, 2));

        settings.Audio = AudioEdit.Mono.Instance;
        Assert.Equal((video + 64_000) * 10 / 8 + SizeEstimator.ContainerOverheadBytes, SizeEstimator.EstimatedBytes(settings, 30, 2));

        settings.Audio = AudioEdit.Remove.Instance;
        Assert.Equal(video * 10 / 8 + SizeEstimator.ContainerOverheadBytes, SizeEstimator.EstimatedBytes(settings, 30, 2));

        var smaller = new VideoEditSettings(new TrimRange(2, 5, 30), new Size(640, 360), 0.2, AudioEdit.Remove.Instance);
        Assert.True(SizeEstimator.EstimatedBytes(smaller, 30, 0) < SizeEstimator.EstimatedBytes(settings, 30, 0));

        Assert.Equal(1000.0, SizeEstimator.EstimatedTrimOnlyBytes(3_000, new TrimRange(0, 10, 30)));
    }
}
