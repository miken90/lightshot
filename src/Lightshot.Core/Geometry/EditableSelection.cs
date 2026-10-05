// Ported from LightshotKit/Sources/LightshotKit/EditableSelection.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// The aspect-ratio lock for a recording selection.
/// </summary>
public enum AspectRatio
{
    Freeform,
    R16x9,
    R4x3,
    R1x1,
    R9x16,
    R5x4
}

public static class AspectRatioExtensions
{
    public static double? Value(this AspectRatio ratio) => ratio switch
    {
        AspectRatio.Freeform => null,
        AspectRatio.R16x9 => 16.0 / 9.0,
        AspectRatio.R4x3 => 4.0 / 3.0,
        AspectRatio.R1x1 => 1.0,
        AspectRatio.R9x16 => 9.0 / 16.0,
        AspectRatio.R5x4 => 5.0 / 4.0,
        _ => null
    };

    public static string Title(this AspectRatio ratio) => ratio switch
    {
        AspectRatio.Freeform => "Freeform",
        AspectRatio.R16x9 => "16:9",
        AspectRatio.R4x3 => "4:3",
        AspectRatio.R1x1 => "1:1",
        AspectRatio.R9x16 => "9:16",
        AspectRatio.R5x4 => "5:4",
        _ => "Freeform"
    };
}

/// <summary>
/// The recording overlay's selection as pure geometry.
/// </summary>
public struct EditableSelection : IEquatable<EditableSelection>
{
    public abstract record DragKind
    {
        public sealed record Draw : DragKind
        {
            public static readonly Draw Instance = new();
        }
        public sealed record Move : DragKind
        {
            public static readonly Move Instance = new();
        }
        public sealed record Resize(Handle Handle) : DragKind;
    }

    public const double MinimumSide = 4.0;
    public const double HandleGrabRadius = 8.0;

    public Rect Bounds { get; }
    public AspectRatio Ratio { get; private set; }
    public Rect? Rect { get; private set; }

    private DragState? _drag;

    private readonly record struct DragState(DragKind Kind, Point Anchor, Rect? StartRect);

    public EditableSelection(Rect bounds, AspectRatio ratio = AspectRatio.Freeform, Rect? rect = null)
    {
        Bounds = bounds.Standardized;
        Ratio = ratio;
        if (rect is not null)
        {
            Rect? inside = rect.Value.Standardized.Intersection(Bounds);
            if (inside is not null && inside.Value.Width >= MinimumSide && inside.Value.Height >= MinimumSide)
            {
                Rect = inside;
            }
            else
            {
                Rect = null;
            }
        }
        else
        {
            Rect = null;
        }
        _drag = null;
    }

    public bool IsDragging => _drag is not null;

    public bool HasSelection =>
        Rect is not null && Rect.Value.Width >= MinimumSide && Rect.Value.Height >= MinimumSide;

    public DragKind DetermineDragKind(Point point)
    {
        if (Rect is null || !HasSelection)
        {
            return DragKind.Draw.Instance;
        }

        foreach (Handle handle in Enum.GetValues<Handle>())
        {
            if (GeometryUtils.HandlePoint(handle, Rect.Value).Distance(point) <= HandleGrabRadius)
            {
                return new DragKind.Resize(handle);
            }
        }

        return Rect.Value.Contains(point) ? DragKind.Move.Instance : DragKind.Draw.Instance;
    }

    public void DragBegan(Point point)
    {
        DragKind kind = DetermineDragKind(point);
        switch (kind)
        {
            case DragKind.Draw:
                Point anchor = Clamp(point);
                _drag = new DragState(DragKind.Draw.Instance, anchor, null);
                Rect = new Rect(anchor, new Size(0, 0));
                break;
            case DragKind.Move:
                _drag = new DragState(DragKind.Move.Instance, point, Rect);
                break;
            case DragKind.Resize r:
                if (Rect is not null)
                {
                    _drag = new DragState(r, AnchorOpposite(r.Handle, Rect.Value), Rect);
                }
                break;
        }
    }

