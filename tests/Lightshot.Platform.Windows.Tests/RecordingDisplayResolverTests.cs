// Ported from LightshotKit/Tests/LightshotKitTests/RecordingCoordinatorTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class RecordingDisplayResolverTests
{
    private readonly DisplayInfo _primaryDisplay = new(
        DisplayId: 1,
        DeviceName: @"\\.\DISPLAY1",
        HMonitor: (IntPtr)101,
        Bounds: new Rect(0, 0, 1920, 1080),
        WorkArea: new Rect(0, 0, 1920, 1040),
        ScaleFactor: 1.0,
        DpiX: 96,
        DpiY: 96,
        IsPrimary: true,
        AdapterLuid: 1001
    );

    private readonly DisplayInfo _secondaryDisplay = new(
        DisplayId: 2,
        DeviceName: @"\\.\DISPLAY2",
        HMonitor: (IntPtr)102,
        Bounds: new Rect(-1920, 0, 1920, 1080),
        WorkArea: new Rect(-1920, 0, 1920, 1040),
        ScaleFactor: 1.0,
        DpiX: 96,
        DpiY: 96,
        IsPrimary: false,
        AdapterLuid: 1001
    );

    [Fact]
    [Unit]
    public void PicksLargestOverlapNotPrimary()
    {
        var displays = new List<DisplayInfo> { _primaryDisplay, _secondaryDisplay };

        // Rect positioned entirely on the secondary display (negative X space)
        var rectOnSecondary = new Rect(-1200, 200, 800, 600);
        var region = new CaptureRegion.RectRegion(rectOnSecondary);

        var resolved = RecordingDisplayResolver.Resolve(displays, region);

        Assert.Equal(2u, resolved.Display.DisplayId);
        Assert.False(resolved.Display.IsPrimary);
        Assert.Equal(new Rect(-1200, 200, 800, 600), resolved.GlobalRect);
        // Local coordinates relative to secondary display origin (-1920, 0) -> X: -1200 - (-1920) = 720
        Assert.Equal(new Rect(720, 200, 800, 600), resolved.LocalRect);
        Assert.Equal(800, resolved.OutputWidth);
        Assert.Equal(600, resolved.OutputHeight);
    }

    [Fact]
    [Unit]
    public void StraddlingBoundaryPicksLargestOverlapDisplay()
    {
        var displays = new List<DisplayInfo> { _primaryDisplay, _secondaryDisplay };

        // Rect straddling boundary: 300px on secondary (-300..0), 700px on primary (0..700)
        var rectMostlyPrimary = new Rect(-300, 100, 1000, 500);
        var resolvedPrimary = RecordingDisplayResolver.Resolve(displays, rectMostlyPrimary);
        Assert.Equal(1u, resolvedPrimary.Display.DisplayId);
        Assert.True(resolvedPrimary.Display.IsPrimary);
        // Clamped to primary bounds: X: 0..700, width: 700
        Assert.Equal(new Rect(0, 100, 700, 500), resolvedPrimary.GlobalRect);
        Assert.Equal(new Rect(0, 100, 700, 500), resolvedPrimary.LocalRect);

        // Rect straddling boundary: 700px on secondary (-700..0), 300px on primary (0..300)
        var rectMostlySecondary = new Rect(-700, 100, 1000, 500);
        var resolvedSecondary = RecordingDisplayResolver.Resolve(displays, rectMostlySecondary);
        Assert.Equal(2u, resolvedSecondary.Display.DisplayId);
        Assert.False(resolvedSecondary.Display.IsPrimary);
        // Clamped to secondary bounds: X: -700..0, width: 700 -> Local X: -700 - (-1920) = 1220
        Assert.Equal(new Rect(-700, 100, 700, 500), resolvedSecondary.GlobalRect);
        Assert.Equal(new Rect(1220, 100, 700, 500), resolvedSecondary.LocalRect);
    }

    [Fact]
    [Unit]
    public void DisplayRegionResolvesSpecificDisplay()
    {
        var displays = new List<DisplayInfo> { _primaryDisplay, _secondaryDisplay };

        var region = new CaptureRegion.DisplayRegion(2);
        var resolved = RecordingDisplayResolver.Resolve(displays, region);

        Assert.Equal(2u, resolved.Display.DisplayId);
        Assert.Equal(new Rect(-1920, 0, 1920, 1080), resolved.GlobalRect);
        Assert.Equal(new Rect(0, 0, 1920, 1080), resolved.LocalRect);
        Assert.Equal(1920, resolved.OutputWidth);
        Assert.Equal(1080, resolved.OutputHeight);
    }

    [Fact]
    [Unit]
    public void WindowRegionResolvesLargestOverlapDisplay()
    {
        var displays = new List<DisplayInfo> { _primaryDisplay, _secondaryDisplay };

        var windowFrame = new Rect(-1500, 150, 1000, 700);
        var region = new CaptureRegion.WindowRegion(42, windowFrame);
        var resolved = RecordingDisplayResolver.Resolve(displays, region);

        Assert.Equal(2u, resolved.Display.DisplayId);
        Assert.Equal(new Rect(-1500, 150, 1000, 700), resolved.GlobalRect);
        Assert.Equal(new Rect(420, 150, 1000, 700), resolved.LocalRect);
    }

    [Fact]
    [Unit]
    public void MaxResolutionCapsOutputDimensionsToEvenValues()
    {
        var displays = new List<DisplayInfo> { _primaryDisplay, _secondaryDisplay };

        // 3840x2160 area capped to P1080 (1920 max edge)
        var largeRect = new Rect(0, 0, 3840, 2160);
        var resolved = RecordingDisplayResolver.Resolve(displays, largeRect, MaxResolution.P1080);

        Assert.Equal(1920, resolved.OutputWidth);
        Assert.Equal(1080, resolved.OutputHeight);
        Assert.True(resolved.OutputWidth % 2 == 0);
        Assert.True(resolved.OutputHeight % 2 == 0);

        // Odd rect capped to P720 (1280 max edge)
        var oddRect = new Rect(0, 0, 1921, 1081);
        var resolved720 = RecordingDisplayResolver.Resolve(displays, oddRect, MaxResolution.P720);
        Assert.True(resolved720.OutputWidth <= 1280);
        Assert.True(resolved720.OutputWidth % 2 == 0);
        Assert.True(resolved720.OutputHeight % 2 == 0);
    }
}
