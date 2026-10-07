// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Aspect-ratio and boundary constraints for crop rectangles.
/// </summary>
public static class CropConstraint
{
    /// <summary>
    /// Computes the largest rectangle with the given aspect ratio (width / height)
    /// centered inside <paramref name="container"/>.
    /// </summary>
    public static Rect FitInside(Rect container, double ratio)
    {
        Rect c = container.Standardized;
        if (c.Width <= 0 || c.Height <= 0 || ratio <= 0 || double.IsNaN(ratio) || double.IsInfinity(ratio))
        {
            return c;
        }

        double cw = c.Width;
        double ch = c.Height;
        double w;
        double h;

        if (cw / ch > ratio)
        {
            h = ch;
            w = ch * ratio;
        }
        else
        {
            w = cw;
            h = cw / ratio;
        }

        double x = c.MinX + (cw - w) / 2.0;
        double y = c.MinY + (ch - h) / 2.0;
        return new Rect(x, y, w, h);
    }

    /// <summary>
    /// Resizes <paramref name="original"/> by moving <paramref name="handle"/> to <paramref name="current"/>,
    /// constraining the aspect ratio to <paramref name="ratio"/> and clamping within <paramref name="bounds"/>.
    /// </summary>
    public static Rect Constrain(Rect original, Handle handle, Point current, double ratio, Rect bounds)
    {
        Rect b = bounds.Standardized;
        Rect orig = original.Standardized;

        if (b.Width <= 0 || b.Height <= 0)
        {
            return b;
        }

        if (ratio <= 0 || double.IsNaN(ratio) || double.IsInfinity(ratio))
        {
            Handle opp = AnchorOpposite(handle);
            Point anc = GeometryUtils.HandlePoint(opp, orig);
            Point cl = new(Math.Clamp(current.X, b.MinX, b.MaxX), Math.Clamp(current.Y, b.MinY, b.MaxY));
            return GeometryUtils.RectBetween(anc, cl);
        }

        switch (handle)
        {
            case Handle.TopLeft or Handle.TopRight or Handle.BottomLeft or Handle.BottomRight:
            {
                Point anchor = handle switch
                {
                    Handle.TopLeft => new Point(orig.MaxX, orig.MaxY),
                    Handle.TopRight => new Point(orig.MinX, orig.MaxY),
                    Handle.BottomRight => new Point(orig.MinX, orig.MinY),
                    Handle.BottomLeft => new Point(orig.MaxX, orig.MinY),
                    _ => Point.Zero
                };

                double tx = Math.Clamp(current.X, b.MinX, b.MaxX);
                double ty = Math.Clamp(current.Y, b.MinY, b.MaxY);
                double dx = tx - anchor.X;
                double dy = ty - anchor.Y;

                if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
                {
                    return new Rect(anchor.X, anchor.Y, 0, 0);
                }

                double sx = dx < 0 ? -1.0 : 1.0;
                double sy = dy < 0 ? -1.0 : 1.0;
                double w = Math.Abs(dx);
                double h = Math.Abs(dy);

                if (w >= h * ratio)
                {
                    h = w / ratio;
                }
                else
                {
                    w = h * ratio;
                }

                double roomX = sx > 0 ? b.MaxX - anchor.X : anchor.X - b.MinX;
                double roomY = sy > 0 ? b.MaxY - anchor.Y : anchor.Y - b.MinY;
                roomX = Math.Max(0, roomX);
                roomY = Math.Max(0, roomY);

                double scale = Math.Min(1.0, Math.Min(w > 0 ? roomX / w : 1.0, h > 0 ? roomY / h : 1.0));
                w *= scale;
                h = w / ratio;

                Point target = new(anchor.X + sx * w, anchor.Y + sy * h);
                return GeometryUtils.RectBetween(anchor, target);
            }

            case Handle.Left or Handle.Right:
            {
                double anchorX = handle == Handle.Left ? orig.MaxX : orig.MinX;
                double sx = handle == Handle.Left ? -1.0 : 1.0;
                double tx = Math.Clamp(current.X, b.MinX, b.MaxX);
                double dx = tx - anchorX;

                if ((handle == Handle.Left && dx > 0) || (handle == Handle.Right && dx < 0))
                {
                    sx = dx < 0 ? -1.0 : 1.0;
                }

                double w = Math.Abs(dx);
                double roomX = sx > 0 ? b.MaxX - anchorX : anchorX - b.MinX;
                w = Math.Min(w, Math.Max(0, roomX));
                double h = w / ratio;

                if (h > b.Height)
                {
                    h = b.Height;
                    w = h * ratio;
                }

                double y = orig.MidY - h / 2.0;
                if (y < b.MinY) y = b.MinY;
                if (y + h > b.MaxY) y = b.MaxY - h;

                double x = sx > 0 ? anchorX : anchorX - w;
                return new Rect(x, y, w, h);
            }

            case Handle.Top or Handle.Bottom:
            {
                double anchorY = handle == Handle.Top ? orig.MaxY : orig.MinY;
                double sy = handle == Handle.Top ? -1.0 : 1.0;
                double ty = Math.Clamp(current.Y, b.MinY, b.MaxY);
                double dy = ty - anchorY;

                if ((handle == Handle.Top && dy > 0) || (handle == Handle.Bottom && dy < 0))
                {
                    sy = dy < 0 ? -1.0 : 1.0;
                }

                double h = Math.Abs(dy);
                double roomY = sy > 0 ? b.MaxY - anchorY : anchorY - b.MinY;
                h = Math.Min(h, Math.Max(0, roomY));
                double w = h * ratio;

                if (w > b.Width)
                {
                    w = b.Width;
                    h = w / ratio;
                }

                double x = orig.MidX - w / 2.0;
                if (x < b.MinX) x = b.MinX;
                if (x + w > b.MaxX) x = b.MaxX - w;

                double y = sy > 0 ? anchorY : anchorY - h;
                return new Rect(x, y, w, h);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
        }
    }

    private static Handle AnchorOpposite(Handle handle) => handle switch
    {
        Handle.TopLeft => Handle.BottomRight,
        Handle.Top => Handle.Bottom,
        Handle.TopRight => Handle.BottomLeft,
        Handle.Right => Handle.Left,
        Handle.BottomRight => Handle.TopLeft,
        Handle.Bottom => Handle.Top,
        Handle.BottomLeft => Handle.TopRight,
        Handle.Left => Handle.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, null)
    };
}
