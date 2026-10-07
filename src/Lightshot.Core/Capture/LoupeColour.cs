// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Samples a pixel of a captured image for the loupe readout.
/// </summary>
public static class LoupeColour
{
    /// <summary>
    /// The pixel at (x, y) as uppercase "#RRGGBB", or null when it lies outside the image or the
    /// buffer is too short. Captured images are 32-bit BGRA with a tight stride.
    /// </summary>
    public static string? SampleHex(CapturedImage image, int x, int y)
    {
        if (x < 0 || y < 0 || x >= image.PixelWidth || y >= image.PixelHeight) return null;

        long offset = ((long)y * image.PixelWidth + x) * 4;
        if (offset + 4 > image.Data.Length) return null;

        var px = image.Data.Span;
        return Hex(px[(int)offset + 2], px[(int)offset + 1], px[(int)offset]);
    }

    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
