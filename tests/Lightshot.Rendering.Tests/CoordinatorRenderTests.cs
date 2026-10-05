// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class CoordinatorRenderTests
{
    [Fact]
    [Render]
    public void RenderAndExportFlows()
    {
        int width = 200;
        int height = 150;
        var baseImage = PixelAssert.SolidImage(width, height, (0.8, 0.8, 0.8));

        var doc = new AnnotationDocument(baseImage);

        // Add a rectangle and an arrow
        var rectElement = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(20, 20, 80, 50)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 3.0));

        var arrowElement = new AnnotationElement(
            kind: new AnnotationElement.Kind.Arrow(new Point(10, 10), new Point(120, 100), null, ArrowStyle.Standard),
            style: new Style(color: new RGBAColor(0.0, 0.0, 1.0, 1.0), strokeWidth: 4.0));

        doc.Add(rectElement);
        doc.Add(arrowElement);

        // 1. Render via DocumentRenderer (IImageRenderer)
        var renderer = new DocumentRenderer();
        var rendered = renderer.Render(doc);

        Assert.Equal(width, rendered.PixelWidth);
        Assert.Equal(height, rendered.PixelHeight);
        Assert.NotEmpty(rendered.Data);

        // 2. Export via SkiaImageCodec (IImageCodec)
        var codec = new SkiaImageCodec();

        // PNG export
        byte[] pngBytes = codec.Encode(rendered, new ImageFormat.Png());
        Assert.NotEmpty(pngBytes);
        using var pngStream = new SKMemoryStream(pngBytes);
        using var pngCodec = SKCodec.Create(pngStream);
        Assert.NotNull(pngCodec);
        Assert.Equal(SKEncodedImageFormat.Png, pngCodec.EncodedFormat);
        Assert.Equal(width, pngCodec.Info.Width);
        Assert.Equal(height, pngCodec.Info.Height);

        // JPEG export
        byte[] jpegBytes = codec.Encode(rendered, new ImageFormat.Jpeg(0.85));
        Assert.NotEmpty(jpegBytes);
        using var jpegStream = new SKMemoryStream(jpegBytes);
        using var jpegCodec = SKCodec.Create(jpegStream);
        Assert.NotNull(jpegCodec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, jpegCodec.EncodedFormat);
        Assert.Equal(width, jpegCodec.Info.Width);
        Assert.Equal(height, jpegCodec.Info.Height);
    }
}
