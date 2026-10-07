// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Where the capture loupe sits and which way it flipped to stay on its monitor.
/// </summary>
public readonly record struct LoupeLayout(Rect Bounds, bool FlippedX, bool FlippedY);

/// <summary>
/// Pure placement maths for the capture loupe: below-right of the pointer by default,
/// flipped left or above near the monitor edges, and always clamped inside the monitor.
/// </summary>
public static class LoupeGeometry
{
    /// <summary>Gap between the pointer and the loupe at 100% scale, in pixels.</summary>
    public const double PointerOffset = 24;

    /// <param name="pointer">Pointer position in the same coordinate space as <paramref name="monitor"/>.</param>
    /// <param name="monitor">Bounds of the monitor the loupe is drawn on.</param>
    /// <param name="loupeSize">Loupe size in device pixels.</param>
    /// <param name="scale">DPI scale; multiplies the pointer offset.</param>
    public static LoupeLayout Calculate(Point pointer, Rect monitor, Size loupeSize, double scale = 1.0)
    {
        double offset = PointerOffset * (scale > 0 ? scale : 1.0);
        double w = loupeSize.Width;
        double h = loupeSize.Height;

        bool flipX = pointer.X + offset + w > monitor.MaxX;
        bool flipY = pointer.Y + offset + h > monitor.MaxY;

        double x = flipX ? pointer.X - offset - w : pointer.X + offset;
        double y = flipY ? pointer.Y - offset - h : pointer.Y + offset;

        // The corner case: a flip can push the loupe off the opposite edge. A loupe larger than
        // the monitor pins to the top-left so its readout stays visible.
        x = Math.Max(monitor.MinX, Math.Min(x, monitor.MaxX - w));
        y = Math.Max(monitor.MinY, Math.Min(y, monitor.MaxY - h));

        return new LoupeLayout(new Rect(x, y, w, h), flipX, flipY);
    }
}
