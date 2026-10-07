// Ported from LightshotKit/Tests/LightshotKitTests/HistoryStoreTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class HistoryStoreTests : IDisposable
{
    private readonly string _tempDir;

    public HistoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"history-render-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private static string CreateGifFixture(string path, int width = 60, int height = 40, int[]? delaysCs = null)
    {
        delaysCs ??= [10, 25]; // 0.10s + 0.25s = 0.35s

        // Frame 0: Red
        byte[] redRgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            redRgba[i * 4] = 255;
            redRgba[i * 4 + 3] = 255;
        }
        var frame0 = GifQuantizer.Quantize(redRgba, width, height, isBgra: false, maxColors: 256);

        // Frame 1: Blue
        byte[] blueRgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            blueRgba[i * 4 + 2] = 255;
            blueRgba[i * 4 + 3] = 255;
        }
        var frame1 = GifQuantizer.Quantize(blueRgba, width, height, isBgra: false, maxColors: 256);

        using (var fs = File.Create(path))
        using (var writer = new GifWriter(fs, width, height, loopCount: 0, leaveOpen: false))
        {
            writer.WriteFrame(frame0, delayCentiseconds: (ushort)delaysCs[0]);
            writer.WriteFrame(frame1, delayCentiseconds: (ushort)delaysCs[1]);
        }

        return path;
    }

    [Fact]
    [Render]
    public void AddWritesTheImageAndAThumbnailToDisk()
    {
        var thumbnailer = new SkiaThumbnailer();
        var store = new HistoryStore(_tempDir, thumbnailer: thumbnailer);

        var image = PixelAssert.SolidImage(64, 48);
        var record = store.Add(image, CaptureSource.Fullscreen);

        Assert.True(File.Exists(record.FileUrl));
        Assert.True(File.Exists(record.ThumbnailUrl));
        Assert.Equal(64, record.PixelWidth);
        Assert.Equal(48, record.PixelHeight);

        // The thumbnail is a real, decodable image (not placeholder bytes)
        using var stream = File.OpenRead(record.ThumbnailUrl);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.True(codec.Info.Width <= 256 && codec.Info.Height <= 256);
    }

    [Fact]
    [Render]
    public void AddGIFReadsDimensionsThumbnailAndDurationThroughImageIO()
    {
        var historyDir = Path.Combine(_tempDir, "history");
        var thumbnailer = new SkiaThumbnailer();
        var store = new HistoryStore(historyDir, thumbnailer: thumbnailer);

        var gifPath = Path.Combine(_tempDir, "take.gif");
        CreateGifFixture(gifPath, 60, 40, [10, 25]);

        var record = store.AddGif(gifPath, CaptureSource.Recording);

        Assert.Equal(CaptureKind.Gif, record.Kind);
        Assert.Equal(60, record.PixelWidth);
        Assert.Equal(40, record.PixelHeight);
        Assert.NotNull(record.Duration);
        Assert.True(Math.Abs(record.Duration.Value - 0.35) < 0.01);
        Assert.Equal(".gif", Path.GetExtension(record.FileUrl));
        Assert.False(File.Exists(gifPath)); // moved into history, not copied

        // Frame 0 is red: the thumbnail decodes to that size and colour
        Assert.True(File.Exists(record.ThumbnailUrl));
        using (var thumbStream = File.OpenRead(record.ThumbnailUrl))
        using (var thumbCodec = SKCodec.Create(thumbStream))
        {
            Assert.NotNull(thumbCodec);
            using var thumbBitmap = SKBitmap.Decode(thumbCodec);
            Assert.NotNull(thumbBitmap);
            var pixel = thumbBitmap.GetPixel(thumbBitmap.Width / 2, thumbBitmap.Height / 2);
            Assert.True(pixel.Red > 200 && pixel.Blue < 50);
        }

        Assert.Null(store.CapturedImage(record));

        // Unreadable media leaves file where it was and throws HistoryException with UnreadableMedia
        var notAGif = Path.Combine(_tempDir, "take.mp4");
        File.WriteAllBytes(notAGif, [0, 1, 2, 3]);
        var ex = Assert.Throws<HistoryException>(() => store.AddGif(notAGif, CaptureSource.Recording));
        Assert.Equal(HistoryError.UnreadableMedia, ex.Error);
        Assert.True(File.Exists(notAGif));
    }

    [Fact]
    [Render]
    public void ReopenedScreenshotDecodesToTheOriginalSize()
    {
        var thumbnailer = new SkiaThumbnailer();
        var codec = new SkiaImageCodec();
        var store = new HistoryStore(_tempDir, thumbnailer: thumbnailer, codec: codec);

        var rawPixels = new byte[100 * 80 * 4];
        for (int i = 0; i < rawPixels.Length; i += 4)
        {
            rawPixels[i] = 0x11;     // B
            rawPixels[i + 1] = 0x22; // G
            rawPixels[i + 2] = 0x33; // R
            rawPixels[i + 3] = 0xFF; // A
        }
        var image = new CapturedImage(100, 80, rawPixels);
        var record = store.Add(image, CaptureSource.Area);

        var reopened = store.CapturedImage(record);
        Assert.NotNull(reopened);
        Assert.Equal(100, reopened.Value.PixelWidth);
        Assert.Equal(80, reopened.Value.PixelHeight);
        Assert.Equal(100 * 80 * 4, reopened.Value.Data.Length);
    }

    [Fact]
    [Render]
    public void LegacyRawScreenshotIsPurgedAtLoad()
    {
        var historyDir = Path.Combine(_tempDir, "legacy-history");
        Directory.CreateDirectory(historyDir);

        // 1. Build a store without a codec: writes raw pixels to <id>.png
        var storeWithoutCodec = new HistoryStore(historyDir, thumbnailer: new SkiaThumbnailer());
        var rawPixels = new byte[50 * 50 * 4];
        rawPixels[0] = 0x01; // Not PNG signature
        var image = new CapturedImage(50, 50, rawPixels);
        var record = storeWithoutCodec.Add(image, CaptureSource.Area);

        Assert.True(File.Exists(record.FileUrl));
        Assert.True(File.Exists(record.ThumbnailUrl));
        Assert.Single(storeWithoutCodec.All());

        // 2. Open a new store with codec: purges legacy raw screenshot
        var storeWithCodec = new HistoryStore(historyDir, thumbnailer: new SkiaThumbnailer(), codec: new SkiaImageCodec());
        Assert.Empty(storeWithCodec.All());
        Assert.False(File.Exists(record.FileUrl));
        Assert.False(File.Exists(record.ThumbnailUrl));
    }

    [Fact]
    [Render]
    public void RecordingEntriesSurviveTheLegacyPurge()
    {
        var historyDir = Path.Combine(_tempDir, "recording-purge-history");
        Directory.CreateDirectory(historyDir);

        var storeWithoutCodec = new HistoryStore(historyDir, thumbnailer: new SkiaThumbnailer());

        var dummyVideo = Path.Combine(_tempDir, "dummy-video.mp4");
        File.WriteAllBytes(dummyVideo, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
        byte[] dummyThumb = [0x01, 0x02, 0x03, 0x04];

        var record = storeWithoutCodec.Add(
            dummyVideo,
            CaptureKind.Video,
            1280,
            720,
            10.5,
            dummyThumb,
            CaptureSource.Area);

        Assert.Single(storeWithoutCodec.All());
        Assert.True(File.Exists(record.FileUrl));
        Assert.True(File.Exists(record.ThumbnailUrl));

        // Open a new store with codec: video recording survives
        var storeWithCodec = new HistoryStore(historyDir, thumbnailer: new SkiaThumbnailer(), codec: new SkiaImageCodec());
        var records = storeWithCodec.All();
        Assert.Single(records);
        Assert.Equal(CaptureKind.Video, records[0].Kind);
        Assert.True(File.Exists(records[0].FileUrl));
        Assert.True(File.Exists(records[0].ThumbnailUrl));
    }
}
