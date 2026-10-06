// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.TestSupport;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Tests;

public class RecordingSelectionTests
{
    private static readonly Rect LargeDisplay = new(0, 0, 2560, 1440);
    private static readonly Rect SmallDisplay = new(0, 0, 1440, 900);
    private static readonly Rect StandardHdDisplay = new(0, 0, 1920, 1080);

    // =========================================================================
    // 1. Default 720p Rect Tests
    // =========================================================================

    [Fact]
    [Unit]
    public void DefaultRecordingRect_Centered720pOnLargeDisplay()
    {
        // Spec & Core: On large displays (e.g. 2560x1440), default rect is 1280x720 centered
        var rect = RecordingSelectionPainter.GetDefaultRecordingRect(LargeDisplay);

        Assert.Equal(1280.0, rect.Width);
        Assert.Equal(720.0, rect.Height);
        Assert.Equal((2560 - 1280) / 2.0, rect.MinX);
        Assert.Equal((1440 - 720) / 2.0, rect.MinY);
    }

    [Fact]
    [Unit]
    public void DefaultRecordingRect_SmallDisplay_SixtyPercentWide16By9()
    {
        // On small displays (e.g. 1440x900), width is 60% (864), height is 16:9 (486)
        var rect = RecordingSelectionPainter.GetDefaultRecordingRect(SmallDisplay);

        Assert.Equal(864.0, rect.Width);
        Assert.Equal(486.0, rect.Height);
        Assert.Equal(720.0, rect.MidX);
        Assert.Equal(450.0, rect.MidY);
    }

    [Fact]
    [Unit]
    public void Session_InitializesWithDefault720pRect()
    {
        var session = new RecordingSelectionSession(LargeDisplay, displayId: 1);

        Assert.True(session.HasSelection);
        Assert.NotNull(session.CurrentRect);
        Assert.Equal(new Rect(640, 360, 1280, 720), session.CurrentRect.Value);
    }

    // =========================================================================
    // 2. Nudge Tests: 1 px and 10 px
    // =========================================================================

    [Fact]
    [Unit]
    public void Nudge_1Px_MovesSelectionByExactOnePixel()
    {
        var session = new RecordingSelectionSession(StandardHdDisplay, displayId: 1, initialRect: new Rect(100, 100, 400, 300));
        var initial = session.CurrentRect!.Value;

        // Move right 1px
        session.Nudge(1.0, 0.0, largeStep: false);
        Assert.Equal(new Rect(initial.MinX + 1.0, initial.MinY, 400, 300), session.CurrentRect.Value);

        // Move down 1px
        session.Nudge(0.0, 1.0, largeStep: false);
        Assert.Equal(new Rect(initial.MinX + 1.0, initial.MinY + 1.0, 400, 300), session.CurrentRect.Value);

        // Move left 1px, up 1px
        session.Nudge(-1.0, -1.0, largeStep: false);
        Assert.Equal(initial, session.CurrentRect.Value);
    }

    [Fact]
    [Unit]
    public void Nudge_10Px_MovesSelectionByTenPixels()
    {
        var session = new RecordingSelectionSession(StandardHdDisplay, displayId: 1, initialRect: new Rect(200, 200, 500, 350));
        var initial = session.CurrentRect!.Value;

        // Shift + arrow: largeStep = true moves by 10 px
        session.Nudge(1.0, 0.0, largeStep: true);
        Assert.Equal(new Rect(initial.MinX + 10.0, initial.MinY, 500, 350), session.CurrentRect.Value);

        session.Nudge(0.0, 1.0, largeStep: true);
        Assert.Equal(new Rect(initial.MinX + 10.0, initial.MinY + 10.0, 500, 350), session.CurrentRect.Value);

        session.Nudge(-2.0, -3.0, largeStep: true);
        Assert.Equal(new Rect(initial.MinX - 10.0, initial.MinY - 20.0, 500, 350), session.CurrentRect.Value);
    }

    // =========================================================================
    // 3. Aspect Lock Tests
    // =========================================================================