    public void DragChanged(Point point, bool forceSquare = false)
    {
        if (_drag is null) return;
        double? lockRatio = forceSquare ? 1.0 : Ratio.Value();
        switch (_drag.Value.Kind)
        {
            case DragKind.Draw:
                Rect = Fitted(_drag.Value.Anchor, point, lockRatio);
                break;
            case DragKind.Move:
                if (_drag.Value.StartRect is not null)
                {
                    Rect = Translated(_drag.Value.StartRect.Value, new Point(point.X - _drag.Value.Anchor.X, point.Y - _drag.Value.Anchor.Y));
                }
                break;
            case DragKind.Resize r:
                if (_drag.Value.StartRect is not null)
                {
                    Rect = Resized(_drag.Value.StartRect.Value, r.Handle, _drag.Value.Anchor, point, lockRatio);
                }
                break;
        }
    }

    public void DragEnded(Point point, bool forceSquare = false)
    {
        DragChanged(point, forceSquare);
        _drag = null;
        if (!HasSelection)
        {
            Rect = null;
        }
    }

    public void Nudge(double dx, double dy)
    {
        if (Rect is null || !HasSelection || IsDragging) return;
        Rect = Translated(Rect.Value, new Point(dx, dy));
    }

    public void Resize(double dw, double dh)
    {
        if (Rect is null || !HasSelection || IsDragging) return;
        if (Ratio.Value() is { } lockRatio)
        {
            double width = dw != 0 ? Rect.Value.Width + dw : (Rect.Value.Height + dh) * lockRatio;
            SetSize(width, width / lockRatio);
        }
        else
        {
            SetSize(Rect.Value.Width + dw, Rect.Value.Height + dh);
        }
    }

    public void SetWidth(double width)
    {
        if (Rect is null || !HasSelection) return;
        SetSize(width, Ratio.Value().HasValue ? width / Ratio.Value()!.Value : Rect.Value.Height);
    }

    public void SetHeight(double height)
    {
        if (Rect is null || !HasSelection) return;
        SetSize(Ratio.Value().HasValue ? height * Ratio.Value()!.Value : Rect.Value.Width, height);
    }

    public void SetRatio(AspectRatio ratio)
    {
        Ratio = ratio;
        if (Rect is null || !HasSelection || Ratio.Value() is not { } lockRatio) return;
        SetSize(Rect.Value.Width, Rect.Value.Width / lockRatio);
    }

    public static Rect DefaultRecordingRect(Rect bounds)
    {
        double width = Math.Min(1280, bounds.Width * 0.6);
        double height = width * 9.0 / 16.0;
        if (height > bounds.Height * 0.7)
        {
            height = bounds.Height * 0.7;
            width = height * 16.0 / 9.0;
        }
        return new Rect(
            bounds.MinX + (bounds.Width - width) / 2.0,
            bounds.MinY + (bounds.Height - height) / 2.0,
            width,
            height
        );
    }

    public void Snap(Rect frame)
    {
        _drag = null;
        Rect = frame.Standardized.Intersection(Bounds);
    }

    private void SetSize(double width, double height)
    {
        if (Rect is null) return;
        double w = Math.Max(MinimumSide, width);
        double h = Math.Max(MinimumSide, height);

        double maxW = Bounds.MaxX - Rect.Value.MinX;
        double maxH = Bounds.MaxY - Rect.Value.MinY;

        if (Ratio.Value() is { } lockRatio)
        {
            double scale = Math.Min(1.0, Math.Min(maxW / w, maxH / h));
            w *= scale;
            h = w / lockRatio;
        }
        else
        {
            w = Math.Min(w, maxW);
            h = Math.Min(h, maxH);
        }

        Rect = new Rect(Rect.Value.MinX, Rect.Value.MinY, w, h);
    }

    private Point Clamp(Point point) =>
        new(
            Math.Min(Math.Max(point.X, Bounds.MinX), Bounds.MaxX),
            Math.Min(Math.Max(point.Y, Bounds.MinY), Bounds.MaxY)
        );

