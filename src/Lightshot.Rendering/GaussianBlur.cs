// Ported from LightshotKit/Sources/LightshotKit/RedactionEffect.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Bit-exact, CPU-independent Gaussian blur approximation using a three-pass integer box blur.
///
/// Approximation Theory:
/// By the Central Limit Theorem, repeatedly convolving with a uniform (box) filter converges
/// to a Gaussian distribution. Three passes of an appropriately sized box filter achieve an
/// approximation error typically under 3% compared to an analytical Gaussian filter, while
/// eliminating floating-point transcendental evaluations and SIMD-specific rounding differences.
/// Edge clamping replicates border pixels to prevent dark or transparent seams at boundaries.
/// </summary>
public static class GaussianBlur
{
    public const int PassCount = 3;

    /// <summary>
    /// Computes the box blur radii for the passes from the target sigma.
    /// </summary>
    public static int[] ComputeBoxRadii(double sigma, int n = PassCount)
    {
        if (sigma <= 0 || n <= 0)
        {
            return new int[Math.Max(1, n)];
        }

        // Sizing formula: wIdeal = sqrt((12 * sigma^2 / n) + 1)
        double wIdeal = Math.Sqrt((12.0 * sigma * sigma / n) + 1.0);
        int wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0)
        {
            wl--;
        }
        if (wl < 1)
        {
            wl = 1;
        }

        int wu = wl + 2;
        double mIdeal = (12.0 * sigma * sigma - n * wl * wl - 4.0 * n * wl - 3.0 * n) / (-4.0 * wl - 4.0);
        int m = (int)Math.Round(mIdeal);

        var radii = new int[n];
        for (int i = 0; i < n; i++)
        {
            int w = (i < m) ? wl : wu;
            radii[i] = Math.Max(0, (w - 1) / 2);
        }

        return radii;
    }

    /// <summary>
    /// Applies the 3-pass integer box blur to the given bitmap.
    /// Returns a new SKBitmap containing the blurred pixels.
    /// </summary>
    public static SKBitmap Apply(SKBitmap source, double sigma)
    {
        int width = source.Width;
        int height = source.Height;

        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (width <= 0 || height <= 0)
        {
            return result;
        }

        if (sigma <= 0.01)
        {
            source.CopyTo(result);
            return result;
        }

        int[] radii = ComputeBoxRadii(sigma, PassCount);

        // Convert source to RGBA8888 byte array
        byte[] current = new byte[width * height * 4];
        byte[] temp = new byte[width * height * 4];

        // Ensure source pixels are read in RGBA8888
        using (var pixmap = source.PeekPixels())
        {
            if (pixmap != null && source.ColorType == SKColorType.Rgba8888)
            {
                var span = pixmap.GetPixelSpan();
                span.CopyTo(current);
            }
            else
            {
                // Fallback decode / copy into current via a canvas
                using var canvas = new SKCanvas(result);
                canvas.DrawBitmap(source, 0, 0);
                var span = result.GetPixelSpan();
                span.CopyTo(current);
            }
        }

        for (int pass = 0; pass < PassCount; pass++)
        {
            int r = radii[pass];
            if (r <= 0) continue;

            // Horizontal pass: read current, write to temp
            BlurHorizontal(current, temp, width, height, r);

            // Vertical pass: read temp, write to current
            BlurVertical(temp, current, width, height, r);
        }

        // Copy blurred bytes back into result bitmap
        var destSpan = result.GetPixelSpan();
        current.AsSpan().CopyTo(destSpan);

        return result;
    }

    private static void BlurHorizontal(byte[] src, byte[] dst, int width, int height, int r)
    {
        int windowSize = 2 * r + 1;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width * 4;

            int sumR = 0, sumG = 0, sumB = 0, sumA = 0;

            // Initial accumulator with clamped border at x = 0
            int firstR = src[rowOffset];
            int firstG = src[rowOffset + 1];
            int firstB = src[rowOffset + 2];
            int firstA = src[rowOffset + 3];

            sumR += firstR * (r + 1);
            sumG += firstG * (r + 1);
            sumB += firstB * (r + 1);
            sumA += firstA * (r + 1);

            for (int i = 1; i <= r; i++)
            {
                int sampleX = Math.Min(i, width - 1);
                int offset = rowOffset + sampleX * 4;
                sumR += src[offset];
                sumG += src[offset + 1];
                sumB += src[offset + 2];
                sumA += src[offset + 3];
            }

            for (int x = 0; x < width; x++)
            {
                int outOffset = rowOffset + x * 4;
                dst[outOffset] = (byte)(sumR / windowSize);
                dst[outOffset + 1] = (byte)(sumG / windowSize);
                dst[outOffset + 2] = (byte)(sumB / windowSize);
                dst[outOffset + 3] = (byte)(sumA / windowSize);

                int addX = Math.Min(x + r + 1, width - 1);
                int subX = Math.Max(x - r, 0);

                int addOffset = rowOffset + addX * 4;
                int subOffset = rowOffset + subX * 4;

                sumR += src[addOffset] - src[subOffset];
                sumG += src[addOffset + 1] - src[subOffset + 1];
                sumB += src[addOffset + 2] - src[subOffset + 2];
                sumA += src[addOffset + 3] - src[subOffset + 3];
            }
        }
    }

    private static void BlurVertical(byte[] src, byte[] dst, int width, int height, int r)
    {
        int windowSize = 2 * r + 1;

        for (int x = 0; x < width; x++)
        {
            int colOffset = x * 4;

            int sumR = 0, sumG = 0, sumB = 0, sumA = 0;

            int firstR = src[colOffset];
            int firstG = src[colOffset + 1];
            int firstB = src[colOffset + 2];
            int firstA = src[colOffset + 3];

            sumR += firstR * (r + 1);
            sumG += firstG * (r + 1);
            sumB += firstB * (r + 1);
            sumA += firstA * (r + 1);

            for (int i = 1; i <= r; i++)
            {
                int sampleY = Math.Min(i, height - 1);
                int offset = sampleY * width * 4 + colOffset;
                sumR += src[offset];
                sumG += src[offset + 1];
                sumB += src[offset + 2];
                sumA += src[offset + 3];
            }

            for (int y = 0; y < height; y++)
            {
                int outOffset = y * width * 4 + colOffset;
                dst[outOffset] = (byte)(sumR / windowSize);
                dst[outOffset + 1] = (byte)(sumG / windowSize);
                dst[outOffset + 2] = (byte)(sumB / windowSize);
                dst[outOffset + 3] = (byte)(sumA / windowSize);

                int addY = Math.Min(y + r + 1, height - 1);
                int subY = Math.Max(y - r, 0);

                int addOffset = addY * width * 4 + colOffset;
                int subOffset = subY * width * 4 + colOffset;

                sumR += src[addOffset] - src[subOffset];
                sumG += src[addOffset + 1] - src[subOffset + 1];
                sumB += src[addOffset + 2] - src[subOffset + 2];
                sumA += src[addOffset + 3] - src[subOffset + 3];
            }
        }
    }
}
