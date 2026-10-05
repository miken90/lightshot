// Ported from LightshotKit/Tests/LightshotKitTests/FrozenScreenTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class FrozenScreenTests
{
    private static CapturedImage QuadrantStill(int widthPoints, int heightPoints, int scale)
    {
        int width = widthPoints * scale;
        int height = heightPoints * scale;
        byte[] data = new byte[width * height * 4];

        int halfW = width / 2;
        int halfH = height / 2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                bool top = y < halfH;
                bool left = x < halfW;

                if (top && left) // Red
                {
                    data[offset] = 0;       // B
                    data[offset + 1] = 0;   // G
                    data[offset + 2] = 255; // R
                    data[offset + 3] = 255; // A
                }
                else if (top && !left) // Green
                {
                    data[offset] = 0;
                    data[offset + 1] = 255;
                    data[offset + 2] = 0;
                    data[offset + 3] = 255;
                }
                else if (!top && left) // Blue
                {
                    data[offset] = 255;
                    data[offset + 1] = 0;
                    data[offset + 2] = 0;
                    data[offset + 3] = 255;
                }
                else // White
                {
                    data[offset] = 255;
                    data[offset + 1] = 255;
                    data[offset + 2] = 255;
                    data[offset + 3] = 255;
                }
            }
        }

        return new CapturedImage(width, height, data);
    }

    private static HashSet<string> Colours(CapturedImage image)
    {
        var found = new HashSet<string>();
        var span = image.Data.Span;
        for (int i = 0; i < span.Length; i += 4)
        {
            byte b = span[i] > 127 ? (byte)255 : (byte)0;
            byte g = span[i + 1] > 127 ? (byte)255 : (byte)0;
            byte r = span[i + 2] > 127 ? (byte)255 : (byte)0;

            if (r == 255 && g == 0 && b == 0) found.Add("red");
            else if (r == 0 && g == 255 && b == 0) found.Add("green");
            else if (r == 0 && g == 0 && b == 255) found.Add("blue");
            else if (r == 255 && g == 255 && b == 255) found.Add("white");
        }
        return found;
    }

    private static FrozenDisplay Display(uint id = 1, Point origin = default, int scale = 2)
    {
        return new FrozenDisplay(
            id,
            new Rect(origin.X, origin.Y, 200, 100),
            QuadrantStill(200, 100, scale)
        );
    }

    private static FrozenScreen Frozen(Point origin = default)
    {
        return new FrozenScreen(new[] { Display(origin: origin) }, Array.Empty<FrozenWindow>());
    }

    private static FrozenScreen TwoDisplays()
    {
        return new FrozenScreen(
            new[] { Display(1), Display(2, origin: new Point(200, 0), scale: 1) },
            Array.Empty<FrozenWindow>()
        );
    }

    [Fact]
    public void CropOfOneQuadrantIsThatQuadrantAtNativePixels()
    {
        var cropNullable = Frozen().ImageOf(new CaptureRegion.RectRegion(new Rect(110, 10, 50, 30)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(100, crop.PixelWidth);
        Assert.Equal(60, crop.PixelHeight);
        Assert.Equal(new HashSet<string> { "green" }, Colours(crop));
    }

    [Fact]
    public void AReversedDragIsStandardizedBeforeCropping()
    {
        var cropNullable = Frozen().ImageOf(new CaptureRegion.RectRegion(new Rect(90, 90, -80, -30)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(160, crop.PixelWidth);
        Assert.Equal(60, crop.PixelHeight);
        Assert.Equal(new HashSet<string> { "blue" }, Colours(crop));
    }

    [Fact]
    public void ASelectionOvershootingTheEdgeIsClampedToTheScreen()
    {
        var cropNullable = Frozen().ImageOf(new CaptureRegion.RectRegion(new Rect(150, 60, 100, 100)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(100, crop.PixelWidth);
        Assert.Equal(80, crop.PixelHeight);
        Assert.Equal(new HashSet<string> { "white" }, Colours(crop));
    }

    [Fact]
    public void ASelectionEntirelyOffTheScreenCropsNothing()
    {
        Assert.Null(Frozen().ImageOf(new CaptureRegion.RectRegion(new Rect(300, 10, 50, 50))));
        Assert.Null(Frozen().ImageOf(new CaptureRegion.RectRegion(new Rect(10, 10, 0, 50))));
    }

    [Fact]
    public void ADisplayAwayFromTheOriginIsOffsetBeforeCropping()
    {
        var screen = Frozen(origin: new Point(1000, 500));
        var cropNullable = screen.ImageOf(new CaptureRegion.RectRegion(new Rect(1010, 510, 20, 20)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(40, crop.PixelWidth);
        Assert.Equal(new HashSet<string> { "red" }, Colours(crop));
    }

    [Fact]
    public void ADisplayRegionIsNeverCutFromTheStills()
    {
        Assert.Null(Frozen().ImageOf(new CaptureRegion.DisplayRegion(1)));
    }

    [Fact]
    public void AnAreaOnTheSecondDisplayIsCutFromItsStillAtItsOwnScale()
    {
        var cropNullable = TwoDisplays().ImageOf(new CaptureRegion.RectRegion(new Rect(210, 10, 30, 20)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(30, crop.PixelWidth);
        Assert.Equal(20, crop.PixelHeight);
        Assert.Equal(new HashSet<string> { "red" }, Colours(crop));
    }

    [Fact]
    public void AnAreaStraddlingTwoDisplaysIsClampedToTheOneItOverlapsMost()
    {
        var cropNullable = TwoDisplays().ImageOf(new CaptureRegion.RectRegion(new Rect(180, 10, 80, 20)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(60, crop.PixelWidth);
        Assert.Equal(new HashSet<string> { "red" }, Colours(crop));
    }

    [Fact]
    public void AWindowWithNoFrozenImageGivesNothing()
    {
        var screen = new FrozenScreen(
            new[] { Display() },
            new[] { new FrozenWindow(7, new Rect(10, 10, 30, 20), null) }
        );

        Assert.Null(screen.ImageOf(new CaptureRegion.WindowRegion(7, new Rect(10, 10, 30, 20))));
        Assert.Null(screen.ImageOf(new CaptureRegion.WindowRegion(8, new Rect(10, 10, 30, 20))));
    }

    [Fact]
    public void FractionalSelectionsRoundLikeTheLiveRectCapture()
    {
        var cropNullable = Frozen().ImageOf(new CaptureRegion.RectRegion(new Rect(10.3, 10.3, 20.4, 15.2)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(41, crop.PixelWidth);
        Assert.Equal(30, crop.PixelHeight);
    }

    [Fact]
    public void WindowImagesAreMatchedToTheirWindowsById()
    {
        var a = QuadrantStill(30, 20, 1);
        var screen = new FrozenScreen(
            new[] { Display() },
            new[]
            {
                new FrozenWindow(7, new Rect(0, 0, 30, 20), null),
                new FrozenWindow(8, new Rect(50, 0, 30, 20), null),
            }
        );

        screen.SetWindowImages(new Dictionary<uint, CapturedImage> { [8] = a, [99] = a });
        Assert.Equal(new CapturedImage?[] { null, a }, screen.WindowList.Select(w => w.Image));
    }

    [Fact]
    public void CropPicksLargestOverlapAcrossMixedScale()
    {
        var screen = new FrozenScreen(
            new[]
            {
                new FrozenDisplay(1, new Rect(0, 0, 200, 100), QuadrantStill(200, 100, 2)), // 2x
                new FrozenDisplay(2, new Rect(200, 0, 200, 100), QuadrantStill(200, 100, 1)), // 1x
            },
            Array.Empty<FrozenWindow>()
        );

        // Selection 140..240: 60pt overlap with Display 1, 40pt overlap with Display 2.
        // Overlap area on Display 1 is larger (60*50 > 40*50), so it picks Display 1 at 2x.
        var cropNullable = screen.ImageOf(new CaptureRegion.RectRegion(new Rect(140, 10, 100, 50)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;
        Assert.Equal(120, crop.PixelWidth); // 60 pt * 2x = 120 px
        Assert.Equal(100, crop.PixelHeight); // 50 pt * 2x = 100 px
    }
}
