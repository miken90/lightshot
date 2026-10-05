// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class DisplayMathTests
{
    [Fact]
    [Unit]
    public void MapsRectAcrossMixedDpi()
    {
        // Monitor 1: 1920x1080 at 100% scale (DIP = Physical: 0, 0, 1920, 1080)
        var monitor1 = new DisplayInfo(
            DisplayId: 1,
            DeviceName: @"\\.\DISPLAY1",
            HMonitor: (IntPtr)1,
            Bounds: new Rect(0, 0, 1920, 1080),
            WorkArea: new Rect(0, 0, 1920, 1040),
            ScaleFactor: 1.0,
            DpiX: 96,
            DpiY: 96,
            IsPrimary: true,
            AdapterLuid: 0
        );

        // Monitor 2: 3840x2160 at 200% scale (scale 2.0, physical X=1920..5760, Y=0..2160)
        var monitor2 = new DisplayInfo(
            DisplayId: 2,
            DeviceName: @"\\.\DISPLAY2",
            HMonitor: (IntPtr)2,
            Bounds: new Rect(1920, 0, 3840, 2160),
            WorkArea: new Rect(1920, 0, 3840, 2100),
            ScaleFactor: 2.0,
            DpiX: 192,
            DpiY: 192,
            IsPrimary: false,
            AdapterLuid: 0
        );

        var displays = new[] { monitor1, monitor2 };

        // Test DIP rect on monitor 1
        var dipRect1 = new Rect(100, 100, 400, 300);
        var physRect1 = DisplayMath.MapDipRectToPhysical(dipRect1, displays);
        Assert.Equal(100, physRect1.X);
        Assert.Equal(100, physRect1.Y);
        Assert.Equal(400, physRect1.Width);
        Assert.Equal(300, physRect1.Height);

        // Test DIP rect on monitor 2:
        // Monitor 2 DIP bounds: (1920/2, 0, 3840/2, 2160/2) = (960, 0, 1920, 1080)
        // Rect at DIP (2000, 100, 300, 200) strictly falls inside monitor 2 (960..2880) and outside monitor 1 (0..1920)
        var dipRect2 = new Rect(2000, 100, 300, 200);
        var physRect2 = DisplayMath.MapDipRectToPhysical(dipRect2, displays);
        // physX = 1920 + (2000 - 960) * 2 = 1920 + 1040 * 2 = 4000
        Assert.Equal(4000, physRect2.X);
        Assert.Equal(200, physRect2.Y);
        Assert.Equal(600, physRect2.Width);
        Assert.Equal(400, physRect2.Height);

        // Test DIP rect overlapping both: largest overlap wins
        // Rect at (900, 0, 200, 200):
        // Overlap with monitor 1 (0..1920): 900..1100 = 200 * 200 = 40000
        // Overlap with monitor 2 (960..2880): 960..1100 = 140 * 200 = 28000
        // Monitor 1 has larger overlap, scale = 1.0
        var dipOverlap = new Rect(900, 0, 200, 200);
        var physOverlap = DisplayMath.MapDipRectToPhysical(dipOverlap, displays);
        Assert.Equal(900, physOverlap.X);
        Assert.Equal(0, physOverlap.Y);
        Assert.Equal(200, physOverlap.Width);
        Assert.Equal(200, physOverlap.Height);
    }
}
