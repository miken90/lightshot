// Ported from LightshotKit/Sources/LightshotKit/CanvasProjection.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Pure mapping between an editor's view space and the document's image pixel coordinates,
/// for an image shown aspect-fit (letter-boxed) in a view.
/// </summary>
public readonly record struct CanvasProjection
{
    public double Scale { get; }
    public Point ImageOrigin { get; }

    public CanvasProjection(Size imageSize, Size viewSize, double inset = 0)
    {
        double sx = imageSize.Width > 0 ? Math.Max(viewSize.Width - inset * 2.0, 0) / imageSize.Width : 1;
        double sy = imageSize.Height > 0 ? Math.Max(viewSize.Height - inset * 2.0, 0) / imageSize.Height : 1;
        double s = Math.Min(sx, sy);
        Scale = s;
        ImageOrigin = new Point(
            (viewSize.Width - imageSize.Width * s) / 2.0,
            (viewSize.Height - imageSize.Height * s) / 2.0
        );
    }

    /// <summary>
    /// Maps an image-space point to view space.
    /// </summary>
    public Point ToView(Point p) =>
        new(ImageOrigin.X + p.X * Scale, ImageOrigin.Y + p.Y * Scale);

    /// <summary>
    /// Maps a view-space point back to image space.
    /// </summary>
    public Point ToImage(Point p)
    {
        if (Scale <= 0)
        {
            return new Point(0, 0);
        }
        return new Point((p.X - ImageOrigin.X) / Scale, (p.Y - ImageOrigin.Y) / Scale);
    }

    /// <summary>
    /// Maps an image-space rect to view space (origin projected, extent scaled).
    /// </summary>
    public Rect ToView(Rect r)
    {
        Point o = ToView(r.Origin);
        return new Rect(o.X, o.Y, r.Size.Width * Scale, r.Size.Height * Scale);
    }

    /// <summary>
    /// Scales an image-space length (e.g. a radius or stroke width) to view space.
    /// </summary>
    public double ToView(double length) => length * Scale;
}
