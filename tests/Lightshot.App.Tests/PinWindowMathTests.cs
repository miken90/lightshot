// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.App.Views.Pin;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class PinWindowMathTests
{
    [Fact]
    [Unit]
    public void KeepsAspectOnResize()
    {
        double[] targetAspects = [16.0 / 9.0, 4.0 / 3.0, 1.0, 21.0 / 9.0, 9.0 / 16.0];

        foreach (var aspect in targetAspects)
        {
            // Initial rect (e.g., 320 x (320 / aspect))
            int initW = 320;
            int initH = (int)Math.Round(initW / aspect);
            int left = 100;
            int top = 100;
            int right = left + initW;
            int bottom = top + initH;

            // Test resize on all 8 edges
            foreach (SizingEdge edge in Enum.GetValues<SizingEdge>())
            {
                // Drag delta: expand or contract by 50 pixels
                int dragLeft = left - 30;
                int dragRight = right + 40;
                int dragTop = top - 25;
                int dragBottom = bottom + 35;

                var adjusted = PinWindowMath.AdjustSizingRect(edge, dragLeft, dragTop, dragRight, dragBottom, aspect);

                int resW = adjusted.Right - adjusted.Left;
                int resH = adjusted.Bottom - adjusted.Top;
                Assert.True(resW > 0, $"Result width must be positive for edge {edge}");
                Assert.True(resH > 0, $"Result height must be positive for edge {edge}");

                double actualAspect = (double)resW / resH;
                Assert.True(
                    Math.Abs(actualAspect - aspect) < 0.05,
                    $"Aspect ratio mismatch for edge {edge}: expected {aspect:F3}, got {actualAspect:F3}");
            }
        }
    }

    [Fact]
    [Unit]
    public void CalculateInitialSizeCappedToWorkArea()
    {
        // 1. Unconstrained: 400x300 on 1.0 scale with 1920x1080 work area -> 400x300
        var wa = new Rect(0, 0, 1920, 1080);
        var size1 = PinWindowMath.CalculateInitialSize(400, 300, 1.0, wa);
        Assert.Equal(400.0, size1.Width);
        Assert.Equal(300.0, size1.Height);

        // 2. High-DPI: 800x600 on 2.0 scale -> 400x300
        var sizeDpi = PinWindowMath.CalculateInitialSize(800, 600, 2.0, wa);
        Assert.Equal(400.0, sizeDpi.Width);
        Assert.Equal(300.0, sizeDpi.Height);

        // 3. Huge image: 4000x3000 on small work area (1000x1000, 90% is 900x900)
        // Aspect 4:3 -> Width should be 900, Height should be 675
        var smallWa = new Rect(0, 0, 1000, 1000);
        var sizeHuge = PinWindowMath.CalculateInitialSize(4000, 3000, 1.0, smallWa);
        Assert.True(sizeHuge.Width <= 900.0, $"Width {sizeHuge.Width} should be <= 900");
        Assert.True(sizeHuge.Height <= 900.0, $"Height {sizeHuge.Height} should be <= 900");
        Assert.Equal(4.0 / 3.0, sizeHuge.Width / sizeHuge.Height, 2);

        // 4. Minimum side of 80 DIPs
        var tiny = PinWindowMath.CalculateInitialSize(20, 10, 1.0, wa);
        Assert.True(tiny.Width >= 80.0);
        Assert.True(tiny.Height >= 80.0);
    }
}
