// Ported from LightshotKit/Tests/LightshotKitTests/FrozenScreenTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class FrozenScreenTests
{
    private static CapturedImage QuadrantStill(int widthPoints, int heightPoints, int scale)
    {
        int width = widthPoints * scale;
        int height = heightPoints * scale;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);

        int halfW = width / 2;
        int halfH = height / 2;

        // Top-left: Red
        using (var paint = new SKPaint { Color = new SKColor(255, 0, 0) })
            canvas.DrawRect(new SKRect(0, 0, halfW, halfH), paint);

        // Top-right: Green
        using (var paint = new SKPaint { Color = new SKColor(0, 255, 0) })
            canvas.DrawRect(new SKRect(halfW, 0, width, halfH), paint);

        // Bottom-left: Blue
        using (var paint = new SKPaint { Color = new SKColor(0, 0, 255) })
            canvas.DrawRect(new SKRect(0, halfH, halfW, height), paint);

        // Bottom-right: White
        using (var paint = new SKPaint { Color = new SKColor(255, 255, 255) })
            canvas.DrawRect(new SKRect(halfW, halfH, width, height), paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    private static HashSet<string> Colours(CapturedImage image)
    {
        using var stream = new SKMemoryStream(image.Data.ToArray());
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        codec.GetPixels(info, bitmap.GetPixels());

        var found = new HashSet<string>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                int r = pixel.Red > 127 ? 255 : 0;
                int g = pixel.Green > 127 ? 255 : 0;
                int b = pixel.Blue > 127 ? 255 : 0;

                if (r == 255 && g == 0 && b == 0) found.Add("red");
                else if (r == 0 && g == 255 && b == 0) found.Add("green");
                else if (r == 0 && g == 0 && b == 255) found.Add("blue");
                else if (r == 255 && g == 255 && b == 255) found.Add("white");
            }
        }
        return found;
    }

    [Fact]
    [Render]
    public void AWindowIsItsFrozenImageAsAPNG()
    {
        var still = QuadrantStill(30, 20, 2);
        var screen = new FrozenScreen(
            new[] { new FrozenDisplay(1, new Rect(0, 0, 200, 100), QuadrantStill(200, 100, 2)) },
            new[] { new FrozenWindow(7, new Rect(10, 10, 30, 20), still) }
        );

        var imageNullable = screen.ImageOf(new CaptureRegion.WindowRegion(7, new Rect(10, 10, 30, 20)));
        Assert.NotNull(imageNullable);
        var image = imageNullable!.Value;

        Assert.Equal(60, image.PixelWidth);
        Assert.Equal(40, image.PixelHeight);

        using var stream = new SKMemoryStream(image.Data.ToArray());
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);

        var colours = Colours(image);
        Assert.Contains("red", colours);
        Assert.Contains("green", colours);
        Assert.Contains("blue", colours);
        Assert.Contains("white", colours);
    }

    [Fact]
    [Render]
    public void AStillInAnyImageIOFormatCropsToAPNG()
    {
        // Still encoded in JPEG format (demonstrating any ImageIO format)
        var png = QuadrantStill(200, 100, 2);
        using var pngStream = new SKMemoryStream(png.Data.ToArray());
        using var codec = SKCodec.Create(pngStream);
        Assert.NotNull(codec);
        using var bitmap = SKBitmap.Decode(codec);
        Assert.NotNull(bitmap);

        using var jpegData = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] jpegBytes = jpegData.ToArray();

        // Decode the image format to raw pixel buffer as FrozenDisplay requires
        var skiaCodec = new SkiaImageCodec();
        var decoded = skiaCodec.Decode(jpegBytes);
        Assert.NotNull(decoded);

        var screen = new FrozenScreen(
            new[] { new FrozenDisplay(1, new Rect(0, 0, 200, 100), decoded.Value) },
            Array.Empty<FrozenWindow>()
        );

        // Crop the bottom-left quadrant (points x: 10, y: 60, w: 40, h: 20) -> blue quadrant
        var cropNullable = screen.ImageOf(new CaptureRegion.RectRegion(new Rect(10, 60, 40, 20)));
        Assert.NotNull(cropNullable);
        var crop = cropNullable!.Value;

        // Encode crop to PNG
        var pngBytes = skiaCodec.Encode(new RenderedImage(crop.PixelWidth, crop.PixelHeight, crop.Data.ToArray()), new ImageFormat.Png());
        var pngCrop = new CapturedImage(crop.PixelWidth, crop.PixelHeight, pngBytes);

        using var verifyStream = new SKMemoryStream(pngCrop.Data.ToArray());
        using var verifyCodec = SKCodec.Create(verifyStream);
        Assert.NotNull(verifyCodec);
        Assert.Equal(SKEncodedImageFormat.Png, verifyCodec.EncodedFormat);

        var colours = Colours(pngCrop);
        Assert.Equal(new HashSet<string> { "blue" }, colours);
    }
}
