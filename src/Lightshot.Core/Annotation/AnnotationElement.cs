// Ported from LightshotKit/Sources/LightshotKit/AnnotationElement.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

/// <summary>
/// Stable identity for an annotation element, handed back by add so callers
/// can address later commands at it.
/// </summary>
public readonly record struct ElementID(Guid Raw)
{
    public ElementID() : this(Guid.NewGuid()) { }
}

/// <summary>
/// One annotation on the image: an identity, a geometric Kind, and a Style.
/// </summary>
public class AnnotationElement : IEquatable<AnnotationElement>
{
    public ElementID Id { get; }
    public Kind ElementKind { get; set; }
    public Style Style { get; set; }

    public AnnotationElement(ElementID? id = null, Kind? kind = null, Style? style = null)
    {
        Id = id ?? new ElementID();
        ElementKind = kind ?? new Kind.Rectangle(Rect.Zero);
        Style = style ?? Style.Default;
    }

    public abstract record Kind
    {
        public sealed record Arrow(Point From, Point To, Point? Bend = null, ArrowStyle Style = ArrowStyle.Standard) : Kind;
        public sealed record Line(Point From, Point To) : Kind;
        public sealed record Rectangle(Rect Rect) : Kind;
        public sealed record Ellipse(Rect Rect) : Kind;
        public sealed record Freehand(IReadOnlyList<Point> Points) : Kind;
        public sealed record Text(string Content, Rect Box) : Kind;
        public sealed record Highlight(Rect Rect) : Kind;
        public sealed record Redaction(
            Rect Rect,
            RedactionStyle Style,
            double Strength = RedactionStyleDefaults.DefaultStrength,
            ulong Seed = 0) : Kind;
        public sealed record StepMarker(int Number, Point Center, double Radius) : Kind;
        public sealed record Focus(Rect Rect) : Kind;

        public Rect? FocusRect => this is Focus f ? f.Rect : null;

        public Rect BoundingBox => this switch
        {
            Arrow a => BoundingBoxOf(a.Bend.HasValue ? [a.From, a.To, a.Bend.Value] : [a.From, a.To]),
            Line l => BoundingBoxOf([l.From, l.To]),
            Rectangle r => r.Rect.Standardized,
            Ellipse e => e.Rect.Standardized,
            Highlight h => h.Rect.Standardized,
            Redaction red => red.Rect.Standardized,
            Focus f => f.Rect.Standardized,
            Text t => t.Box.Standardized,
            Freehand fh => BoundingBoxOf(fh.Points),
            StepMarker sm => new Rect(sm.Center.X - sm.Radius, sm.Center.Y - sm.Radius, sm.Radius * 2.0, sm.Radius * 2.0),
            _ => Rect.Zero
        };

        public IReadOnlyList<(EndpointHandle Handle, Point Point)>? EndpointHandles => this switch
        {
            Line l => [(EndpointHandle.Tail, l.From), (EndpointHandle.Tip, l.To)],
            Arrow a => a.Style.IsBendable()
                ? [(EndpointHandle.Tail, a.From), (EndpointHandle.Tip, a.To), (EndpointHandle.Bend, a.Bend ?? ArrowGeometry.ArrowMidpoint(a.From, a.To))]
                : [(EndpointHandle.Tail, a.From), (EndpointHandle.Tip, a.To)],
            _ => null
        };

        public Kind Moved(double dx, double dy)
        {
            Point Shift(Point p) => new(p.X + dx, p.Y + dy);
            Rect ShiftR(Rect r) => new(Shift(r.Origin), r.Size);

            return this switch
            {
                Arrow a => new Arrow(Shift(a.From), Shift(a.To), a.Bend.HasValue ? Shift(a.Bend.Value) : null, a.Style),
                Line l => new Line(Shift(l.From), Shift(l.To)),
                Rectangle r => new Rectangle(ShiftR(r.Rect)),
                Ellipse e => new Ellipse(ShiftR(e.Rect)),
                Highlight h => new Highlight(ShiftR(h.Rect)),
                Focus f => new Focus(ShiftR(f.Rect)),
                Redaction red => new Redaction(ShiftR(red.Rect), red.Style, red.Strength, red.Seed),
                Text t => new Text(t.Content, ShiftR(t.Box)),
                Freehand fh => new Freehand(fh.Points.Select(Shift).ToList()),
                StepMarker sm => new StepMarker(sm.Number, Shift(sm.Center), sm.Radius),
                _ => this
            };
        }

        public Kind Resized(Handle handle, double dx, double dy)
        {
            Rect old = BoundingBox;
            return Resized(old, old.Resized(handle, dx, dy));
        }

        public Kind Resized(Rect oldBox, Rect newBox)
        {
            Point RemapP(Point p) => Remap(p, oldBox, newBox);
            Rect RemapR(Rect r)
            {
                Point a = RemapP(r.Origin);
                Point b = RemapP(new Point(r.Origin.X + r.Size.Width, r.Origin.Y + r.Size.Height));
                return new Rect(a.X, a.Y, b.X - a.X, b.Y - a.Y);
            }

            return this switch
            {
                Arrow a => new Arrow(RemapP(a.From), RemapP(a.To), a.Bend.HasValue ? RemapP(a.Bend.Value) : null, a.Style),
                Line l => new Line(RemapP(l.From), RemapP(l.To)),
                Rectangle r => new Rectangle(RemapR(r.Rect)),
                Ellipse e => new Ellipse(RemapR(e.Rect)),
                Highlight h => new Highlight(RemapR(h.Rect)),
                Focus f => new Focus(RemapR(f.Rect)),
                Redaction red => new Redaction(RemapR(red.Rect), red.Style, red.Strength, red.Seed),
                Text t => new Text(t.Content, RemapR(t.Box)),
                Freehand fh => new Freehand(fh.Points.Select(RemapP).ToList()),
                StepMarker sm => new StepMarker(sm.Number, newBox.Center, Math.Min(newBox.Width, newBox.Height) / 2.0),
                _ => this
            };
        }

