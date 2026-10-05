// Ported from LightshotKit/Sources/LightshotKit/ArrowGeometry.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

/// <summary>
/// The draggable points of a selected line or arrow. Lines and straight arrows expose
/// tail and tip; a bendable arrow adds bend, which sits on the shaft's middle.
/// </summary>
public enum EndpointHandle
{
    Tail,
    Tip,
    Bend
}

/// <summary>
/// One step of a vector outline, in the same space as the points that produced it.
/// </summary>
public abstract record PathElement
{
    public sealed record Move(Point Point) : PathElement;
    public sealed record Line(Point Point) : PathElement;
    public sealed record QuadCurve(Point To, Point Control) : PathElement;
    public sealed record Close : PathElement
    {
        public static readonly Close Instance = new();
    }
}

/// <summary>
/// A drawable arrow: an outline plus how to paint it.
/// </summary>
public readonly record struct ArrowShape(IReadOnlyList<PathElement> Path, ArrowShapePaint Paint);

public abstract record ArrowShapePaint
{
    public sealed record Fill(double Rounding) : ArrowShapePaint;
    public sealed record Stroke(double Width) : ArrowShapePaint;
}

public static class ArrowGeometry
{
    public const int CenterlineSamples = 16;

    /// <summary>
    /// The midpoint of the straight segment a-b - where a bend handle rests before it is dragged.
    /// </summary>
    public static Point ArrowMidpoint(Point a, Point b) =>
        new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    /// <summary>
    /// The bend a freshly drawn bendable arrow starts with: the shaft's midpoint.
    /// </summary>
    public static Point DefaultArrowBend(Point from, Point to) => ArrowMidpoint(from, to);

    /// <summary>
    /// The shaft's centerline as a polyline - two points when straight, a sampled quadratic through bend otherwise.
    /// </summary>
    public static IReadOnlyList<Point> ArrowCenterline(Point from, Point to, Point? bend)
    {
        if (bend is null)
        {
            return [from, to];
        }

        Point control = BendControl(from, to, bend.Value);
        var result = new Point[CenterlineSamples + 1];
        for (int i = 0; i <= CenterlineSamples; i++)
        {
            double t = (double)i / CenterlineSamples;
            double a = (1.0 - t) * (1.0 - t);
            double b = 2.0 * (1.0 - t) * t;
            double c = t * t;
            result[i] = new Point(
                a * from.X + b * control.X + c * to.X,
                a * from.Y + b * control.Y + c * to.Y
            );
        }
        return result;
    }

