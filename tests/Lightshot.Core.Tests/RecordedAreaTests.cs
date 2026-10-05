// Ported from LightshotKit/Tests/LightshotKitTests/RecordedAreaTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class RecordedAreaTests
{
    [Fact]
    public void APickedWindowRecordsItsAreaOfTheDisplayNotTheWindowAlone()
    {
        var frame = new Rect(300, 200, 800, 500);
        var region = new CaptureRegion.WindowRegion(4242, frame);
        Assert.Equal(new RecordedArea.SubArea(frame), region.RecordedArea);
    }

    [Fact]
    public void ADrawnRectRecordsThatAreaStandardized()
    {
        var flipped = new Rect(1100, 700, -800, -500);
        var region = new CaptureRegion.RectRegion(flipped);
        Assert.Equal(new RecordedArea.SubArea(new Rect(300, 200, 800, 500)), region.RecordedArea);
    }

    [Fact]
    public void ADisplayRecordsTheWholeDisplay()
    {
        var region = new CaptureRegion.DisplayRegion(7);
        Assert.Equal(new RecordedArea.DisplayArea(7), region.RecordedArea);
    }
}
