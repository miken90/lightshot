// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class GaussianBlurTests
{
    [Fact]
    [Render]
    public void OutputHashMatchesGolden()
    {
        // 64x64 test pattern with 4 quadrants
        int width = 64;
        int height = 64;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            using var whitePaint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(new SKRect(16, 16, 48, 48), whitePaint);
        }

        // Apply Gaussian blur with sigma 6.0
        using var blurred = GaussianBlur.Apply(bitmap, 6.0);
        Assert.NotNull(blurred);
        Assert.Equal(width, blurred.Width);
        Assert.Equal(height, blurred.Height);

        // Encode to PNG bytes and assert golden
        using var image = SKImage.FromBitmap(blurred);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var rendered = new RenderedImage(width, height, data.ToArray());

        PixelAssert.AssertOrUpdateGolden("gaussian-blur-exact", rendered, exact: true);
    }

    [Fact]
    [Render]
    public void BlurredOutputIsExactlySymmetricLeftRightAndUpDown()
    {
        using var blurred = CreateBlurredSyntheticInput(out int width, out int height, 6.0);
        var span = blurred.GetPixelSpan();

        // Exact left/right mirror symmetry: pixel(x, y) == pixel(width - 1 - x, y)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width / 2; x++)
            {
                int idx = (y * width + x) * 4;
                int mirroredXIdx = (y * width + (width - 1 - x)) * 4;

                Assert.True(
                    span[idx] == span[mirroredXIdx] &&
                    span[idx + 1] == span[mirroredXIdx + 1] &&
                    span[idx + 2] == span[mirroredXIdx + 2] &&
                    span[idx + 3] == span[mirroredXIdx + 3],
                    $"Left/right asymmetry at ({x}, {y}) vs ({width - 1 - x}, {y})");
            }
        }

        // Exact up/down mirror symmetry: pixel(x, y) == pixel(x, height - 1 - y)
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height / 2; y++)
            {
                int idx = (y * width + x) * 4;
                int mirroredYIdx = ((height - 1 - y) * width + x) * 4;

                Assert.True(
                    span[idx] == span[mirroredYIdx] &&
                    span[idx + 1] == span[mirroredYIdx + 1] &&
                    span[idx + 2] == span[mirroredYIdx + 2] &&
                    span[idx + 3] == span[mirroredYIdx + 3],
                    $"Up/down asymmetry at ({x}, {y}) vs ({x}, {height - 1 - y})");
            }
        }
    }

    [Fact]
    [Render]
    public void BlurredOutputRisesMonotonicallyTowardCentreAlongBothAxes()
    {
        using var blurred = CreateBlurredSyntheticInput(out int width, out int height, 6.0);
        var span = blurred.GetPixelSpan();

        // Monotonic rise toward center along horizontal axis (x: 0 -> 31) for each row
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width / 2 - 1; x++)
            {
                int currIdx = (y * width + x) * 4;
                int nextIdx = (y * width + (x + 1)) * 4;

                Assert.True(span[currIdx] <= span[nextIdx],
                    $"Non-monotonic rise along row {y} from x={x} ({span[currIdx]}) to x={x + 1} ({span[nextIdx]})");
            }
        }

        // Monotonic rise toward center along vertical axis (y: 0 -> 31) for each column
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height / 2 - 1; y++)
            {
                int currIdx = (y * width + x) * 4;
                int nextIdx = ((y + 1) * width + x) * 4;

                Assert.True(span[currIdx] <= span[nextIdx],
                    $"Non-monotonic rise along col {x} from y={y} ({span[currIdx]}) to y={y + 1} ({span[nextIdx]})");
            }
        }
    }

    [Fact]
    [Render]
    public void BlurredOutputMatchesAnalyticGaussianWithinTolerance()
    {
        double sigma = 6.0;
        using var blurred = CreateBlurredSyntheticInput(out int width, out int height, sigma);
        var span = blurred.GetPixelSpan();

        // Synthetic input is a 32x32 white square [16, 48] x [16, 48] on 64x64 black background.
        // The analytic 2D Gaussian convolution of an indicator rect [x1, x2] x [y1, y2]
        // is separable into 1D erf factors:
        //   F(t) = 0.5 * (erf((t - x1) / (sqrt(2) * sigma)) + erf((x2 - t) / (sqrt(2) * sigma)))
        // For pixel (x, y) with center (x + 0.5, y + 0.5):
        //   Expected(x, y) = 255.0 * F(x + 0.5) * F(y + 0.5)
        double sqrt2Sigma = Math.Sqrt(2.0) * sigma;
        double maxDeviation = 0.0;

        for (int y = 0; y < height; y++)
        {
            double yc = y + 0.5;
            double fy = 0.5 * (Erf((yc - 16.0) / sqrt2Sigma) + Erf((48.0 - yc) / sqrt2Sigma));

            for (int x = 0; x < width; x++)
            {
                double xc = x + 0.5;
                double fx = 0.5 * (Erf((xc - 16.0) / sqrt2Sigma) + Erf((48.0 - xc) / sqrt2Sigma));

                double expected = 255.0 * fx * fy;
                byte actual = span[(y * width + x) * 4];

                double deviation = Math.Abs(actual - expected);
                if (deviation > maxDeviation)
                {
                    maxDeviation = deviation;
                }

                Assert.True(deviation <= 5.0,
                    $"Deviation at ({x}, {y}): actual={actual}, expected={expected:F2}, dev={deviation:F2} exceeds 5.0");
            }
        }

        // Verify that measured maximum deviation conforms to reviewed ~4.3 of 255
        Assert.True(maxDeviation <= 5.0, $"Max deviation {maxDeviation:F2} exceeded 5.0");
    }

    private static SKBitmap CreateBlurredSyntheticInput(out int width, out int height, double sigma = 6.0)
    {
        width = 64;
        height = 64;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            using var whitePaint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(new SKRect(16, 16, 48, 48), whitePaint);
        }

        var blurred = GaussianBlur.Apply(bitmap, sigma);
        bitmap.Dispose();
        return blurred;
    }

    private static double Erf(double x)
    {
        // Abramowitz and Stegun formula 7.1.26 (maximum error 1.5e-7 in double precision)
        const double a1 =  0.254829592;
        const double a2 = -0.284496736;
        const double a3 =  1.421413741;
        const double a4 = -1.453152027;
        const double a5 =  1.061405429;
        const double p  =  0.3275911;

        int sign = x < 0 ? -1 : 1;
        double absX = Math.Abs(x);

        double t = 1.0 / (1.0 + p * absX);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-absX * absX);

        return sign * y;
    }
}
