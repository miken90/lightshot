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

        PixelAssert.AssertOrUpdateGolden("gaussian-blur-exact", rendered);
    }
}
