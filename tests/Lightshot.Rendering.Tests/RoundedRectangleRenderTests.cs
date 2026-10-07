// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Rendering.Tests;

public class RoundedRectangleRenderTests
{
    private static AnnotationDocument Document(int width = 100, int height = 100, (double r, double g, double b)? rgb = null)
    {
        return new AnnotationDocument(PixelAssert.SolidImage(width, height, rgb));
    }

    private static RenderedImage Render(AnnotationDocument doc) => Rendering.Render(doc);

    [Fact]
    [Render]
    public void ZeroRadiusIsByteIdenticalToRegularRectangle()
    {
        var doc = Document();
        var element = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(20, 20, 60, 60)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 3, fill: RGBAColor.Red, cornerRadius: 0));
        doc.Add(element);

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("FilledRectanglePaintsItsInterior", rendered, exact: true);
    }

    [Fact]
    [Render]
    public void RoundedRectangleRendersFillAndStroke()
    {
        var doc = Document();
        var element = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(20, 20, 60, 60)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 3, fill: RGBAColor.Red, cornerRadius: 15));
        doc.Add(element);

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("RoundedRectangleRendersFillAndStroke", rendered);

        var pixels = new Pixels(rendered);

        // Centre pixel is interior fill
        Assert.True(PixelAssert.IsRed(pixels.Rgb(50, 50)));

        // Outer corner pixels of the rect bounding box are clipped (remain white background)
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(20, 20)));
        Assert.False(PixelAssert.IsRed(pixels.Rgb(20, 20)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(79, 20)));
        Assert.False(PixelAssert.IsRed(pixels.Rgb(79, 20)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(20, 79)));
        Assert.False(PixelAssert.IsRed(pixels.Rgb(20, 79)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(79, 79)));
        Assert.False(PixelAssert.IsRed(pixels.Rgb(79, 79)));

        // Flat sides remain inside the rect
        Assert.True(PixelAssert.IsRed(pixels.Rgb(50, 22)));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(50, 78)));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(22, 50)));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(78, 50)));
    }
}
