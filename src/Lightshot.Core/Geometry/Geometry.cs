// Ported from LightshotKit/Sources/LightshotKit/Geometry.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// A point in image pixel coordinates. Origin is top-left; y grows downward.
/// </summary>
public readonly record struct Point(double X, double Y)
{
    public static readonly Point Zero = new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double Distance(Point other) => (this - other).Length;

    public static Point operator -(Point lhs, Point rhs) => new(lhs.X - rhs.X, lhs.Y - rhs.Y);

    public static Point operator +(Point lhs, Point rhs) => new(lhs.X + rhs.X, lhs.Y + rhs.Y);

    public override string ToString() => $"Point({X}, {Y})";
}

/// <summary>
/// A size in image pixels.
/// </summary>
public readonly record struct Size(double Width, double Height)
{
    public static readonly Size Zero = new(0, 0);

    public override string ToString() => $"Size({Width}, {Height})";
}

/// <summary>
/// An axis-aligned rectangle in image pixel coordinates.
/// </summary>
public readonly record struct Rect(Point Origin, Size Size)
{
    public static readonly Rect Zero = new(Point.Zero, Size.Zero);

    public Rect(double x, double y, double width, double height)
        : this(new Point(x, y), new Size(width, height))
    {
    }

    public override string ToString() => $"Rect({X}, {Y}, {Width}, {Height})";

    public double X => Origin.X;
    public double Y => Origin.Y;
    public double Width => Math.Abs(Size.Width);
    public double Height => Math.Abs(Size.Height);

    public double MinX => Math.Min(Origin.X, Origin.X + Size.Width);
    public double MinY => Math.Min(Origin.Y, Origin.Y + Size.Height);
    public double MaxX => Math.Max(Origin.X, Origin.X + Size.Width);
    public double MaxY => Math.Max(Origin.Y, Origin.Y + Size.Height);
    public double MidX => (MinX + MaxX) / 2.0;
    public double MidY => (MinY + MaxY) / 2.0;
    public Point Center => new(MidX, MidY);

    /// <summary>
    /// A copy with a non-negative size and its origin at the top-left corner.
    /// </summary>
    public Rect Standardized => new(MinX, MinY, Width, Height);

    /// <summary>
    /// Inclusive containment test against the standardized bounds.
    /// </summary>
    public bool Contains(Point point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

    /// <summary>
    /// Grows (positive) or shrinks (negative) the rect on all sides.
    /// </summary>
    public Rect InsetBy(double dx, double dy) =>
        new(MinX + dx, MinY + dy, Width - 2.0 * dx, Height - 2.0 * dy);

    /// <summary>
    /// The overlapping region with another rect, or null when they don't overlap.
    /// </summary>
    public Rect? Intersection(Rect other)
    {
        double x0 = Math.Max(MinX, other.MinX);
        double y0 = Math.Max(MinY, other.MinY);
        double x1 = Math.Min(MaxX, other.MaxX);
        double y1 = Math.Min(MaxY, other.MaxY);
        if (x1 <= x0 || y1 <= y0)
        {
            return null;
        }
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>
/// The eight resize handles on an element's bounding box.
/// </summary>
public enum Handle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left
}

public static class GeometryUtils
{
    /// <summary>
    /// Where the eight resize handles sit on a bounding box.
    /// </summary>
    public static Point HandlePoint(Handle handle, Rect box)
    {
        Rect b = box.Standardized;
        return handle switch
        {
            Handle.TopLeft => new Point(b.MinX, b.MinY),
            Handle.Top => new Point(b.MidX, b.MinY),
            Handle.TopRight => new Point(b.MaxX, b.MinY),
            Handle.Right => new Point(b.MaxX, b.MidY),
            Handle.BottomRight => new Point(b.MaxX, b.MaxY),
            Handle.Bottom => new Point(b.MidX, b.MaxY),
            Handle.BottomLeft => new Point(b.MinX, b.MaxY),
            Handle.Left => new Point(b.MinX, b.MidY),
            _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, null)
        };
    }

    /// <summary>
    /// The axis-aligned rect spanned by two corner points.
    /// </summary>
    public static Rect RectBetween(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    /// <summary>
    /// Shortest distance from point to the line segment a-b.
    /// </summary>
    public static double DistanceFromPoint(Point point, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0)
        {
            return point.Distance(a);
        }
        double t = Math.Max(0, Math.Min(1, ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared));
        Point projection = new(a.X + t * dx, a.Y + t * dy);
        return point.Distance(projection);
    }
}
