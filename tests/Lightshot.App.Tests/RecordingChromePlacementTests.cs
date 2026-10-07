// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.App.Views;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Tests;

/// <summary>
/// The recording toolbar, controls pill and post-recording overlay anchor inside the work area of the
/// monitor being recorded, not the primary's. WPF maps every window's DIPs to device pixels with the
/// primary's scale, so the expected physical frame is the DIP frame times that scale.
/// </summary>
public class RecordingChromePlacementTests
{
    private static DisplayInfo Display(uint id, Rect bounds, double scale, bool primary, double taskbar = 48)
    {
        var workArea = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height - taskbar);
        uint dpi = (uint)Math.Round(96 * scale);
        return new DisplayInfo(id, $@"\\.\DISPLAY{id}", (IntPtr)id, bounds, workArea, scale, dpi, dpi, primary, 0);
    }

    // The reporting desktop: 150% primary, 100% secondaries to the right and at a negative X.
    private static readonly DisplayInfo[] Mixed =
    [
        Display(1, new Rect(0, 0, 2560, 1600), 1.5, true, 72),
        Display(5, new Rect(2560, 0, 2532, 1170), 1.0, false),
        Display(2, new Rect(-1920, 0, 1920, 1080), 1.0, false),
    ];

    private const double WindowScale = 1.5;

    public static TheoryData<uint, PlacementAnchor> Cases()
    {
        var data = new TheoryData<uint, PlacementAnchor>();
        foreach (var d in Mixed)
        {
            foreach (var anchor in Enum.GetValues<PlacementAnchor>()) data.Add(d.DisplayId, anchor);
        }
        return data;
    }

    private static DisplayInfo ById(uint id) => Array.Find(Mixed, d => d.DisplayId == id)!;

    [Theory]
    [Unit]
    [MemberData(nameof(Cases))]
    public void ChromeAnchorsInsideTheWorkAreaOfTheRecordedMonitor(uint displayId, PlacementAnchor anchor)
    {
        var target = ById(displayId);
        // A region on the target monitor resolves to it, whichever monitor is primary
        var recorded = RecordingDisplayResolver.Resolve(Mixed, new CaptureRegion.DisplayRegion(displayId)).Display;
        Assert.Equal(target.DisplayId, recorded.DisplayId);

        var frame = WindowPlacement.CalculateAnchoredFrame(recorded.WorkArea, WindowScale, 520, 64, anchor, 24);
        var physical = Lightshot.Platform.Windows.Displays.DisplayMath.DipToPhysical(frame, WindowScale);

        Assert.True(
            physical.MinX >= target.WorkArea.MinX - 0.5 && physical.MinY >= target.WorkArea.MinY - 0.5 &&
            physical.MaxX <= target.WorkArea.MaxX + 0.5 && physical.MaxY <= target.WorkArea.MaxY + 0.5,
            $"{anchor} frame {physical} is not inside {target.DeviceName} work area {target.WorkArea}.");
    }

    [Fact]
    [Unit]
    public void AnchorsSitOnTheirEdgesOfTheSecondaryNotThePrimary()
    {
        var left = ById(2).WorkArea;
        var bottom = Lightshot.Platform.Windows.Displays.DisplayMath.DipToPhysical(
            WindowPlacement.CalculateAnchoredFrame(left, WindowScale, 300, 60, PlacementAnchor.BottomCenter, 24), WindowScale);
        var top = Lightshot.Platform.Windows.Displays.DisplayMath.DipToPhysical(
            WindowPlacement.CalculateAnchoredFrame(left, WindowScale, 300, 60, PlacementAnchor.TopCenter, 24), WindowScale);
        var corner = Lightshot.Platform.Windows.Displays.DisplayMath.DipToPhysical(
            WindowPlacement.CalculateAnchoredFrame(left, WindowScale, 300, 60, PlacementAnchor.BottomRight, 16), WindowScale);

        Assert.Equal(left.Center.X, bottom.Center.X, 3);
        Assert.Equal(left.MaxY - 24 * WindowScale, bottom.MaxY, 3);
        Assert.Equal(left.MinY + 24 * WindowScale, top.MinY, 3);
        Assert.Equal(left.MaxX - 16 * WindowScale, corner.MaxX, 3);
    }

    [Fact]
    [Unit]
    public void ALaterTakeOnAnotherMonitorMovesTheAnchor()
    {
        var first = RecordingDisplayResolver.Resolve(Mixed, new CaptureRegion.RectRegion(new Rect(100, 100, 400, 300))).Display;
        var second = RecordingDisplayResolver.Resolve(Mixed, new CaptureRegion.RectRegion(new Rect(-1500, 100, 400, 300))).Display;
        var third = RecordingDisplayResolver.Resolve(Mixed, new CaptureRegion.RectRegion(new Rect(3000, 100, 400, 300))).Display;

        Assert.Equal(new uint[] { 1, 2, 5 }, new[] { first.DisplayId, second.DisplayId, third.DisplayId });

        var frames = new List<Rect>();
        foreach (var d in new[] { first, second, third })
        {
            frames.Add(WindowPlacement.CalculateAnchoredFrame(d.WorkArea, WindowScale, 520, 64, PlacementAnchor.BottomCenter, 24));
        }
        Assert.NotEqual(frames[0], frames[1]);
        Assert.NotEqual(frames[1], frames[2]);
        Assert.True(frames[1].MaxX * WindowScale <= 0, "Left monitor frame must sit at negative X.");
    }
}
