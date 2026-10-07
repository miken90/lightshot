// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class EdgeColorSamplerTests
{
    [Fact]
    [Unit]
    public void DominantBorderColorWins()
    {
        int width = 50;
        int height = 50;
        byte[] pixels = new byte[width * height * 4];

        // Fill entire image with Green (B=0, G=255, R=0, A=255)
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4 + 0] = 0;   // B
            pixels[i * 4 + 1] = 255; // G
            pixels[i * 4 + 2] = 0;   // R
            pixels[i * 4 + 3] = 255; // A
        }

        // Put a single red pixel on one border corner
        pixels[0] = 0;
        pixels[1] = 0;
        pixels[2] = 255;
        pixels[3] = 255;

        var color = EdgeColorSampler.Sample(pixels, width, height, PixelOrder.Bgra);

        Assert.Equal(0.0, color.R, 3);
        Assert.Equal(1.0, color.G, 3);
        Assert.Equal(0.0, color.B, 3);
        Assert.Equal(1.0, color.A, 3);
    }

    [Fact]
    [Unit]
    public void TransparentBorderFallsBack()
    {
        int width = 30;
        int height = 30;
        byte[] pixels = new byte[width * height * 4];

        // All pixels have alpha = 0 (or < 128)
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4 + 0] = 255; // B
            pixels[i * 4 + 1] = 255; // G
            pixels[i * 4 + 2] = 255; // R
            pixels[i * 4 + 3] = 50;  // A < 128
        }

        var color = EdgeColorSampler.Sample(pixels, width, height, PixelOrder.Bgra);
        Assert.Equal(EdgeColorSampler.Fallback, color);
    }

    [Fact]
    [Unit]
    public void QuantizesToStepsOf32()
    {
        int width = 40;
        int height = 40;
        byte[] pixels = new byte[width * height * 4];

        // R=15 -> 0, G=45 -> 32, B=200 -> 192, A=255
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4 + 0] = 200; // B
            pixels[i * 4 + 1] = 45;  // G
            pixels[i * 4 + 2] = 15;  // R
            pixels[i * 4 + 3] = 255; // A
        }

        var color = EdgeColorSampler.Sample(pixels, width, height, PixelOrder.Bgra);

        Assert.Equal(0.0, color.R, 3);
        Assert.Equal(32.0 / 255.0, color.G, 3);
        Assert.Equal(192.0 / 255.0, color.B, 3);
        Assert.Equal(1.0, color.A, 3);
    }

    [Fact]
    [Unit]
    public void RgbaAndBgraAgree()
    {
        int width = 40;
        int height = 40;
        byte[] bgra = new byte[width * height * 4];
        byte[] rgba = new byte[width * height * 4];

        // Color: R=192, G=128, B=64, A=255
        for (int i = 0; i < width * height; i++)
        {
            bgra[i * 4 + 0] = 64;  // B
            bgra[i * 4 + 1] = 128; // G
            bgra[i * 4 + 2] = 192; // R
            bgra[i * 4 + 3] = 255; // A

            rgba[i * 4 + 0] = 192; // R
            rgba[i * 4 + 1] = 128; // G
            rgba[i * 4 + 2] = 64;  // B
            rgba[i * 4 + 3] = 255; // A
        }

        var colorBgra = EdgeColorSampler.Sample(bgra, width, height, PixelOrder.Bgra);
        var colorRgba = EdgeColorSampler.Sample(rgba, width, height, PixelOrder.Rgba);

        Assert.Equal(colorBgra, colorRgba);
        Assert.Equal(192.0 / 255.0, colorBgra.R, 3);
        Assert.Equal(128.0 / 255.0, colorBgra.G, 3);
        Assert.Equal(64.0 / 255.0, colorBgra.B, 3);
    }

    [Fact]
    [Unit]
    public void ShortBufferFallsBack()
    {
        byte[] shortBuffer = new byte[100];
        var color = EdgeColorSampler.Sample(shortBuffer, 20, 20, PixelOrder.Bgra);
        Assert.Equal(EdgeColorSampler.Fallback, color);

        var zeroColor = EdgeColorSampler.Sample(shortBuffer, 0, 0, PixelOrder.Bgra);
        Assert.Equal(EdgeColorSampler.Fallback, zeroColor);
    }
}
