// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class GifQuantizerTests
{
    [Fact]
    [Unit]
    public void IsDeterministicWithAtMost256Colours()
    {
        int width = 64;
        int height = 64;
        byte[] rgba = new byte[width * height * 4];

        // Synthesize a gradient with diverse colors (more than 256 colors)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                rgba[offset] = (byte)(x * 4);
                rgba[offset + 1] = (byte)(y * 4);
                rgba[offset + 2] = (byte)((x + y) * 2);
                rgba[offset + 3] = 255;
            }
        }

        var frame1 = GifQuantizer.Quantize(rgba, width, height, isBgra: false, maxColors: 256);
        var frame2 = GifQuantizer.Quantize(rgba, width, height, isBgra: false, maxColors: 256);

        // Palette has at most 256 colors
        Assert.True(frame1.Palette.Count <= 256);
        Assert.True(frame1.Palette.Count >= 2);

        // Output is strictly deterministic
        Assert.Equal(frame1.Palette.Count, frame2.Palette.Count);
        Assert.Equal(frame1.Palette, frame2.Palette);
        Assert.Equal(frame1.IndexedPixels, frame2.IndexedPixels);

        // All pixel indices point to valid palette entries
        Assert.All(frame1.IndexedPixels, idx => Assert.True(idx < frame1.Palette.Count));
    }
}
