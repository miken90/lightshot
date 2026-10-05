// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// Detects blank or all-black (DRM / protected content) frames.
/// A frame is flagged as black if all color channels (R, G, B) are zero across all pixels.
/// </summary>
public static class BlackFrameDetector
{
    /// <summary>
    /// Checks whether the provided BGRA pixel span contains only black pixels (R=0, G=0, B=0).
    /// Returns false immediately on the first non-black pixel found.
    /// </summary>
    public static bool IsBlackFrame(ReadOnlySpan<byte> bgraPixels, int width, int height, int stride = 0)
    {
        if (width <= 0 || height <= 0 || bgraPixels.IsEmpty)
        {
            return false;
        }

        if (stride <= 0)
        {
            stride = width * 4;
        }

        int bytesPerRow = width * 4;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            if (rowOffset + bytesPerRow > bgraPixels.Length)
            {
                break;
            }

            var row = bgraPixels.Slice(rowOffset, bytesPerRow);
            for (int x = 0; x < bytesPerRow; x += 4)
            {
                byte b = row[x];
                byte g = row[x + 1];
                byte r = row[x + 2];

                if (b != 0 || g != 0 || r != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether a CapturedImage is an all-black frame.
    /// </summary>
    public static bool IsBlackFrame(CapturedImage image)
    {
        return IsBlackFrame(image.Data.Span, image.PixelWidth, image.PixelHeight);
    }

    /// <summary>
    /// Checks whether a PixelSurface is an all-black frame.
    /// </summary>
    public static bool IsBlackFrame(PixelSurface surface)
    {
        if (surface == null) return false;
        return IsBlackFrame(surface.Pixels, surface.Width, surface.Height, surface.Stride);
    }
}
