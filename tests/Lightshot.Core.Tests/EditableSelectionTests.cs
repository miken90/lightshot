// Ported from LightshotKit/Tests/LightshotKitTests/EditableSelectionTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class EditableSelectionTests
{
    private static readonly Rect TestBounds = new(0, 0, 1000, 800);

    private static EditableSelection Drawn(Point a, Point b, AspectRatio ratio = AspectRatio.Freeform, bool square = false)
    {
        var s = new EditableSelection(TestBounds, ratio);
        s.DragBegan(a);
        s.DragEnded(b, forceSquare: square);
        return s;
    }

    [Fact]
    [Unit]
    public void ADragDrawsAStandardizedRectAndReleaseKeepsItEditable()
    {
        var s = Drawn(new Point(300, 200), new Point(100, 50));
        Assert.Equal(new Rect(100, 50, 200, 150), s.Rect);
        Assert.True(s.HasSelection);
        Assert.False(s.IsDragging);
        Assert.Equal(EditableSelection.DragKind.Move.Instance, s.DetermineDragKind(new Point(200, 100)));
    }

    [Fact]
    [Unit]
    public void ADrawStartingOutsideASelectionReplacesIt()
    {
        var s = Drawn(new Point(100, 100), new Point(300, 200));
        s.DragBegan(new Point(500, 500));
        s.DragEnded(new Point(600, 650));
        Assert.Equal(new Rect(500, 500, 100, 150), s.Rect);
    }

    [Fact]
    [Unit]
    public void ADegenerateDragLeavesNoSelection()
    {
        var s = Drawn(new Point(10, 10), new Point(12, 11));
        Assert.Null(s.Rect);
        Assert.False(s.HasSelection);
    }

    [Fact]
    [Unit]
    public void DrawingIsClampedToTheBounds()
    {
        var s = Drawn(new Point(900, 700), new Point(1500, 1200));
        Assert.Equal(new Rect(900, 700, 100, 100), s.Rect);
    }

    [Fact]
    [Unit]
    public void ARatioLockConstrainsTheInitialDragFromTheAnchor()
    {
        var s = Drawn(new Point(100, 100), new Point(420, 150), ratio: AspectRatio.R16x9);
        Assert.Equal(new Rect(100, 100, 320, 180), s.Rect);

        var up = Drawn(new Point(500, 500), new Point(340, 450), ratio: AspectRatio.R16x9);
        Assert.Equal(new Rect(340, 410, 160, 90), up.Rect);
    }

    [Fact]
    [Unit]
    public void ARatioLockedDragShrinksToFitTheBoundsInsteadOfUnlocking()
    {
        var s = Drawn(new Point(800, 100), new Point(1000, 900), ratio: AspectRatio.R1x1);
        Assert.Equal(new Rect(800, 100, 200, 200), s.Rect);
        Assert.Equal(s.Rect!.Value.Width, s.Rect!.Value.Height);
    }

    [Fact]
    [Unit]
    public void OptionForcesASquareForThatDragOnly()
    {
        var s = Drawn(new Point(0, 0), new Point(300, 100), square: true);
        Assert.Equal(new Rect(0, 0, 300, 300), s.Rect);
        Assert.Equal(AspectRatio.Freeform, s.Ratio);
    }

    [Fact]
    [Unit]
    public void HandlesAreHitWithinTheGrabRadiusAndTheInsideMoves()
    {
        var s = Drawn(new Point(100, 100), new Point(300, 200));
        Assert.Equal(new EditableSelection.DragKind.Resize(Handle.BottomRight), s.DetermineDragKind(new Point(305, 205)));
        Assert.Equal(new EditableSelection.DragKind.Resize(Handle.Top), s.DetermineDragKind(new Point(200, 96)));
        Assert.Equal(EditableSelection.DragKind.Move.Instance, s.DetermineDragKind(new Point(200, 150)));
        Assert.Equal(EditableSelection.DragKind.Draw.Instance, s.DetermineDragKind(new Point(500, 500)));
    }

    [Fact]
    [Unit]
    public void DraggingAnEdgeMovesItAndAnchorsTheOppositeEdge()
    {
        var s = Drawn(new Point(100, 100), new Point(300, 200));
        s.DragBegan(new Point(300, 150));
        s.DragEnded(new Point(450, 999));
        Assert.Equal(new Rect(100, 100, 350, 100), s.Rect);

        s.DragBegan(new Point(100, 150));
        s.DragEnded(new Point(500, 150));
        Assert.Equal(new Rect(450, 100, 50, 100), s.Rect);
    }

    [Fact]
    [Unit]
    public void DraggingACornerAnchorsTheOppositeCorner()
    {
        var s = Drawn(new Point(100, 100), new Point(300, 200));
        s.DragBegan(new Point(100, 100));
        s.DragEnded(new Point(50, 20));
        Assert.Equal(new Rect(50, 20, 250, 180), s.Rect);
    }

    [Fact]
    [Unit]
    public void RatioLockedHandleDragsKeepTheLockWithTheOtherAxisFollowing()
    {
        var s = Drawn(new Point(100, 100), new Point(420, 280), ratio: AspectRatio.R16x9);
        s.DragBegan(new Point(420, 190));
        s.DragEnded(new Point(260, 190));
        Assert.Equal(new Rect(100, 100, 160, 90), s.Rect);

        s.DragBegan(new Point(260, 190));
        s.DragEnded(new Point(740, 400));
        Assert.Equal(new Rect(100, 100, 640, 360), s.Rect);
    }

    [Fact]
    [Unit]
    public void RatioLockedEdgeDragsNeverLeaveTheBounds()
    {
        var s = Drawn(new Point(100, 100), new Point(300, 200), ratio: AspectRatio.R1x1);
        Assert.Equal(new Rect(100, 100, 200, 200), s.Rect);

        s.DragBegan(new Point(100, 200));
        s.DragEnded(new Point(900, 200));
        Assert.Equal(new Rect(300, 100, 600, 600), s.Rect);
        Assert.True(s.Rect!.Value.MaxX <= 1000 && s.Rect!.Value.MaxY <= 800);

        s.DragBegan(new Point(900, 400));
        s.DragEnded(new Point(1500, 400));
        Assert.True(s.Rect!.Value.MaxX <= 1000 && s.Rect!.Value.MaxY <= 800);
        Assert.Equal(s.Rect!.Value.Width, s.Rect!.Value.Height);
    }

    [Fact]
    [Unit]
    public void RatioLockedTopAndBottomEdgeDragsFollowWithTheWidth()
    {
        var s = Drawn(new Point(100, 100), new Point(420, 280), ratio: AspectRatio.R16x9);
        s.DragBegan(new Point(260, 280));
        s.DragEnded(new Point(260, 190));
        Assert.Equal(new Rect(100, 100, 160, 90), s.Rect);

        s.DragBegan(new Point(180, 100));
        s.DragEnded(new Point(180, 0));
        var r = s.Rect!.Value;
        Assert.True(r.MinX == 100 && r.MinY == 0 && r.Height == 190);
        Assert.True(Math.Abs(r.Width - 190 * 16.0 / 9.0) < 0.001);
    }

    [Fact]
    [Unit]
    public void MoveNudgeAndTypedSizesStayInsideTheBounds()
    {
        var s = Drawn(new Point(100, 100), new Point(300, 200));
        s.DragBegan(new Point(200, 150));
        s.DragEnded(new Point(1100, 150));
        Assert.Equal(new Rect(800, 100, 200, 100), s.Rect);

        s.Nudge(-10, 5);
        Assert.Equal(new Rect(790, 105, 200, 100), s.Rect);
        s.Nudge(0, -999);
        Assert.Equal(0.0, s.Rect?.MinY);

        s.SetWidth(500);
        Assert.Equal(new Rect(790, 0, 210, 100), s.Rect);
        s.SetHeight(3);
        Assert.Equal(EditableSelection.MinimumSide, s.Rect?.Height);
    }

    [Fact]
    [Unit]
    public void TypedSizesAndShiftArrowsFollowTheRatioLock()
    {
        var s = Drawn(new Point(0, 0), new Point(160, 90), ratio: AspectRatio.R16x9);
        s.SetWidth(320);
        Assert.Equal(new Rect(0, 0, 320, 180), s.Rect);

        s.SetHeight(45);
        Assert.Equal(new Rect(0, 0, 80, 45), s.Rect);

        s.Resize(80, 0);
        Assert.Equal(new Rect(0, 0, 160, 90), s.Rect);

        s.Resize(0, 90);
        Assert.Equal(new Rect(0, 0, 320, 180), s.Rect);
    }

    [Fact]
    [Unit]
    public void ChangingTheRatioRefitsFromTheTopLeftAndShrinksToFit()
    {
        var s = Drawn(new Point(0, 700), new Point(400, 800));
        s.SetRatio(AspectRatio.R1x1);
        Assert.Equal(AspectRatio.R1x1, s.Ratio);
        Assert.Equal(new Rect(0, 700, 100, 100), s.Rect);

        s.SetRatio(AspectRatio.Freeform);
        Assert.Equal(new Rect(0, 700, 100, 100), s.Rect);
    }

    [Fact]
    [Unit]
    public void SnappingToAWindowReplacesTheSelectionClippedToTheBounds()
    {
        var s = Drawn(new Point(0, 0), new Point(50, 50));
        s.Snap(new Rect(900, 700, 300, 300));
        Assert.Equal(new Rect(900, 700, 100, 100), s.Rect);
    }

    [Fact]
    [Unit]
    public void ARememberedRectIsKeptOnlyIfItStillFits()
    {
        Assert.Equal(new Rect(10, 10, 100, 50), new EditableSelection(TestBounds, rect: new Rect(10, 10, 100, 50)).Rect);
        Assert.Null(new EditableSelection(TestBounds, rect: new Rect(2000, 2000, 100, 50)).Rect);

        Assert.Equal(new Rect(900, 700, 100, 100), new EditableSelection(TestBounds, rect: new Rect(900, 700, 300, 300)).Rect);
        Assert.Null(new EditableSelection(TestBounds, rect: new Rect(998, 798, 100, 50)).Rect);
    }

    [Fact]
    [Unit]
    public void TheDefaultRecordingAreaIsACentred720pRectOnALargeDisplay()
    {
        var rect = EditableSelection.DefaultRecordingRect(new Rect(0, 0, 2560, 1440));
        Assert.Equal(new Rect(640, 360, 1280, 720), rect);
    }

    [Fact]
    [Unit]
    public void OnASmallDisplayTheDefaultAreaIsSixtyPercentWideAndStays16By9()
    {
        var rect = EditableSelection.DefaultRecordingRect(new Rect(0, 0, 1440, 900));
        Assert.True(rect.Width == 864 && rect.Height == 486);
        Assert.True(rect.MidX == 720 && rect.MidY == 450);
    }

    [Fact]
    [Unit]
    public void OnATallDisplayTheDefaultAreaStillFits()
    {
        var bounds = new Rect(0, 0, 800, 1200);
        var rect = EditableSelection.DefaultRecordingRect(bounds);
        Assert.True(rect.Width <= 800 * 0.6 + 1e-9 && rect.Height <= 1200);
        Assert.True(Math.Abs(rect.Width / rect.Height - 16.0 / 9.0) < 0.01);
    }
}