    /// <summary>
    /// The outline of an arrow from from (tail) to to (tip) in style, sized from lineWidth.
    /// </summary>
    public static ArrowShape ComputeArrowShape(
        Point from, Point to, Point? bend, ArrowStyle style, double lineWidth
    )
    {
        double length = from.Distance(to);
        if (length <= 0)
        {
            return new ArrowShape(Array.Empty<PathElement>(), new ArrowShapePaint.Fill(0));
        }

        return style switch
        {
            ArrowStyle.Standard or ArrowStyle.Fancy =>
                TaperedArrow(from, to, length, style == ArrowStyle.Fancy, lineWidth),
            ArrowStyle.Curved or ArrowStyle.Double =>
                BentTaperedArrow(from, to, bend, length, style == ArrowStyle.Double, lineWidth),
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, null)
        };
    }

    private static ArrowShape TaperedArrow(Point from, Point to, double length, bool fancy, double lineWidth)
    {
        Point u = new((to.X - from.X) / length, (to.Y - from.Y) / length);
        Point p = new(-u.Y, u.X);

        double head = Math.Min(lineWidth * 5.0, length * 0.6);
        double rounding = fancy ? 0 : head * 0.1;
        double headHalf = head * 0.5;
        double shaftHalf = head * 0.17;
        double tailHalf = head * (fancy ? 0.02 : 0.04);

        Point At(double along, double across) =>
            new(to.X - u.X * along + p.X * across, to.Y - u.Y * along + p.Y * across);

        double inset = rounding / 2.0;
        double neck = fancy ? head * 0.8 : head;

        var path = new PathElement[]
        {
            new PathElement.Move(At(length - inset, tailHalf)),
            new PathElement.Line(At(neck, shaftHalf)),
            new PathElement.Line(At(head, headHalf)),
            new PathElement.Line(At(inset, 0)),
            new PathElement.Line(At(head, -headHalf)),
            new PathElement.Line(At(neck, -shaftHalf)),
            new PathElement.Line(At(length - inset, -tailHalf)),
            PathElement.Close.Instance
        };

        return new ArrowShape(path, new ArrowShapePaint.Fill(rounding));
    }

    private static ArrowShape BentTaperedArrow(
        Point from, Point to, Point? bend, double length, bool bothEnds, double lineWidth
    )
    {
        Point control = BendControl(from, to, bend ?? ArrowMidpoint(from, to));
        double head = Math.Min(lineWidth * 5.0, length * (bothEnds ? 0.4 : 0.6));
        double rounding = head * 0.1;
        double headHalf = head * 0.5;
        double shaftHalf = head * 0.17;
        double tailHalf = bothEnds ? shaftHalf : head * 0.04;
        double inset = rounding / 2.0;

        Point Curve(double t)
        {
            double a = (1.0 - t) * (1.0 - t);
            double b = 2.0 * (1.0 - t) * t;
            double c = t * t;
            return new Point(
                a * from.X + b * control.X + c * to.X,
                a * from.Y + b * control.Y + c * to.Y
            );
        }

        Point Tangent(double t)
        {
            double dx = (1.0 - t) * (control.X - from.X) + t * (to.X - control.X);
            double dy = (1.0 - t) * (control.Y - from.Y) + t * (to.Y - control.Y);
            double d = Math.Sqrt(dx * dx + dy * dy);
            return d > 0 ? new Point(dx / d, dy / d) : new Point((to.X - from.X) / length, (to.Y - from.Y) / length);
        }

        double NeckParameter(bool atTip)
        {
            Point end = atTip ? to : from;
            double outside = atTip ? 0.0 : 1.0;
            double inside = atTip ? 1.0 : 0.0;
            for (int i = 1; i <= CenterlineSamples; i++)
            {
                double step = (double)i / CenterlineSamples;
                double t = atTip ? 1.0 - step : step;
                if (Curve(t).Distance(end) >= head)
                {
                    outside = t;
                    break;
                }
                inside = t;
            }
            for (int iter = 0; iter < 24; iter++)
            {
                double mid = (outside + inside) / 2.0;
                if (Curve(mid).Distance(end) >= head)
                {
                    outside = mid;
                }
                else
                {
                    inside = mid;
                }
            }
            return outside;
        }

        List<PathElement> HeadOutline(Point neck, Point end)
        {
            double d = Math.Max(neck.Distance(end), double.Epsilon);
            Point u = new((end.X - neck.X) / d, (end.Y - neck.Y) / d);
            Point p = new(-u.Y, u.X);
            return
            [
                new PathElement.Line(new Point(neck.X + p.X * headHalf, neck.Y + p.Y * headHalf)),
                new PathElement.Line(new Point(end.X - u.X * inset, end.Y - u.Y * inset)),
                new PathElement.Line(new Point(neck.X - p.X * headHalf, neck.Y - p.Y * headHalf))
            ];
        }

        double tipT = NeckParameter(atTip: true);
        double tailT = bothEnds ? NeckParameter(atTip: false) : inset / length;
        var left = new List<Point>(CenterlineSamples + 1);
        var right = new List<Point>(CenterlineSamples + 1);

        for (int i = 0; i <= CenterlineSamples; i++)
        {
            double f = (double)i / CenterlineSamples;
            double t = tailT + (tipT - tailT) * f;
            Point c = Curve(t);
            Point dir = Tangent(t);
            double half = tailHalf + (shaftHalf - tailHalf) * f;
            left.Add(new Point(c.X - dir.Y * half, c.Y + dir.X * half));
            right.Add(new Point(c.X + dir.Y * half, c.Y - dir.X * half));
        }

        var path = new List<PathElement> { new PathElement.Move(left[0]) };
        for (int i = 1; i < left.Count; i++)
        {
            path.Add(new PathElement.Line(left[i]));
        }
        path.AddRange(HeadOutline(Curve(tipT), to));
        for (int i = right.Count - 1; i >= 0; i--)
        {
            path.Add(new PathElement.Line(right[i]));
        }
        if (bothEnds)
        {
            path.AddRange(HeadOutline(Curve(tailT), from));
        }
        path.Add(PathElement.Close.Instance);

        return new ArrowShape(path, new ArrowShapePaint.Fill(rounding));
    }

    private static Point BendControl(Point from, Point to, Point bend)
    {
        Point mid = ArrowMidpoint(from, to);
        return new Point(2.0 * bend.X - mid.X, 2.0 * bend.Y - mid.Y);
    }

    /// <summary>
    /// Where bend lands after the shaft from->to becomes newFrom->newTo.
    /// </summary>
    public static Point CarriedBend(Point bend, Point from, Point to, Point newFrom, Point newTo)
    {
        Point v = to - from;
        double lengthSquared = v.X * v.X + v.Y * v.Y;
        if (lengthSquared <= 0)
        {
            Point oldMid = ArrowMidpoint(from, to);
            Point newMid = ArrowMidpoint(newFrom, newTo);
            return new Point(bend.X + newMid.X - oldMid.X, bend.Y + newMid.Y - oldMid.Y);
        }

        Point d = bend - from;
        double along = (d.X * v.X + d.Y * v.Y) / lengthSquared;
        double across = (v.X * d.Y - v.Y * d.X) / lengthSquared;
        Point nv = newTo - newFrom;
        return new Point(
            newFrom.X + along * nv.X - across * nv.Y,
            newFrom.Y + along * nv.Y + across * nv.X
        );
    }
}
