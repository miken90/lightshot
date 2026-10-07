// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class CropConstraintTests
{
    [Fact]
    [Unit]
    public void FitInsideCentresLargestRect()
    {
        // 1. Exact match container
        var c1 = new Rect(0, 0, 1920, 1080);
        var r1 = CropConstraint.FitInside(c1, 16.0 / 9.0);
        Assert.Equal(0, r1.MinX, 3);
        Assert.Equal(0, r1.MinY, 3);
        Assert.Equal(1920, r1.Width, 3);
        Assert.Equal(1080, r1.Height, 3);

        // 2. Square container, 16:9 ratio -> width constrained, centered vertically
        var c2 = new Rect(0, 0, 1000, 1000);
        var r2 = CropConstraint.FitInside(c2, 16.0 / 9.0);
        Assert.Equal(1000, r2.Width, 3);
        Assert.Equal(1000.0 / (16.0 / 9.0), r2.Height, 3);
        Assert.Equal(0, r2.MinX, 3);
        Assert.Equal((1000 - r2.Height) / 2.0, r2.MinY, 3);
        Assert.Equal(c2.MidX, r2.MidX, 3);
        Assert.Equal(c2.MidY, r2.MidY, 3);

        // 3. Wide container, 1:1 ratio -> height constrained, centered horizontally
        var c3 = new Rect(100, 50, 1600, 900);
        var r3 = CropConstraint.FitInside(c3, 1.0);
        Assert.Equal(900, r3.Width, 3);
        Assert.Equal(900, r3.Height, 3);
        Assert.Equal(100 + (1600 - 900) / 2.0, r3.MinX, 3);
        Assert.Equal(50, r3.MinY, 3);
        Assert.Equal(c3.MidX, r3.MidX, 3);
        Assert.Equal(c3.MidY, r3.MidY, 3);

        // 4. Tall ratio (9:16) in 800x600 -> height constrained
        var c4 = new Rect(0, 0, 800, 600);
        var r4 = CropConstraint.FitInside(c4, 9.0 / 16.0);
        Assert.Equal(600, r4.Height, 3);
        Assert.Equal(600 * (9.0 / 16.0), r4.Width, 3);
        Assert.Equal(c4.MidX, r4.MidX, 3);
        Assert.Equal(c4.MidY, r4.MidY, 3);

        // 5. Edge cases: invalid ratio or empty container returns container
        var rInvalidRatio = CropConstraint.FitInside(c1, 0);
        Assert.Equal(c1, rInvalidRatio);

        var rEmpty = CropConstraint.FitInside(new Rect(10, 10, 0, 0), 16.0 / 9.0);
        Assert.Equal(0, rEmpty.Width);
        Assert.Equal(0, rEmpty.Height);
    }

    [Fact]
    [Unit]
    public void ConstrainPreservesAspectAndClampsToBounds()
    {
        var bounds = new Rect(0, 0, 1000, 1000);
        var original = new Rect(200, 200, 400, 300);
        double ratio = 16.0 / 9.0;

        // 1. BottomRight drag within bounds
        var r1 = CropConstraint.Constrain(original, Handle.BottomRight, new Point(800, 600), ratio, bounds);
        Assert.Equal(original.MinX, r1.MinX, 3);
        Assert.Equal(original.MinY, r1.MinY, 3);
        Assert.Equal(ratio, r1.Width / r1.Height, 3);
        AssertBoundsContained(r1, bounds);

        // 2. BottomRight drag exceeding bounds (current outside to the right and bottom)
        var r2 = CropConstraint.Constrain(original, Handle.BottomRight, new Point(1500, 1500), ratio, bounds);
        Assert.Equal(original.MinX, r2.MinX, 3);
        Assert.Equal(original.MinY, r2.MinY, 3);
        Assert.Equal(ratio, r2.Width / r2.Height, 3);
        AssertBoundsContained(r2, bounds);

        // 3. TopLeft drag
        var r3 = CropConstraint.Constrain(original, Handle.TopLeft, new Point(50, 100), ratio, bounds);
        Assert.Equal(original.MaxX, r3.MaxX, 3);
        Assert.Equal(original.MaxY, r3.MaxY, 3);
        Assert.Equal(ratio, r3.Width / r3.Height, 3);
        AssertBoundsContained(r3, bounds);

        // 4. TopLeft drag exceeding bounds (current negative)
        var r4 = CropConstraint.Constrain(original, Handle.TopLeft, new Point(-200, -200), ratio, bounds);
        Assert.Equal(original.MaxX, r4.MaxX, 3);
        Assert.Equal(original.MaxY, r4.MaxY, 3);
        Assert.Equal(ratio, r4.Width / r4.Height, 3);
        AssertBoundsContained(r4, bounds);

        // 5. TopRight drag
        var r5 = CropConstraint.Constrain(original, Handle.TopRight, new Point(900, 50), ratio, bounds);
        Assert.Equal(original.MinX, r5.MinX, 3);
        Assert.Equal(original.MaxY, r5.MaxY, 3);
        Assert.Equal(ratio, r5.Width / r5.Height, 3);
        AssertBoundsContained(r5, bounds);

        // 6. BottomLeft drag
        var r6 = CropConstraint.Constrain(original, Handle.BottomLeft, new Point(50, 800), ratio, bounds);
        Assert.Equal(original.MaxX, r6.MaxX, 3);
        Assert.Equal(original.MinY, r6.MinY, 3);
        Assert.Equal(ratio, r6.Width / r6.Height, 3);
        AssertBoundsContained(r6, bounds);

        // 7. Right edge handle
        var r7 = CropConstraint.Constrain(original, Handle.Right, new Point(900, original.MidY), ratio, bounds);
        Assert.Equal(original.MinX, r7.MinX, 3);
        Assert.Equal(ratio, r7.Width / r7.Height, 3);
        AssertBoundsContained(r7, bounds);

        // 8. Left edge handle
        var r8 = CropConstraint.Constrain(original, Handle.Left, new Point(50, original.MidY), ratio, bounds);
        Assert.Equal(original.MaxX, r8.MaxX, 3);
        Assert.Equal(ratio, r8.Width / r8.Height, 3);
        AssertBoundsContained(r8, bounds);

        // 9. Bottom edge handle
        var r9 = CropConstraint.Constrain(original, Handle.Bottom, new Point(original.MidX, 900), ratio, bounds);
        Assert.Equal(original.MinY, r9.MinY, 3);
        Assert.Equal(ratio, r9.Width / r9.Height, 3);
        AssertBoundsContained(r9, bounds);

        // 10. Top edge handle
        var r10 = CropConstraint.Constrain(original, Handle.Top, new Point(original.MidX, 50), ratio, bounds);
        Assert.Equal(original.MaxY, r10.MaxY, 3);
        Assert.Equal(ratio, r10.Width / r10.Height, 3);
        AssertBoundsContained(r10, bounds);
    }

    private static void AssertBoundsContained(Rect r, Rect bounds)
    {
        Assert.True(r.MinX >= bounds.MinX - 1e-6, $"MinX {r.MinX} < bounds.MinX {bounds.MinX}");
        Assert.True(r.MaxX <= bounds.MaxX + 1e-6, $"MaxX {r.MaxX} > bounds.MaxX {bounds.MaxX}");
        Assert.True(r.MinY >= bounds.MinY - 1e-6, $"MinY {r.MinY} < bounds.MinY {bounds.MinY}");
        Assert.True(r.MaxY <= bounds.MaxY + 1e-6, $"MaxY {r.MaxY} > bounds.MaxY {bounds.MaxY}");
    }
}
