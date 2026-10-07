// MIT License, Copyright (c) 2026 Viet Le

using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class LoupeGeometryTests
{
    private static readonly Rect Monitor = new(0, 0, 1920, 1080);
    private static readonly Size Loupe = new(200, 260);

    [Fact]
    public void LoupeOffsetsBottomRightByDefault()
    {
        var layout = LoupeGeometry.Calculate(new Point(500, 400), Monitor, Loupe);

        Assert.Equal(new Rect(524, 424, 200, 260), layout.Bounds);
        Assert.False(layout.FlippedX);
        Assert.False(layout.FlippedY);
    }

    [Fact]
    public void LoupeFlipsLeftNearRightScreenEdge()
    {
        var layout = LoupeGeometry.Calculate(new Point(1800, 400), Monitor, Loupe);

        Assert.True(layout.FlippedX);
        Assert.False(layout.FlippedY);
        Assert.Equal(1800 - 24 - 200, layout.Bounds.X);
        Assert.Equal(424, layout.Bounds.Y);
    }

    [Fact]
    public void LoupeFlipsAboveNearBottomScreenEdge()
    {
        var layout = LoupeGeometry.Calculate(new Point(500, 1000), Monitor, Loupe);

        Assert.False(layout.FlippedX);
        Assert.True(layout.FlippedY);
        Assert.Equal(524, layout.Bounds.X);
        Assert.Equal(1000 - 24 - 260, layout.Bounds.Y);
    }

    [Fact]
    public void LoupeClampsToMonitorBoundsInCorner()
    {
        var secondary = new Rect(1920, 0, 1280, 720);
        var corners = new[]
        {
            new Point(1920, 0), new Point(3199, 0), new Point(1920, 719), new Point(3199, 719),
            new Point(1930, 10), new Point(3190, 710),
        };

        foreach (var pointer in corners)
        {
            var b = LoupeGeometry.Calculate(pointer, secondary, Loupe).Bounds;
            Assert.True(b.MinX >= secondary.MinX && b.MaxX <= secondary.MaxX, $"x out of bounds for {pointer}");
            Assert.True(b.MinY >= secondary.MinY && b.MaxY <= secondary.MaxY, $"y out of bounds for {pointer}");
        }

        // Pointer in the bottom-right corner flips on both axes.
        var corner = LoupeGeometry.Calculate(new Point(3199, 719), secondary, Loupe);
        Assert.True(corner.FlippedX);
        Assert.True(corner.FlippedY);
    }
}