    private Rect Translated(Rect r, Point delta)
    {
        double x = Math.Min(Math.Max(r.MinX + delta.X, Bounds.MinX), Bounds.MaxX - r.Width);
        double y = Math.Min(Math.Max(r.MinY + delta.Y, Bounds.MinY), Bounds.MaxY - r.Height);
        return new Rect(x, y, r.Width, r.Height);
    }

    private Rect Fitted(Point anchor, Point point, double? lockRatio)
    {
        Point target = Clamp(point);
        double dx = target.X - anchor.X;
        double dy = target.Y - anchor.Y;

        if (lockRatio is { } l)
        {
            double sx = dx < 0 ? -1.0 : 1.0;
            double sy = dy < 0 ? -1.0 : 1.0;
            double w = Math.Abs(dx);
            double h = Math.Abs(dy);

            if (w >= h * l)
            {
                h = w / l;
            }
            else
            {
                w = h * l;
            }

            double roomX = sx > 0 ? Bounds.MaxX - anchor.X : anchor.X - Bounds.MinX;
            double roomY = sy > 0 ? Bounds.MaxY - anchor.Y : anchor.Y - Bounds.MinY;

            double scale = Math.Min(1.0, Math.Min(w > 0 ? roomX / w : 1.0, h > 0 ? roomY / h : 1.0));
            w *= scale;
            h = w / l;
            dx = sx * w;
            dy = sy * h;
        }

        return GeometryUtils.RectBetween(anchor, new Point(anchor.X + dx, anchor.Y + dy));
    }

    private Rect Resized(Rect start, Handle handle, Point anchor, Point point, double? lockRatio)
    {
        Point target = Clamp(point);
        switch (handle)
        {
            case Handle.TopLeft or Handle.TopRight or Handle.BottomLeft or Handle.BottomRight:
                return Fitted(anchor, target, lockRatio);
            case Handle.Left or Handle.Right:
            {
                double dx = target.X - anchor.X;
                if (lockRatio is null)
                {
                    return new Rect(Math.Min(anchor.X, anchor.X + dx), start.MinY, Math.Abs(dx), start.Height);
                }
                double l = lockRatio.Value;
                double roomX = dx < 0 ? anchor.X - Bounds.MinX : Bounds.MaxX - anchor.X;
                double width = Math.Min(Math.Abs(dx), roomX);
                double height = width / l;
                double roomY = Bounds.MaxY - start.MinY;
                if (height > roomY)
                {
                    height = roomY;
                    width = height * l;
                }
                return new Rect(dx < 0 ? anchor.X - width : anchor.X, start.MinY, width, height);
            }
            case Handle.Top or Handle.Bottom:
            {
                double dy = target.Y - anchor.Y;
                if (lockRatio is null)
                {
                    return new Rect(start.MinX, Math.Min(anchor.Y, anchor.Y + dy), start.Width, Math.Abs(dy));
                }
                double l = lockRatio.Value;
                double roomY = dy < 0 ? anchor.Y - Bounds.MinY : Bounds.MaxY - anchor.Y;
                double height = Math.Min(Math.Abs(dy), roomY);
                double width = height * l;
                double roomX = Bounds.MaxX - start.MinX;
                if (width > roomX)
                {
                    width = roomX;
                    height = width / l;
                }
                return new Rect(start.MinX, dy < 0 ? anchor.Y - height : anchor.Y, width, height);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
        }
    }

    private static Point AnchorOpposite(Handle handle, Rect rect)
    {
        Handle opposite = handle switch
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
        return GeometryUtils.HandlePoint(opposite, rect);
    }

    public bool Equals(EditableSelection other) =>
        Bounds.Equals(other.Bounds) &&
        Ratio == other.Ratio &&
        Nullable.Equals(Rect, other.Rect);

    public override bool Equals(object? obj) => obj is EditableSelection other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Bounds, (int)Ratio, Rect);

    public static bool operator ==(EditableSelection left, EditableSelection right) => left.Equals(right);
    public static bool operator !=(EditableSelection left, EditableSelection right) => !left.Equals(right);
}