    [Theory]
    [Unit]
    [InlineData(AspectRatio.R16x9, 16.0 / 9.0)]
    [InlineData(AspectRatio.R4x3, 4.0 / 3.0)]
    [InlineData(AspectRatio.R1x1, 1.0)]
    [InlineData(AspectRatio.R9x16, 9.0 / 16.0)]
    [InlineData(AspectRatio.R5x4, 5.0 / 4.0)]
    public void AspectLock_PreservesConfiguredRatiosFromCore(AspectRatio ratio, double expectedValue)
    {
        Assert.Equal(expectedValue, ratio.Value()!.Value);

        var session = new RecordingSelectionSession(LargeDisplay, displayId: 1, initialRect: new Rect(100, 100, 800, 600));
        session.SetRatio(ratio);

        var r = session.CurrentRect!.Value;
        double actualRatio = r.Width / r.Height;
        Assert.True(Math.Abs(actualRatio - expectedValue) < 0.01,
            $"Expected ratio {expectedValue} for {ratio}, got {actualRatio}");
    }

    [Fact]
    [Unit]
    public void AspectLock_FreeformAllowsArbitraryDimensions()
    {
        var session = new RecordingSelectionSession(LargeDisplay, displayId: 1, initialRect: new Rect(100, 100, 800, 300));
        session.SetRatio(AspectRatio.Freeform);

        Assert.Equal(800.0, session.CurrentRect!.Value.Width);
        Assert.Equal(300.0, session.CurrentRect!.Value.Height);
        Assert.Null(session.Ratio.Value());
    }

    // =========================================================================
    // 4. Typed Size Tests
    // =========================================================================

    [Fact]
    [Unit]
    public void TypedSize_Freeform_SetsWidthAndHeightIndependently()
    {
        var session = new RecordingSelectionSession(LargeDisplay, displayId: 1, initialRect: new Rect(50, 50, 600, 400));
        session.SetRatio(AspectRatio.Freeform);

        session.SetWidth(950);
        Assert.Equal(950.0, session.CurrentRect!.Value.Width);
        Assert.Equal(400.0, session.CurrentRect!.Value.Height);

        session.SetHeight(520);
        Assert.Equal(950.0, session.CurrentRect!.Value.Width);
        Assert.Equal(520.0, session.CurrentRect!.Value.Height);
    }

    [Fact]
    [Unit]
    public void TypedSize_LockedRatio_AdjustsOppositeDimension()
    {
        var session = new RecordingSelectionSession(LargeDisplay, displayId: 1, initialRect: new Rect(100, 100, 800, 450));
        session.SetRatio(AspectRatio.R16x9);

        // Setting width to 1280 automatically sets height to 720 (16:9)
        session.SetWidth(1280);
        Assert.Equal(1280.0, session.CurrentRect!.Value.Width);
        Assert.Equal(720.0, session.CurrentRect!.Value.Height);

        // Setting height to 900 automatically sets width to 1600 (16:9)
        session.SetHeight(900);
        Assert.Equal(1600.0, session.CurrentRect!.Value.Width);
        Assert.Equal(900.0, session.CurrentRect!.Value.Height);
    }

    [Fact]
    [Unit]
    public void TypedSize_SetSizeConvenience_UpdatesBothDimensions()
    {
        var session = new RecordingSelectionSession(LargeDisplay, displayId: 1, initialRect: new Rect(100, 100, 500, 300));
        session.SetSize(700, 450);

        Assert.Equal(700.0, session.CurrentRect!.Value.Width);
        Assert.Equal(450.0, session.CurrentRect!.Value.Height);
    }

    // =========================================================================
    // 5. Display Confinement Tests
    // =========================================================================

