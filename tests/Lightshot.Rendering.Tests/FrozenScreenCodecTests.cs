// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class FrozenScreenCodecTests
{
    [Fact]
    [Render]
    public void EncodesAndDecodesPixelSurface()
    {
        int width = 50;
        int height = 30;
        int stride = width * 4;
        byte[] bgraData = new byte[height * stride];

        // Fill with pattern (BGRA)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 4;
                bgraData[offset] = (byte)(x * 5);         // B
                bgraData[offset + 1] = (byte)(y * 8);     // G
                bgraData[offset + 2] = (byte)((x + y) * 3);// R
                bgraData[offset + 3] = 255;               // A
            }
        }

        var surface = new PixelSurface(width, height, pixels: bgraData, stride: stride);

        // 1. PixelSurface <-> SKBitmap round-trip
        using var bitmap = PixelSurfaceConverter.ToBitmap(surface);
        Assert.Equal(width, bitmap.Width);
        Assert.Equal(height, bitmap.Height);

        var roundTrippedSurface = PixelSurfaceConverter.FromBitmap(bitmap);
        Assert.True(surface.Pixels.AsSpan().SequenceEqual(roundTrippedSurface.Pixels.AsSpan()));

        // 2. Encode to PNG
        using var img = SKImage.FromBitmap(bitmap);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        byte[] pngBytes = data.ToArray();
        Assert.NotEmpty(pngBytes);

        // 3. Decode back via SkiaImageCodec
        var codec = new SkiaImageCodec();
        var decoded = codec.Decode(pngBytes);
        Assert.NotNull(decoded);
        Assert.Equal(width, decoded.Value.PixelWidth);
        Assert.Equal(height, decoded.Value.PixelHeight);

        // Check pixel values match
        var decodedSpan = decoded.Value.Data.Span;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                // Allow small rounding tolerance for premul conversion
                Assert.True(Math.Abs(decodedSpan[offset] - bgraData[offset]) <= 2);
                Assert.True(Math.Abs(decodedSpan[offset + 1] - bgraData[offset + 1]) <= 2);
                Assert.True(Math.Abs(decodedSpan[offset + 2] - bgraData[offset + 2]) <= 2);
            }
        }
    }
}
