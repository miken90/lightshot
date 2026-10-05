// Ported from LightshotKit/Sources/LightshotKit/RedactionEffect.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// A tiny seedable SplitMix64 generator used for deterministic scrambling.
/// </summary>
public struct SplitMix64
{
    public const ulong Increment = 0x9E3779B97F4A7C15UL;
    public ulong State;

    public SplitMix64(ulong seed)
    {
        State = seed;
    }

    public ulong Next()
    {
        unchecked
        {
            State += Increment;
            ulong z = State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

/// <summary>
/// Averages an image down to coarse cells, swaps each cell's color for a seeded-random
/// neighbor's, and enlarges the cells back to the original size with nearest-neighbor scaling.
/// </summary>
public static class Scramble
{
    public static SKBitmap Apply(SKBitmap source, double block, ulong seed)
    {
        int width = source.Width;
        int height = source.Height;

        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (width <= 0 || height <= 0)
        {
            return result;
        }

        double effectiveBlock = Math.Max(1.0, block);
        int columns = Math.Max(1, (int)Math.Ceiling(width / effectiveBlock));
        int rows = Math.Max(1, (int)Math.Ceiling(height / effectiveBlock));

        // Read source pixels as RGBA8888
        byte[] srcBytes = new byte[width * height * 4];
        using (var pixmap = source.PeekPixels())
        {
            if (pixmap != null && source.ColorType == SKColorType.Rgba8888)
            {
                pixmap.GetPixelSpan().CopyTo(srcBytes);
            }
            else
            {
                using var canvas = new SKCanvas(result);
                canvas.DrawBitmap(source, 0, 0);
                result.GetPixelSpan().CopyTo(srcBytes);
            }
        }

        // 1. Area-average downscale to (columns, rows)
        var cellColors = new uint[rows, columns];

        for (int r = 0; r < rows; r++)
        {
            int y0 = r * height / rows;
            int y1 = (r + 1) * height / rows;
            if (y1 <= y0) y1 = y0 + 1;

            for (int c = 0; c < columns; c++)
            {
                int x0 = c * width / columns;
                int x1 = (c + 1) * width / columns;
                if (x1 <= x0) x1 = x0 + 1;

                long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                int pixelCount = 0;

                for (int y = y0; y < y1 && y < height; y++)
                {
                    int rowOffset = y * width * 4;
                    for (int x = x0; x < x1 && x < width; x++)
                    {
                        int offset = rowOffset + x * 4;
                        sumR += srcBytes[offset];
                        sumG += srcBytes[offset + 1];
                        sumB += srcBytes[offset + 2];
                        sumA += srcBytes[offset + 3];
                        pixelCount++;
                    }
                }

                if (pixelCount == 0)
                {
                    cellColors[r, c] = 0;
                }
                else
                {
                    byte avgR = (byte)(sumR / pixelCount);
                    byte avgG = (byte)(sumG / pixelCount);
                    byte avgB = (byte)(sumB / pixelCount);
                    byte avgA = (byte)(sumA / pixelCount);
                    cellColors[r, c] = (uint)(avgR | (avgG << 8) | (avgB << 16) | (avgA << 24));
                }
            }
        }

        // 2. Seeded neighbor shuffle: two draws per cell in x-then-y order
        var scrambled = new uint[rows, columns];
        var random = new SplitMix64(seed);

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int sx = Math.Clamp(x + (int)(random.Next() % 3) - 1, 0, columns - 1);
                int sy = Math.Clamp(y + (int)(random.Next() % 3) - 1, 0, rows - 1);
                scrambled[y, x] = cellColors[sy, sx];
            }
        }

        // 3. Nearest-neighbor upscale back to (width, height)
        byte[] dstBytes = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int cy = Math.Min(rows - 1, y * rows / height);
            int rowOffset = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int cx = Math.Min(columns - 1, x * columns / width);
                uint color = scrambled[cy, cx];

                int offset = rowOffset + x * 4;
                dstBytes[offset] = (byte)(color & 0xFF);
                dstBytes[offset + 1] = (byte)((color >> 8) & 0xFF);
                dstBytes[offset + 2] = (byte)((color >> 16) & 0xFF);
                dstBytes[offset + 3] = (byte)((color >> 24) & 0xFF);
            }
        }

        var resultSpan = result.GetPixelSpan();
        dstBytes.AsSpan().CopyTo(resultSpan);

        return result;
    }
}