        public Kind Reshaped(EndpointHandle handle, double dx, double dy)
        {
            Point Shift(Point p) => new(p.X + dx, p.Y + dy);

            return this switch
            {
                Line l => handle switch
                {
                    EndpointHandle.Tail => new Line(Shift(l.From), l.To),
                    EndpointHandle.Tip => new Line(l.From, Shift(l.To)),
                    _ => this
                },
                Arrow a => handle switch
                {
                    EndpointHandle.Tail => ReshapeArrowTail(a, Shift(a.From)),
                    EndpointHandle.Tip => ReshapeArrowTip(a, Shift(a.To)),
                    EndpointHandle.Bend when a.Style.IsBendable() =>
                        new Arrow(a.From, a.To, Shift(a.Bend ?? ArrowGeometry.ArrowMidpoint(a.From, a.To)), a.Style),
                    _ => this
                },
                _ => this
            };
        }

        private static Arrow ReshapeArrowTail(Arrow a, Point moved)
        {
            Point? carried = a.Bend.HasValue
                ? ArrowGeometry.CarriedBend(a.Bend.Value, a.From, a.To, moved, a.To)
                : null;
            return new Arrow(moved, a.To, carried, a.Style);
        }

        private static Arrow ReshapeArrowTip(Arrow a, Point moved)
        {
            Point? carried = a.Bend.HasValue
                ? ArrowGeometry.CarriedBend(a.Bend.Value, a.From, a.To, a.From, moved)
                : null;
            return new Arrow(a.From, moved, carried, a.Style);
        }

        public bool HitTest(Point point, double tolerance) => this switch
        {
            Line l => GeometryUtils.DistanceFromPoint(point, l.From, l.To) <= tolerance,
            Arrow a => HitTestPoints(ArrowGeometry.ArrowCenterline(a.From, a.To, a.Bend), point, tolerance),
            Freehand fh => HitTestPoints(fh.Points, point, tolerance),
            Ellipse el => HitTestEllipse(el.Rect.Standardized, point),
            StepMarker sm => point.Distance(sm.Center) <= sm.Radius + tolerance,
            Rectangle or Highlight or Redaction or Text or Focus =>
                BoundingBox.InsetBy(-tolerance, -tolerance).Contains(point),
            _ => false
        };

        public bool? IsNearOutline(Point point, double tolerance) => this switch
        {
            Rectangle r => IsNearRectangleOutline(r.Rect.Standardized, point, tolerance),
            Ellipse el => IsNearEllipseOutline(el.Rect.Standardized, point, tolerance),
            _ => null
        };

        private static bool HitTestPoints(IReadOnlyList<Point> points, Point point, double tolerance)
        {
            if (points.Count == 0) return false;
            if (points.Count == 1) return point.Distance(points[0]) <= tolerance;
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (GeometryUtils.DistanceFromPoint(point, points[i], points[i + 1]) <= tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HitTestEllipse(Rect box, Point point)
        {
            if (box.Width <= 0 || box.Height <= 0) return false;
            double nx = (point.X - box.MidX) / (box.Width / 2.0);
            double ny = (point.Y - box.MidY) / (box.Height / 2.0);
            return nx * nx + ny * ny <= 1.0;
        }

        private static bool IsNearRectangleOutline(Rect box, Point point, double tolerance)
        {
            return box.InsetBy(-tolerance, -tolerance).Contains(point)
                   && !box.InsetBy(tolerance, tolerance).Contains(point);
        }

        private static bool IsNearEllipseOutline(Rect box, Point point, double tolerance)
        {
            if (box.Width <= 0 || box.Height <= 0) return false;
            double nx = (point.X - box.MidX) / (box.Width / 2.0);
            double ny = (point.Y - box.MidY) / (box.Height / 2.0);
            double d = Math.Abs(Math.Sqrt(nx * nx + ny * ny) - 1.0) * Math.Min(box.Width, box.Height) / 2.0;
            return d <= tolerance;
        }

        private static Rect BoundingBoxOf(IReadOnlyList<Point> points)
        {
            if (points.Count == 0) return Rect.Zero;
            double minX = points[0].X;
            double minY = points[0].Y;
            double maxX = points[0].X;
            double maxY = points[0].Y;

            for (int i = 1; i < points.Count; i++)
            {
                minX = Math.Min(minX, points[i].X);
                minY = Math.Min(minY, points[i].Y);
                maxX = Math.Max(maxX, points[i].X);
                maxY = Math.Max(maxY, points[i].Y);
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private static Point Remap(Point p, Rect oldBox, Rect newBox)
        {
            double fx = oldBox.Width == 0 ? 0 : (p.X - oldBox.MinX) / oldBox.Width;
            double fy = oldBox.Height == 0 ? 0 : (p.Y - oldBox.MinY) / oldBox.Height;
            return new Point(newBox.MinX + fx * newBox.Width, newBox.MinY + fy * newBox.Height);
        }
    }

    public bool Equals(AnnotationElement? other) =>
        other is not null && Id == other.Id && ElementKind.Equals(other.ElementKind) && Style.Equals(other.Style);

    public override bool Equals(object? obj) => obj is AnnotationElement other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, ElementKind, Style);
}
