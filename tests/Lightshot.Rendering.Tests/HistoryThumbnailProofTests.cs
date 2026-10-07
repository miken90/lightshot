// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using Lightshot.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

// Proof for the user report "History shows no thumbnails": captures carry raw BGRA pixels
// (DdaDisplayCapture, FrozenScreen.ImageOf), and HistoryStore must still write decodable images.
[Trait("Tier", "Render")]
public class HistoryThumbnailProofTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"history-proof-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static CapturedImage RawBgraCapture(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4] = 0x40;     // B
            pixels[i * 4 + 1] = 0x80; // G
            pixels[i * 4 + 2] = 0xC0; // R
            pixels[i * 4 + 3] = 0xFF; // A
        }
        return new CapturedImage(width, height, pixels);
    }

    [Fact]
    public void ScreenshotThumbnailIsADecodableImage()
    {
        var store = new HistoryStore(_dir, thumbnailer: new SkiaThumbnailer(), codec: new SkiaImageCodec());

        var record = store.Add(RawBgraCapture(800, 600), CaptureSource.Area);

        using var thumb = SKBitmap.Decode(record.ThumbnailUrl);
        Assert.NotNull(thumb);
        Assert.True(Math.Max(thumb.Width, thumb.Height) <= HistoryStore.ThumbnailMaxPixelSize);
    }

    [Fact]
    public void ScreenshotFileIsADecodableImage()
    {
        var store = new HistoryStore(_dir, thumbnailer: new SkiaThumbnailer(), codec: new SkiaImageCodec());

        var record = store.Add(RawBgraCapture(800, 600), CaptureSource.Fullscreen);

        using var image = SKBitmap.Decode(record.FileUrl);
        Assert.NotNull(image);
        Assert.Equal(800, image.Width);
    }
}