    [Fact]
    [Unit]
    public void DisplayConfinement_ConfinesToDisplayWithLargestOverlap()
    {
        // Topology with 2 displays side by side:
        // Display 1: (0, 0, 1920, 1080)
        // Display 2: (1920, 0, 1920, 1080)
        var disp1 = new DisplayInfo(1, @"\\.\DISPLAY1", (IntPtr)1, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 1.0, 96, 96, true, 0);
        var disp2 = new DisplayInfo(2, @"\\.\DISPLAY2", (IntPtr)2, new Rect(1920, 0, 1920, 1080), new Rect(1920, 0, 1920, 1040), 1.0, 96, 96, false, 0);
        var displays = new List<DisplayInfo> { disp1, disp2 };

        // Test A: Rect primarily in Display 2: (1800, 200, 800, 600)
        // Overlap with Display 1: 120 * 600 = 72,000
        // Overlap with Display 2: 680 * 600 = 408,000
        // Display 2 has largest overlap -> must be confined entirely inside Display 2 [1920..3840]
        var targetA = new Rect(1800, 200, 800, 600);
        var confinedA = RecordingSelectionPainter.ConfineToDisplayWithLargestOverlap(targetA, displays);

        Assert.True(confinedA.MinX >= disp2.Bounds.MinX, $"MinX {confinedA.MinX} must be >= {disp2.Bounds.MinX}");
        Assert.True(confinedA.MaxX <= disp2.Bounds.MaxX, $"MaxX {confinedA.MaxX} must be <= {disp2.Bounds.MaxX}");
        Assert.True(confinedA.MinY >= disp2.Bounds.MinY, $"MinY {confinedA.MinY} must be >= {disp2.Bounds.MinY}");
        Assert.True(confinedA.MaxY <= disp2.Bounds.MaxY, $"MaxY {confinedA.MaxY} must be <= {disp2.Bounds.MaxY}");
        Assert.Equal(800.0, confinedA.Width);
        Assert.Equal(600.0, confinedA.Height);

        // Test B: Rect primarily in Display 1: (1500, 100, 600, 400)
        // Overlap with Display 1: 420 * 400 = 168,000
        // Overlap with Display 2: 180 * 400 = 72,000
        // Display 1 has largest overlap -> must be confined inside Display 1 [0..1920]
        var targetB = new Rect(1500, 100, 600, 400);
        var confinedB = RecordingSelectionPainter.ConfineToDisplayWithLargestOverlap(targetB, displays);

        Assert.True(confinedB.MinX >= disp1.Bounds.MinX);
        Assert.True(confinedB.MaxX <= disp1.Bounds.MaxX);
        Assert.Equal(600.0, confinedB.Width);
        Assert.Equal(400.0, confinedB.Height);
    }

    // =========================================================================
    // 6. Window Pick Records Its Area Tests
    // =========================================================================

    [Fact]
    [Unit]
    public void WindowPick_RecordsItsArea()
    {
        var windowFrame = new Rect(250, 150, 800, 550);
        var session = new RecordingSelectionSession(StandardHdDisplay, displayId: 1);

        session.PickWindow(42, windowFrame);

        Assert.NotNull(session.SnappedWindow);
        Assert.Equal((uint)42, session.SnappedWindow.Id);
        Assert.Equal(windowFrame, session.CurrentRect);

        var region = session.ResolveRegion();
        Assert.NotNull(region);
        Assert.IsType<CaptureRegion.WindowRegion>(region);

        var wr = (CaptureRegion.WindowRegion)region;
        Assert.Equal((uint)42, wr.Id);
        Assert.Equal(windowFrame, wr.Frame);

        // Spec verification: A picked window records its area (RecordedArea.SubArea)
        Assert.Equal(new RecordedArea.SubArea(windowFrame), wr.RecordedArea);
    }

    [Fact]
    [Unit]
    public void WindowPick_EditingBreaksSnapBackToRect()
    {
        var windowFrame = new Rect(200, 200, 600, 400);
        var session = new RecordingSelectionSession(StandardHdDisplay, displayId: 1);
        session.PickWindow(99, windowFrame);

        Assert.NotNull(session.SnappedWindow);

        // Nudging breaks the snapped window link
        session.Nudge(1.0, 0.0);
        Assert.Null(session.SnappedWindow);

        var region = session.ResolveRegion();
        Assert.NotNull(region);
        Assert.IsType<CaptureRegion.RectRegion>(region);
    }

    // =========================================================================
    // 7. Session Resolves to RecordingChoice
    // =========================================================================

    [Fact]
    [Unit]
    public void Session_ResolvesToRecordingChoice_VideoAndGif()
    {
        var session = new RecordingSelectionSession(StandardHdDisplay, displayId: 1, initialRect: new Rect(100, 100, 1280, 720));

        var choiceVideo = session.ToRecordingChoice(RecordingOutputKind.Video);
        Assert.NotNull(choiceVideo);
        Assert.Equal(RecordingOutputKind.Video, choiceVideo.Output);
        Assert.Equal(new Rect(100, 100, 1280, 720), ((CaptureRegion.RectRegion)choiceVideo.Region).Rect);

        var choiceGif = session.ToRecordingChoice(RecordingOutputKind.Gif);
        Assert.NotNull(choiceGif);
        Assert.Equal(RecordingOutputKind.Gif, choiceGif.Output);
    }
}
