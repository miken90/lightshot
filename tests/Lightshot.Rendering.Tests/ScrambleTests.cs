// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class ScrambleTests
{
    [Fact]
    [Render]
    public void SeedFixesOutput()
    {
        int width = 64;
        int height = 64;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var redPaint = new SKPaint { Color = SKColors.Red };
            using var bluePaint = new SKPaint { Color = SKColors.Blue };
            using var greenPaint = new SKPaint { Color = SKColors.Green };
            using var yellowPaint = new SKPaint { Color = SKColors.Yellow };

            canvas.DrawRect(new SKRect(0, 0, 32, 32), redPaint);
            canvas.DrawRect(new SKRect(32, 0, 64, 32), bluePaint);
            canvas.DrawRect(new SKRect(0, 32, 32, 64), greenPaint);
            canvas.DrawRect(new SKRect(32, 32, 64, 64), yellowPaint);
        }

        ulong seedA = 0x123456789ABCDEF0UL;
        ulong seedB = 0xFEDCBA9876543210UL;

        using var scrambledA1 = Scramble.Apply(bitmap, 8.0, seedA);
        using var scrambledA2 = Scramble.Apply(bitmap, 8.0, seedA);
        using var scrambledB = Scramble.Apply(bitmap, 8.0, seedB);

        byte[] bytesA1 = scrambledA1.Bytes;
        byte[] bytesA2 = scrambledA2.Bytes;
        byte[] bytesB = scrambledB.Bytes;

        // Same seed yields bit-for-bit identical pixels
        Assert.True(bytesA1.SequenceEqual(bytesA2));

        // Different seed yields different pixels
        Assert.False(bytesA1.SequenceEqual(bytesB));

        // SplitMix64 generator matches exact reference values (pure-math literal test)
        var sm = new SplitMix64(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, sm.Next());
        Assert.Equal(0x6E789E6AA1B965F4UL, sm.Next());
        Assert.Equal(0x06C45D188009454FUL, sm.Next());

        // SplitMix64 generator sequence is strictly deterministic
        var rng1 = new SplitMix64(seedA);
        var rng2 = new SplitMix64(seedA);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(rng1.Next(), rng2.Next());
        }
    }
}
