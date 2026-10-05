// Ported from LightshotKit/Sources/LightshotKit/PixelSurface.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Converts between uncompressed BGRA32 PixelSurface and SkiaSharp SKBitmap.
/// </summary>
public static class PixelSurfaceConverter
{
    public static SKBitmap ToBitmap(PixelSurface surface)
    {
        if (surface == null) throw new ArgumentNullException(nameof(surface));

        var info = new SKImageInfo(surface.Width, surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        if (surface.Width <= 0 || surface.Height <= 0)
        {
            return bitmap;
        }

        var dstSpan = bitmap.GetPixelSpan();
        int dstRowBytes = bitmap.RowBytes;

        for (int y = 0; y < surface.Height; y++)
        {
            int srcOffset = y * surface.Stride;
            int dstOffset = y * dstRowBytes;
            int copyBytes = Math.Min(surface.Width * 4, Math.Min(surface.Stride, dstRowBytes));

            surface.Pixels.AsSpan(srcOffset, copyBytes).CopyTo(dstSpan.Slice(dstOffset, copyBytes));
        }

        return bitmap;
    }

    public static PixelSurface FromBitmap(SKBitmap bitmap)
    {
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

        int width = bitmap.Width;
        int height = bitmap.Height;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        if (width <= 0 || height <= 0)
        {
            return new PixelSurface(width, height, pixels, stride);
        }

        // If bitmap is already Bgra8888
        if (bitmap.ColorType == SKColorType.Bgra8888)
        {
            using var pixmap = bitmap.PeekPixels();
            var srcSpan = pixmap.GetPixelSpan();
            int srcRowBytes = bitmap.RowBytes;

            for (int y = 0; y < height; y++)
            {
                int srcOffset = y * srcRowBytes;
                int dstOffset = y * stride;
                int copyBytes = Math.Min(stride, srcRowBytes);

                srcSpan.Slice(srcOffset, copyBytes).CopyTo(pixels.AsSpan(dstOffset, copyBytes));
            }
        }
        else
        {
            // Convert to Bgra8888 via SKCanvas
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var converted = new SKBitmap(info);
            using (var canvas = new SKCanvas(converted))
            {
                canvas.DrawBitmap(bitmap, 0, 0);
            }

            var srcSpan = converted.GetPixelSpan();
            int srcRowBytes = converted.RowBytes;

            for (int y = 0; y < height; y++)
            {
                int srcOffset = y * srcRowBytes;
                int dstOffset = y * stride;
                int copyBytes = Math.Min(stride, srcRowBytes);

                srcSpan.Slice(srcOffset, copyBytes).CopyTo(pixels.AsSpan(dstOffset, copyBytes));
            }
        }

        return new PixelSurface(width, height, pixels, stride);
    }
}
