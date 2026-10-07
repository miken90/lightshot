// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class CanvasRenderTests
{
    private static readonly DocumentRenderer Renderer = new();

    [Fact]
    [Render]
    public void DisabledCanvasKeepsTheTightOutput()
    {
        var baseImg = PixelAssert.SolidImage(120, 80, (0.2, 0.4, 0.6));
        var docNoCanvas = new AnnotationDocument(baseImg);
        var docDisabledCanvas = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(Enabled: false, Padding: 40)
        };

        var rendered1 = Renderer.Render(docNoCanvas);
        var rendered2 = Renderer.Render(docDisabledCanvas);

        Assert.Equal(120, rendered1.PixelWidth);
        Assert.Equal(80, rendered1.PixelHeight);
        Assert.Equal(rendered1.PixelWidth, rendered2.PixelWidth);
        Assert.Equal(rendered1.PixelHeight, rendered2.PixelHeight);
        Assert.Equal(rendered1.Data, rendered2.Data);
    }

    [Fact]
    [Render]
    public void EnabledCanvasOutputsTheLayoutSize()
    {
        var baseImg = PixelAssert.SolidImage(100, 100);
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(Enabled: true, Padding: 40, Aspect: AspectPreset.Auto)
        };

        var rendered = Renderer.Render(doc);

        Assert.Equal(180, rendered.PixelWidth);
        Assert.Equal(180, rendered.PixelHeight);
    }

    [Fact]
    [Render]
    public void SolidFillPaintsThePadding()
    {
        var baseImg = PixelAssert.SolidImage(100, 100, (0.0, 0.0, 0.0));
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                Padding: 20,
                CornerRadius: 0,
                Shadow: 0,
                Fill: new CanvasFill(CanvasFillKind.Solid, Color: new RGBAColor(1.0, 0.0, 0.0, 1.0)))
        };

        var rendered = Renderer.Render(doc);
        Assert.Equal(140, rendered.PixelWidth);
        Assert.Equal(140, rendered.PixelHeight);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsRed(pixels.Rgb(5, 5)));
        Assert.True(PixelAssert.IsBlack(pixels.Rgb(70, 70)));
    }

    [Fact]
    [Render]
    public void ImageIsDrawnAtTheLayoutRect()
    {
        var baseImg = PixelAssert.SolidImage(100, 100, (0.0, 1.0, 0.0));
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                Padding: 30,
                CornerRadius: 0,
                Shadow: 0,
                Fill: new CanvasFill(CanvasFillKind.Solid, Color: new RGBAColor(1.0, 0.0, 0.0, 1.0)))
        };

        var rendered = Renderer.Render(doc);
        Assert.Equal(160, rendered.PixelWidth);
        Assert.Equal(160, rendered.PixelHeight);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsRed(pixels.Rgb(29, 29)));
        Assert.True(PixelAssert.IsGreen(pixels.Rgb(31, 31)));
        Assert.True(PixelAssert.IsGreen(pixels.Rgb(129, 129)));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(131, 131)));
    }

    [Fact]
    [Render]
    public void RoundedCornerShowsTheFillAtTheImageCorner()
    {
        var baseImg = PixelAssert.SolidImage(100, 100, (0.0, 0.0, 0.0));
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                Padding: 30,
                CornerRadius: 20,
                Shadow: 0,
                Fill: new CanvasFill(CanvasFillKind.Solid, Color: new RGBAColor(1.0, 0.0, 0.0, 1.0)))
        };

        var rendered = Renderer.Render(doc);
        var pixels = new Pixels(rendered);

        Assert.True(PixelAssert.IsRed(pixels.Rgb(31, 31)));
        Assert.True(PixelAssert.IsBlack(pixels.Rgb(80, 80)));
    }

    [Fact]
    [Render]
    public void AutoEdgeFillUsesTheDominantBorderColor()
    {
        var info = new SKImageInfo(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Black);
            using var bluePaint = new SKPaint { Color = SKColors.Blue, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
            c.DrawRect(new SKRect(0, 0, 100, 100), bluePaint);
        }
        using var encoded = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var baseImg = new CapturedImage(100, 100, encoded.ToArray());

        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                Padding: 20,
                CornerRadius: 0,
                Shadow: 0,
                Fill: new CanvasFill(CanvasFillKind.AutoEdge))
        };

        var rendered = Renderer.Render(doc);
        var pixels = new Pixels(rendered);

        Assert.True(PixelAssert.IsBlue(pixels.Rgb(5, 5)));
    }

    [Fact]
    [Render]
    public void MissingFillImageFallsBackToGradient()
    {
        var baseImg = PixelAssert.SolidImage(100, 100, (0.0, 0.0, 0.0));
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                Padding: 20,
                CornerRadius: 0,
                Shadow: 0,
                Fill: new CanvasFill(CanvasFillKind.Image, ImagePath: "nonexistent_lightshot_missing_test_bg.png"))
        };

        var rendered = Renderer.Render(doc);
        var pixels = new Pixels(rendered);

        var c = pixels.Rgb(0, 0);
        Assert.True(c.r > 0.8 && c.g > 0.3 && c.g < 0.5 && c.b < 0.3);
    }

    [Fact]
    [Render]
    public void DownscaleToFitKeepsTheFixedSize()
    {
        var baseImg = PixelAssert.SolidImage(2000, 2000, (0.0, 0.0, 0.0));
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                SizeMode: CanvasSizeMode.FixedSize,
                TargetWidth: 1000,
                TargetHeight: 1000,
                DownscaleToFit: true,
                Padding: 100,
                CornerRadius: 0,
                Shadow: 0,
                Fill: new CanvasFill(CanvasFillKind.Solid, Color: new RGBAColor(1.0, 0.0, 0.0, 1.0)))
        };

        var rendered = Renderer.Render(doc);
        Assert.Equal(1000, rendered.PixelWidth);
        Assert.Equal(1000, rendered.PixelHeight);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsBlack(pixels.Rgb(500, 500)));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(10, 10)));
    }

    [Fact]
    [Render]
    public void GradientCanvasWithShadow()
    {
        var baseImg = PixelAssert.SolidImage(100, 100, (1.0, 1.0, 1.0));
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                Padding: 40,
                CornerRadius: 12,
                Shadow: 20,
                Fill: new CanvasFill(CanvasFillKind.Gradient, GradientIndex: 0))
        };

        var rendered = Renderer.Render(doc);
        Assert.Equal(180, rendered.PixelWidth);
        Assert.Equal(180, rendered.PixelHeight);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(90, 90)));

        var c = pixels.Rgb(0, 0);
        Assert.True(c.r > 0.8 && c.g > 0.3 && c.b < 0.3);

        var docNoShadow = new AnnotationDocument(baseImg)
        {
            Canvas = doc.Canvas with { Shadow = 0 }
        };
        var renderedNoShadow = Renderer.Render(docNoShadow);
        var pixelsNoShadow = new Pixels(renderedNoShadow);

        var shadowColor = pixels.Rgb(90, 145);
        var noShadowColor = pixelsNoShadow.Rgb(90, 145);
        Assert.True(shadowColor.r < noShadowColor.r);

        PixelAssert.AssertOrUpdateGolden("GradientCanvasWithShadow", rendered);
    }

    [Fact]
    [Render]
    public void ImageFillCoversTheCanvas()
    {
        string wallpaperPath = Path.Combine(AppContext.BaseDirectory, "canvas_test_wallpaper.png");
        var info = new SKImageInfo(60, 60, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var bgBmp = new SKBitmap(info))
        {
            using (var c = new SKCanvas(bgBmp))
            {
                c.Clear(new SKColor(0x10, 0x50, 0xA0));
                using var p = new SKPaint { Color = new SKColor(0x30, 0x80, 0xE0) };
                c.DrawCircle(30, 30, 15, p);
            }
            using var data = bgBmp.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(wallpaperPath, data.ToArray());
        }

        var baseImg = PixelAssert.SolidImage(100, 100, (1.0, 1.0, 1.0));
        var doc = new AnnotationDocument(baseImg)
        {
            Canvas = new CanvasStyle(
                Enabled: true,
                Padding: 30,
                CornerRadius: 0,
                Shadow: 0,
                Fill: new CanvasFill(CanvasFillKind.Image, ImagePath: wallpaperPath))
        };

        var rendered = Renderer.Render(doc);
        Assert.Equal(160, rendered.PixelWidth);
        Assert.Equal(160, rendered.PixelHeight);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(80, 80)));

        var padColor = pixels.Rgb(5, 5);
        Assert.True(padColor.b > 0.5 && padColor.r < 0.2);

        PixelAssert.AssertOrUpdateGolden("ImageFillCoversTheCanvas", rendered);
    }
}
