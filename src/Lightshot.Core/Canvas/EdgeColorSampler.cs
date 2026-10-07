// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

public enum PixelOrder
{
    Bgra,
    Rgba
}

public static class EdgeColorSampler
{
    public static readonly RGBAColor Fallback = new(0x1A / 255.0, 0x1A / 255.0, 0x2E / 255.0);

    public static RGBAColor Sample(ReadOnlySpan<byte> pixels, int width, int height, PixelOrder order)
    {
        if (width <= 0 || height <= 0 || pixels.Length < (long)width * height * 4)
        {
            return Fallback;
        }

        int k = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / 1920.0));
        int step = 10 * k;

        Dictionary<int, int> frequencies = new();
        List<int> firstSeen = new();

        // Top row
        for (int x = 0; x < width; x += step)
        {
            SamplePixel(pixels, width, x, 0, order, frequencies, firstSeen);
        }

        // Bottom row
        if (height > 1)
        {
            for (int x = 0; x < width; x += step)
            {
                SamplePixel(pixels, width, x, height - 1, order, frequencies, firstSeen);
            }
        }

        // Left column
        for (int y = 0; y < height; y += step)
        {
            SamplePixel(pixels, width, 0, y, order, frequencies, firstSeen);
        }

        // Right column
        if (width > 1)
        {
            for (int y = 0; y < height; y += step)
            {
                SamplePixel(pixels, width, width - 1, y, order, frequencies, firstSeen);
            }
        }

        if (firstSeen.Count == 0)
        {
            return Fallback;
        }

        int bestKey = firstSeen[0];
        int maxCount = 0;
        foreach (int key in firstSeen)
        {
            int count = frequencies[key];
            if (count > maxCount)
            {
                maxCount = count;
                bestKey = key;
            }
        }

        int winR = (bestKey >> 16) & 0xFF;
        int winG = (bestKey >> 8) & 0xFF;
        int winB = bestKey & 0xFF;

        return new RGBAColor(winR / 255.0, winG / 255.0, winB / 255.0, 1.0);
    }

    private static void SamplePixel(
        ReadOnlySpan<byte> pixels,
        int width,
        int x,
        int y,
        PixelOrder order,
        Dictionary<int, int> frequencies,
        List<int> firstSeen)
    {
        int offset = (y * width + x) * 4;
        byte b0 = pixels[offset];
        byte b1 = pixels[offset + 1];
        byte b2 = pixels[offset + 2];
        byte b3 = pixels[offset + 3];

        byte r, g, b, a;
        if (order == PixelOrder.Bgra)
        {
            b = b0;
            g = b1;
            r = b2;
            a = b3;
        }
        else
        {
            r = b0;
            g = b1;
            b = b2;
            a = b3;
        }

        if (a < 128)
        {
            return;
        }

        int qr = Math.Min(255, (int)Math.Round(r / 32.0) * 32);
        int qg = Math.Min(255, (int)Math.Round(g / 32.0) * 32);
        int qb = Math.Min(255, (int)Math.Round(b / 32.0) * 32);

        int key = (qr << 16) | (qg << 8) | qb;
        if (frequencies.TryAdd(key, 1))
        {
            firstSeen.Add(key);
        }
        else
        {
            frequencies[key]++;
        }
    }
}
