// Ported from LightshotKit/Tests/LightshotKitTests/DocumentRenderTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Linq;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

public class DocumentRenderTests
{
    private static AnnotationDocument Document(int width = 100, int height = 100, (double r, double g, double b)? rgb = null)
    {
        return new AnnotationDocument(PixelAssert.SolidImage(width, height, rgb));
    }

    private static RenderedImage Render(AnnotationDocument doc) => Rendering.Render(doc);

    // MARK: - Dimensions & base pass-through

    [Fact]
    [Render]
    public void EmptyDocumentKeepsBaseDimensions()
    {
        var doc = Document(128, 96);
        var outImg = Render(doc);
        Assert.Equal(128, outImg.PixelWidth);
        Assert.Equal(96, outImg.PixelHeight);
    }

    [Fact]
    [Render]
    public void EmptyDocumentReproducesTheBasePixels()
    {
        var doc = Document(40, 40, (1, 1, 1));
        var pixels = new Pixels(Render(doc));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(20, 20)));
    }

    [Fact]
    [Render]
    public void CropShrinksOutputToTheCropRect()
    {
        var doc = Document(200, 150);
        doc.ApplyCrop(new Rect(20, 10, 80, 60));
        var outImg = Render(doc);
        Assert.Equal(80, outImg.PixelWidth);
        Assert.Equal(60, outImg.PixelHeight);
    }

    [Fact]
    [Render]
    public void CropOffsetsAnnotationsIntoTheCroppedFrame()
    {
        var doc = Document(200, 150);
        var mark = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(100, 80, 40, 30)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 3, fill: RGBAColor.Red));
        doc.Add(mark);
        doc.ApplyCrop(new Rect(80, 60, 100, 80));

        var rendered = Render(doc);
        var pixels = new Pixels(rendered);
        Assert.Equal(100, pixels.Width);
        Assert.Equal(80, pixels.Height);

        // Mark centered at (120, 95) lands at (40, 35) in cropped output
        Assert.True(PixelAssert.IsRed(pixels.Rgb(40, 35)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(5, 5)));
    }

    [Fact]
    [Render]
    public void ReversingACropRestoresTheFullFrameWithElementsIntact()
    {
        var doc = Document(200, 150);
        var mark = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(80, 60, 40, 30)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 3, fill: RGBAColor.Red));
        doc.Add(mark);

        doc.ApplyCrop(new Rect(0, 0, 60, 60));
        Assert.Equal(60, Render(doc).PixelWidth);

        doc.Undo();
        var outImg = Render(doc);
        Assert.Equal(200, outImg.PixelWidth);
        Assert.Equal(150, outImg.PixelHeight);
        Assert.True(PixelAssert.IsRed(new Pixels(outImg).Rgb(100, 75)));
    }

    // MARK: - Vector tools appear

    [Fact]
    [Render]
    public void FilledRectanglePaintsItsInterior()
    {
        var doc = Document();
        var element = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(20, 20, 60, 60)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 3, fill: RGBAColor.Red));
        doc.Add(element);

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("FilledRectanglePaintsItsInterior", rendered);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsRed(pixels.Rgb(50, 50)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(5, 5)));
    }

    [Fact]
    [Render]
    public void StrokedLineMarksThePixelsAlongIt()
    {
        var doc = Document();
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Line(new Point(10, 50), new Point(90, 50)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 6)));

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("StrokedLineMarksThePixelsAlongIt", rendered);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsRed(pixels.Rgb(50, 50)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(50, 20)));
    }

    [Fact]
    [Render]
    public void FilledEllipsePaintsCenterButNotBoundingBoxCorner()
    {
        var doc = Document();
        var element = new AnnotationElement(
            kind: new AnnotationElement.Kind.Ellipse(new Rect(10, 10, 80, 80)),
            style: new Style(color: RGBAColor.Red, fill: RGBAColor.Red));
        doc.Add(element);

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("FilledEllipsePaintsCenterButNotBoundingBoxCorner", rendered);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsRed(pixels.Rgb(50, 50)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(12, 12)));
    }

    // MARK: - Highlight & redaction

    [Fact]
    [Render]
    public void HighlighterWashesTheRegionButLetsContentShowThrough()
    {
        var doc = Document();
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Highlight(new Rect(20, 20, 60, 60)),
            style: new Style(color: RGBAColor.Red)));

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("HighlighterWashesTheRegionButLetsContentShowThrough", rendered);

        var pixels = new Pixels(rendered);
        var (r, g, b, _) = pixels.RgbaByte(50, 50);

        // Arithmetic derivation:
        // Upstream Render.swift highlightAlpha = 0.35
        // Red highlight (255, 0, 0) over white background (255, 255, 255):
        // R = round(255 * 0.35 + 255 * (1 - 0.35)) = 255
        // G = round(0 * 0.35 + 255 * (1 - 0.35)) = round(255 * 0.65) = round(165.75) = 166
        // B = round(0 * 0.35 + 255 * (1 - 0.35)) = round(255 * 0.65) = round(165.75) = 166
        // Expected washed color is (255, 166, 166) within +-1 channel value.
        Assert.Equal(255, r);
        Assert.InRange(g, 165, 167);
        Assert.InRange(b, 165, 167);
    }

    [Fact]
    [Render]
    public void BlackoutErasesTheSentinelBeneathIt()
    {
        var doc = new AnnotationDocument(PixelAssert.SolidImage(100, 100, (0, 1, 0)));
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Redaction(new Rect(20, 20, 60, 60), RedactionStyle.Blackout)));

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("BlackoutErasesTheSentinelBeneathIt", rendered);

        var pixels = new Pixels(rendered);
        foreach (var (x, y) in new[] { (30, 30), (50, 50), (70, 70) })
        {
            var c = pixels.Rgb(x, y);
            Assert.True(PixelAssert.IsBlack(c));
            Assert.False(PixelAssert.IsGreen(c));
        }

        Assert.True(PixelAssert.IsGreen(pixels.Rgb(5, 5)));
    }

    [Fact]
    [Render]
    public void BlurChangesTheRegionWithoutErasingIt()
    {
        var baseImg = PixelAssert.HalvesImage(100, 100, (1, 0, 0), (0, 0, 1));
        var doc = new AnnotationDocument(baseImg);
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Redaction(new Rect(30, 30, 40, 40), RedactionStyle.Blur)));

        var plain = new Pixels(Render(new AnnotationDocument(baseImg)));
        var blurred = new Pixels(Render(doc));

        Assert.True(PixelAssert.ChannelDistance(plain.Rgb(50, 50), blurred.Rgb(50, 50)) > 0.05);
    }

    [Fact]
    [Render]
    public void PixelateChangesTheRegion()
    {
        var baseImg = PixelAssert.HalvesImage(100, 100, (1, 0, 0), (0, 0, 1));
        var doc = new AnnotationDocument(baseImg);
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Redaction(new Rect(30, 30, 40, 40), RedactionStyle.Pixelate)));

        var plain = new Pixels(Render(new AnnotationDocument(baseImg)));
        var pixelated = new Pixels(Render(doc));

        bool changed = Enumerable.Range(44, 13).Any(x =>
            PixelAssert.ChannelDistance(plain.Rgb(x, 50), pixelated.Rgb(x, 50)) > 0.05);
        Assert.True(changed);
    }

    // MARK: - Z-order

    [Fact]
    [Render]
    public void LaterElementDrawsOnTopAtOverlap()
    {
        var doc = Document();
        var bottom = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 60, 60)),
            style: new Style(color: RGBAColor.Black, fill: RGBAColor.Black));
        var top = new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(30, 30, 60, 60)),
            style: new Style(color: RGBAColor.Red, fill: RGBAColor.Red));

        doc.Add(bottom);
        doc.Add(top);

        var pixels = new Pixels(Render(doc));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(45, 45)));
    }

    // MARK: - Arrow styles & redaction effects

    [Fact]
    [Render]
    public void StandardArrowIsThinAtTheTailAndWideAtTheHead()
    {
        var doc = Document(200, 100);
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Arrow(new Point(10, 50), new Point(190, 50), null, ArrowStyle.Standard),
            style: new Style(color: RGBAColor.Red, strokeWidth: 8)));

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("StandardArrowIsThinAtTheTailAndWideAtTheHead", rendered);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsRed(pixels.Rgb(30, 50)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(30, 55)));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(145, 55)));
        Assert.True(PixelAssert.IsRed(pixels.Rgb(155, 64)));
    }

    [Fact]
    [Render]
    public void CurvedArrowIsDrawnThroughItsBendNotAlongItsChord()
    {
        var doc = Document(200, 120);
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Arrow(new Point(20, 100), new Point(180, 100), new Point(100, 30), ArrowStyle.Curved),
            style: new Style(color: RGBAColor.Red, strokeWidth: 6)));

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("CurvedArrowIsDrawnThroughItsBendNotAlongItsChord", rendered);

        var pixels = new Pixels(rendered);
        Assert.True(PixelAssert.IsRed(pixels.Rgb(100, 30)));
        Assert.True(PixelAssert.IsWhite(pixels.Rgb(100, 100)));
    }

    [Theory]
    [InlineData(RedactionStyle.Blur)]
    [InlineData(RedactionStyle.Pixelate)]
    [Render]
    public void TheSameElementAlwaysFlattensToTheSamePixels(RedactionStyle style)
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var doc1 = new AnnotationDocument(baseImg);
        doc1.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), style, 0.5, 7)));

        var doc2 = new AnnotationDocument(baseImg);
        doc2.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), style, 0.5, 7)));

        Assert.Equal(Render(doc1).Data, Render(doc2).Data);
    }

    [Theory]
    [InlineData(RedactionStyle.Blur)]
    [InlineData(RedactionStyle.Pixelate)]
    [Render]
    public void ADifferentSeedScramblesDifferently(RedactionStyle style)
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var doc1 = new AnnotationDocument(baseImg);
        doc1.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), style, 0.5, 1)));

        var doc2 = new AnnotationDocument(baseImg);
        doc2.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), style, 0.5, 2)));

        Assert.NotEqual(Render(doc1).Data, Render(doc2).Data);
    }

    [Theory]
    [InlineData(RedactionStyle.Blur)]
    [InlineData(RedactionStyle.Pixelate)]
    [Render]
    public void StrengthChangesTheResult(RedactionStyle style)
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var doc1 = new AnnotationDocument(baseImg);
        doc1.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), style, 0.1, 7)));

        var doc2 = new AnnotationDocument(baseImg);
        doc2.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), style, 0.9, 7)));

        Assert.NotEqual(Render(doc1).Data, Render(doc2).Data);
    }

    [Fact]
    [Render]
    public void BlurSmearsTheSeamWidelyAndStaysInsideItsRegion()
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var doc = new AnnotationDocument(baseImg);
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), RedactionStyle.Blur, 1.0, 7)));

        var plain = new Pixels(Render(new AnnotationDocument(baseImg)));
        var blurred = new Pixels(Render(doc));

        // 20px from red/blue seam is reached by blur
        Assert.True(PixelAssert.ChannelDistance(plain.Rgb(80, 60), blurred.Rgb(80, 60)) > 0.05);

        // Outside the region nothing moved
        Assert.True(PixelAssert.ChannelDistance(plain.Rgb(55, 60), blurred.Rgb(55, 60)) < 0.001);
        Assert.True(PixelAssert.ChannelDistance(plain.Rgb(100, 25), blurred.Rgb(100, 25)) < 0.001);
    }

    [Fact]
    [Render]
    public void BlurObscuresAnnotationsBeneathItButNotThoseAbove()
    {
        var doc = new AnnotationDocument(PixelAssert.SolidImage(200, 120, (1, 1, 1)));
        var mark = new Rect(90, 50, 20, 20);
        var region = new Rect(60, 30, 80, 60);

        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(mark), style: new Style(color: RGBAColor.Red, fill: RGBAColor.Red)));
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(region, RedactionStyle.Blur, 0.3, 7)));

        var blurred = new Pixels(Render(doc));
        Assert.False(PixelAssert.IsRed(blurred.Rgb(100, 60)));
        Assert.True(PixelAssert.ChannelDistance(blurred.Rgb(82, 60), (1, 1, 1)) > 0.02);

        // Mark on top stays solid red
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(mark), style: new Style(color: RGBAColor.Red, fill: RGBAColor.Red)));
        Assert.True(PixelAssert.IsRed(new Pixels(Render(doc)).Rgb(100, 60)));
    }

    [Theory]
    [InlineData(RedactionStyle.Blur)]
    [InlineData(RedactionStyle.Pixelate)]
    [Render]
    public void TheCanvasPreviewPatchIsExactlyWhatExports(RedactionStyle style)
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var region = new Rect(60, 30, 80, 60);
        var doc = new AnnotationDocument(baseImg);
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(region, style, 0.5, 7)));

        var backdrop = DocumentRenderer.CreateRedactionBackdrop(doc, 0);
        Assert.NotNull(backdrop);

        using var patch = backdrop.Patch(region, style, 0.5, 7);
        Assert.NotNull(patch);
        Assert.Equal(region, patch.Rect);

        using var patchData = patch.Image.Encode(SKEncodedImageFormat.Png, 100);
        var preview = new Pixels(new RenderedImage(patch.Image.Width, patch.Image.Height, patchData.ToArray()));
        var export = new Pixels(Render(doc));

        foreach (var (x, y) in new[] { (0, 0), (40, 30), (39, 10), (79, 59), (12, 47) })
        {
            var a = preview.Rgb(x, y);
            var b = export.Rgb(60 + x, 30 + y);
            Assert.True(PixelAssert.ChannelDistance(a, b) < 0.001);
        }
    }

    [Fact]
    [Render]
    public void BlackoutNeedsNoPatch()
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var doc = new AnnotationDocument(baseImg);
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(60, 30, 80, 60), RedactionStyle.Blackout, 0.5, 7)));

        var backdrop = DocumentRenderer.CreateRedactionBackdrop(doc, 0);
        Assert.NotNull(backdrop);
        Assert.Null(backdrop.Patch(new Rect(60, 30, 80, 60), RedactionStyle.Blackout, 0.5, 7));
    }

    [Fact]
    [Render]
    public void ARegionPokingOutsideTheCropIsClippedToTheVisibleFrame()
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var region = new Rect(60, 30, 80, 60);
        var doc = new AnnotationDocument(baseImg);
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(region, RedactionStyle.Pixelate, 0.5, 7)));

        doc.ApplyCrop(new Rect(100, 0, 100, 120));

        var backdrop = DocumentRenderer.CreateRedactionBackdrop(doc, 0);
        Assert.NotNull(backdrop);

        using var patch = backdrop.Patch(region, RedactionStyle.Pixelate, 0.5, 7);
        Assert.NotNull(patch);
        Assert.Equal(new Rect(100, 30, 40, 60), patch.Rect);
    }

    // MARK: - Focus (LIG-47)

    [Fact]
    [Render]
    public void EverythingOutsideTheFocusAreasIsDimmedAndTheAreasStayClear()
    {
        var doc = Document();
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Focus(new Rect(10, 10, 30, 30))));
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Focus(new Rect(30, 30, 30, 30))));

        var rendered = Render(doc);
        PixelAssert.AssertOrUpdateGolden("EverythingOutsideTheFocusAreasIsDimmedAndTheAreasStayClear", rendered);

        var pixels = new Pixels(rendered);
        foreach (var (x, y) in new[] { (20, 20), (35, 35), (50, 50) })
        {
            var (cr, cg, cb, _) = pixels.RgbaByte(x, y);
            Assert.True(cr >= 250 && cg >= 250 && cb >= 250);
        }

        // Arithmetic derivation:
        // Upstream Render.swift focusDimAlpha = 0.55 (literal constant, not using FocusDim.FocusDimAlpha)
        // Black dim layer (0, 0, 0, alpha=0.55) over white background (255, 255, 255):
        // R = G = B = round(0 * 0.55 + 255 * (1 - 0.55)) = round(255 * 0.45) = round(114.75) = 115
        // Expected dimmed outside color is (115, 115, 115) within +-1 channel value.
        var (outR, outG, outB, _) = pixels.RgbaByte(85, 85);
        Assert.InRange(outR, 114, 116);
        Assert.InRange(outG, 114, 116);
        Assert.InRange(outB, 114, 116);
    }

    [Fact]
    [Render]
    public void MarksStayBrightOutsideTheFocusAreas()
    {
        var doc = Document();
        doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Rectangle(new Rect(60, 60, 30, 30)),
            style: new Style(color: RGBAColor.Red, strokeWidth: 6)));
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Focus(new Rect(10, 10, 20, 20))));

        var pixels = new Pixels(Render(doc));
        var edge = pixels.Rgb(60, 75);
        Assert.True(edge.r > 0.9 && edge.g < 0.2);
    }

    [Fact]
    [Render]
    public void AFocusAreaIsGrabbedAnywhereButTheMarksItFramesWinAClick()
    {
        var area = new AnnotationElement.Kind.Focus(new Rect(10, 10, 80, 60));
        Assert.True(area.HitTest(new Point(10, 40), tolerance: 2));
        Assert.True(area.HitTest(new Point(50, 40), tolerance: 2));

        var doc = Document();
        var mark = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(40, 30, 20, 20))));
        var focus = doc.Add(new AnnotationElement(kind: area));

        Assert.Equal(mark, doc.ElementID(new Point(40, 40)));
        Assert.Equal(focus, doc.ElementID(new Point(80, 60)));
        Assert.Equal(12.0, FocusDim.FocusCornerRadius(new Rect(0, 0, 200, 200)));
        Assert.Equal(5.0, FocusDim.FocusCornerRadius(new Rect(0, 0, 20, 40)));
        Assert.Equal(new Rect(10, 10, 80, 60), area.BoundingBox);
        Assert.Equal(new Rect(15, 5, 80, 60), ((AnnotationElement.Kind.Focus)area.Moved(5, -5)).Rect);
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.Fields(area));
    }

    // MARK: - Required New Phase 3 Test

    [Fact]
    [Render]
    public void PreviewPatchEqualsExportPatch()
    {
        var baseImg = PixelAssert.HalvesImage(200, 120, (1, 0, 0), (0, 0, 1));
        var region = new Rect(60, 30, 80, 60);
        var doc = new AnnotationDocument(baseImg);
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(region, RedactionStyle.Blur, 0.6, 42)));

        var backdrop = DocumentRenderer.CreateRedactionBackdrop(doc, 0);
        Assert.NotNull(backdrop);

        using var patch = backdrop.Patch(region, RedactionStyle.Blur, 0.6, 42);
        Assert.NotNull(patch);

        using var patchData = patch.Image.Encode(SKEncodedImageFormat.Png, 100);
        var preview = new Pixels(new RenderedImage(patch.Image.Width, patch.Image.Height, patchData.ToArray()));
        var export = new Pixels(Render(doc));

        // Preview patch must equal export patch pixel for pixel
        for (int y = 0; y < (int)region.Height; y += 5)
        {
            for (int x = 0; x < (int)region.Width; x += 5)
            {
                var a = preview.Rgb(x, y);
                var b = export.Rgb((int)region.MinX + x, (int)region.MinY + y);
                Assert.True(PixelAssert.ChannelDistance(a, b) < 0.001,
                    $"Preview and export patch differ at ({x}, {y})");
            }
        }
    }
}
