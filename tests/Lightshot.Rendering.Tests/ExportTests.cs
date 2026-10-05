// Ported from LightshotKit/Tests/LightshotKitTests/ExportTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

public class ExportTests
{
    private static readonly DateTime FixtureDate = new(2026, 9, 5, 7, 3, 9, DateTimeKind.Utc);
    private static readonly FilenameFormatter Formatter = new("Screenshot %Y-%m-%d at %H.%M.%S");

    private static RenderedImage RenderedFixture()
    {
        var info = new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(51, 102, 153, 255));
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new RenderedImage(8, 8, data.ToArray());
    }

    [Fact]
    [Render]
    public void ExtensionsAndTypesMatchTheFormat()
    {
        Assert.Equal("png", ImageFormat.Png.Instance.FileExtension);
        Assert.Equal("jpg", new ImageFormat.Jpeg(0.5).FileExtension);
        Assert.Equal("public.png", ImageFormat.Png.Instance.UtiIdentifier);
        Assert.Equal("public.jpeg", new ImageFormat.Jpeg(0.5).UtiIdentifier);
    }

    [Fact]
    [Render]
    public void ClampingFactoryNormalizesQualityIntoRange()
    {
        Assert.Equal(new ImageFormat.Jpeg(1.0), ImageFormat.Jpeg.Clamping(1.7));
        Assert.Equal(new ImageFormat.Jpeg(0.0), ImageFormat.Jpeg.Clamping(-0.3));
        Assert.Equal(new ImageFormat.Jpeg(0.6), ImageFormat.Jpeg.Clamping(0.6));
    }

    [Fact]
    [Render]
    public void ExpandsTokensZeroPadded()
    {
        Assert.Equal("Screenshot 2026-09-05 at 07.03.09", Formatter.Filename(FixtureDate));
    }

    [Fact]
    [Render]
    public void PassesUnknownTokensAndLonePercentThrough()
    {
        var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var formatter = new FilenameFormatter("100%_shot_%q_%Y");
        Assert.Equal("100%_shot_%q_2026", formatter.Filename(date));
    }

    [Fact]
    [Render]
    public void DestinationURLJoinsLocationNameAndExtension()
    {
        string dir = "/tmp/shots";
        string png = Formatter.DestinationPath(dir, ImageFormat.Png.Instance, FixtureDate);
        string jpg = Formatter.DestinationPath(dir, new ImageFormat.Jpeg(0.9), FixtureDate);

        Assert.Equal("/tmp/shots/Screenshot 2026-09-05 at 07.03.09.png", png);
        Assert.Equal("/tmp/shots/Screenshot 2026-09-05 at 07.03.09.jpg", jpg);
        Assert.Equal("Screenshot 2026-09-05 at 07.03.09.png", Path.GetFileName(png));
        Assert.Equal("Screenshot 2026-09-05 at 07.03.09.jpg", Path.GetFileName(jpg));
    }

    [Fact]
    [Render]
    public void PngEncodePassesTheRenderBytesThrough()
    {
        var image = RenderedFixture();
        var codec = new SkiaImageCodec();
        Assert.Equal(image.Data, codec.Encode(image, ImageFormat.Png.Instance));
    }

    [Fact]
    [Render]
    public void JpegEncodeProducesDecodableJPEGBytes()
    {
        var image = RenderedFixture();
        var codec = new SkiaImageCodec();
        byte[] data = codec.Encode(image, new ImageFormat.Jpeg(0.7));

        Assert.NotEmpty(data);
        Assert.NotEqual(image.Data, data);

        using var ms = new MemoryStream(data);
        using var skCodec = SKCodec.Create(ms);
        Assert.NotNull(skCodec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, skCodec.EncodedFormat);
        Assert.Equal(8, skCodec.Info.Width);
        Assert.Equal(8, skCodec.Info.Height);
    }
}
