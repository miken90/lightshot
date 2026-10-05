// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class ThumbnailTests
{
    [Fact]
    [Render]
    public void CreatesThumbnailWithinBounds()
    {
        var thumbnailer = new SkiaThumbnailer();

        // 800x400 image
        var info = new SKImageInfo(800, 400, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        byte[] inputBytes = pngData.ToArray();

        byte[]? thumbBytes = thumbnailer.CreateThumbnail(inputBytes, maxPixelSize: 256);
        Assert.NotNull(thumbBytes);

        using var stream = new SKMemoryStream(thumbBytes);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.True(codec.Info.Width <= 256);
        Assert.True(codec.Info.Height <= 256);
        Assert.Equal(256, codec.Info.Width);
        Assert.Equal(128, codec.Info.Height);
    }

    [Fact]
    [Render]
    public void UnreadableImageReturnsNull()
    {
        var thumbnailer = new SkiaThumbnailer();
        byte[] corrupted = [0, 1, 2, 3, 4, 5, 6, 7];
        byte[]? result = thumbnailer.CreateThumbnail(corrupted, maxPixelSize: 256);
        Assert.Null(result);
    }
}
