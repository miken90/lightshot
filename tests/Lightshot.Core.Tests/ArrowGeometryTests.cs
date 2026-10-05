// Ported from LightshotKit/Tests/LightshotKitTests/ArrowGeometryTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class ArrowGeometryTests
{
    private static readonly Point Tail = new(0, 0);
    private static readonly Point Tip = new(200, 0);

    private static List<Point> Points(ArrowShape shape)
    {
        var pts = new List<Point>();
        foreach (var element in shape.Path)
        {
            switch (element)
            {
                case PathElement.Move m: pts.Add(m.Point); break;
                case PathElement.Line l: pts.Add(l.Point); break;
                case PathElement.QuadCurve q: pts.Add(q.To); break;
            }
        }
        return pts;
    }

    [Fact]
    [Unit]
    public void TaperedStylesWidenFromTailToHead()
    {
        ArrowStyle[] styles = [ArrowStyle.Standard, ArrowStyle.Fancy];
        foreach (var style in styles)
        {
            var outline = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, null, style, 6));
            double tailWidth = outline[0].Distance(outline[^1]);
            double neckWidth = outline[1].Distance(outline[^2]);
            double headWidth = outline[2].Distance(outline[4]);
            Assert.True(tailWidth < neckWidth);
            Assert.True(neckWidth < headWidth);
        }
    }

    [Fact]
    [Unit]
    public void FancyHeadSweepsItsBarbsBackPastTheNeck()
    {
        var outline = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, null, ArrowStyle.Fancy, 6));
        Assert.True(outline[2].Distance(Tip) > outline[1].Distance(Tip));
        Assert.Contains(outline, p => p.Distance(Tip) < 0.001);
    }

    [Fact]
    [Unit]
    public void AShortArrowShrinksItsHeadInsteadOfOvershootingTheTail()
    {
        var shortTip = new Point(10, 0);
        var outline = Points(ArrowGeometry.ComputeArrowShape(Tail, shortTip, null, ArrowStyle.Standard, 12));
        Assert.All(outline, p => Assert.True(p.X >= -0.001 && p.X <= 10.001));
    }

    [Fact]
    [Unit]
    public void AnUnbentCurvedArrowIsTheStandardArrow()
    {
        var standard = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, null, ArrowStyle.Standard, 6));
        Point?[] bends = [null, ArrowGeometry.ArrowMidpoint(Tail, Tip)];
        foreach (var bend in bends)
        {
            var curved = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, bend, ArrowStyle.Curved, 6));
            foreach (var corner in standard)
            {
                Assert.Contains(curved, p => p.Distance(corner) < 0.001);
            }

            double tailHalf = standard[0].Y;
            Point neck = standard[1];
            foreach (var point in curved.Where(p => p.X < neck.X - 0.001))
            {
                double edge = tailHalf + (neck.Y - tailHalf) * (point.X - standard[0].X) / (neck.X - standard[0].X);
                Assert.True(Math.Abs(Math.Abs(point.Y) - edge) < 0.001);
            }
        }
    }

    [Fact]
    [Unit]
    public void ANewBendableArrowStartsStraight()
    {
        Assert.Equal(ArrowGeometry.ArrowMidpoint(Tail, Tip), ArrowGeometry.DefaultArrowBend(Tail, Tip));
    }

    [Fact]
    [Unit]
    public void CurvedHeadFollowsTheShaftsTangentAtTheTip()
    {
        static (Point, Point) Barbs(List<Point> outline)
        {
            int minIdx = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < outline.Count; i++)
            {
                double d = outline[i].Distance(Tip);
                if (d < minDist)
                {
                    minDist = d;
                    minIdx = i;
                }
            }
            return (outline[minIdx - 1], outline[minIdx + 1]);
        }

        var straight = Barbs(Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, null, ArrowStyle.Curved, 6)));
        var bent = Barbs(Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, new Point(100, -60), ArrowStyle.Curved, 6)));

        Assert.True(Math.Abs(straight.Item1.Y + straight.Item2.Y) < 0.001);
        Assert.True(Math.Abs(bent.Item1.Y + bent.Item2.Y) > 1);
        Assert.True(Math.Abs(bent.Item1.Distance(Tip) - bent.Item2.Distance(Tip)) < 0.001);
    }

    [Fact]
    [Unit]
    public void ABentCurvedArrowStillWidensFromTailToHead()
    {
        var bend = new Point(100, -60);
        var outline = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, bend, ArrowStyle.Curved, 6));

        double Width(int i) => outline[i].Distance(outline[outline.Count - 1 - i]);
        int neck = outline.Count / 2 - 2;
        Assert.True(Width(0) < Width(neck / 2));
        Assert.True(Width(neck / 2) < Width(neck));
        Assert.True(Width(neck) < Width(neck + 1));
        Assert.True(outline[neck / 2].Y < -30);
    }

    [Fact]
    [Unit]
    public void DoubleStyleCarriesTheStandardHeadAtBothEnds()
    {
        var standard = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, null, ArrowStyle.Standard, 6));
        var doubleShape = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, null, ArrowStyle.Double, 6));

        for (int i = 1; i <= 5; i++)
        {
            var corner = standard[i];
            Assert.Contains(doubleShape, p => p.Distance(corner) < 0.001);
        }

        for (int i = 1; i <= 5; i++)
        {
            var corner = standard[i];
            var mirrored = new Point(Tail.X + Tip.X - corner.X, corner.Y);
            Assert.Contains(doubleShape, p => p.Distance(mirrored) < 0.001);
        }
    }

    [Fact]
    [Unit]
    public void AShortDoubleArrowsHeadsNeverMeet()
    {
        var shortTip = new Point(10, 0);
        var outline = Points(ArrowGeometry.ComputeArrowShape(Tail, shortTip, null, ArrowStyle.Double, 12));
        Assert.All(outline, p => Assert.True(p.X >= -0.001 && p.X <= 10.001));

        var barbs = outline.Where(p => Math.Abs(p.Y) > 1.5).Select(p => p.X).ToList();
        Assert.True(barbs.Any(x => x < 5) && barbs.Any(x => x > 5));
        Assert.DoesNotContain(barbs, x => Math.Abs(x - 5) < 0.5);
    }

    [Fact]
    [Unit]
    public void ZeroLengthArrowHasNoOutline()
    {
        Assert.Empty(ArrowGeometry.ComputeArrowShape(Tail, Tail, null, ArrowStyle.Standard, 6).Path);
    }

    [Fact]
    [Unit]
    public void ScalingTheInputsScalesTheOutline()
    {
        foreach (ArrowStyle style in Enum.GetValues<ArrowStyle>())
        {
            Point? bend = style.IsBendable() ? new Point(100, -40) : null;
            var one = Points(ArrowGeometry.ComputeArrowShape(Tail, Tip, bend, style, 6));
            var two = Points(ArrowGeometry.ComputeArrowShape(
                Tail, new Point(400, 0), bend.HasValue ? new Point(bend.Value.X * 2, bend.Value.Y * 2) : null,
                style, 12
            ));

            Assert.Equal(one.Count, two.Count);
            for (int i = 0; i < one.Count; i++)
            {
                Assert.True(Math.Abs(one[i].X * 2 - two[i].X) < 0.001);
                Assert.True(Math.Abs(one[i].Y * 2 - two[i].Y) < 0.001);
            }
        }
    }

    [Fact]
    [Unit]
    public void CenterlinePassesThroughTheBend()
    {
        var bend = new Point(100, -60);
        var line = ArrowGeometry.ArrowCenterline(Tail, Tip, bend);
        Assert.Equal(Tail, line[0]);
        Assert.Equal(Tip, line[^1]);
        Assert.Contains(line, p => p.Distance(bend) < 0.001);
    }
}
