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

[Trait("Tier", "Render")]
public class DragOutFileTests : IDisposable
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private readonly string _tempDir;

    public DragOutFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dragout-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    [Render]
    public void CardDragFileIsPng()
    {
        var codec = new SkiaImageCodec();
        var rawPixels = new byte[100 * 100 * 4];
        for (int i = 0; i < rawPixels.Length; i += 4)
        {
            rawPixels[i] = 0x55;     // B
            rawPixels[i + 1] = 0xAA; // G
            rawPixels[i + 2] = 0xFF; // R
            rawPixels[i + 3] = 0xFF; // A
        }
        var image = new CapturedImage(100, 100, rawPixels);
        var id = Guid.NewGuid();

        var path = DragOutFile.WritePng(_tempDir, id, image, codec);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 8);
        Assert.Equal(PngSignature, bytes.Take(8));

        using var bitmap = SKBitmap.Decode(path);
        Assert.NotNull(bitmap);
        Assert.Equal(100, bitmap.Width);
        Assert.Equal(100, bitmap.Height);
    }

    [Fact]
    [Render]
    public void EditorDragPayloadIsPng()
    {
        var rawPixels = new byte[80 * 60 * 4];
        for (int i = 0; i < rawPixels.Length; i += 4)
        {
            rawPixels[i] = 0x33;
            rawPixels[i + 1] = 0x66;
            rawPixels[i + 2] = 0x99;
            rawPixels[i + 3] = 0xFF;
        }
        var rawImage = new CapturedImage(80, 60, rawPixels);
        var doc = new AnnotationDocument(rawImage);
        var renderer = new DocumentRenderer();

        var rendered = renderer.Render(doc);

        Assert.NotNull(rendered);
        Assert.True(rendered.Data.Length >= 8);
        Assert.Equal(PngSignature, rendered.Data.Take(8));

        using var bitmap = SKBitmap.Decode(rendered.Data);
        Assert.NotNull(bitmap);
        Assert.Equal(80, bitmap.Width);
        Assert.Equal(60, bitmap.Height);
    }
}
