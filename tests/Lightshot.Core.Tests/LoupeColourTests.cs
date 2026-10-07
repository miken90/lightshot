// MIT License, Copyright (c) 2026 Viet Le

using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class LoupeColourTests
{
    [Fact]
    public void SampledPixelFormatsToUppercaseHex()
    {
        // 2x1 BGRA image: (x=0) R=0xAB G=0xCD B=0xEF, (x=1) white.
        var image = new CapturedImage(2, 1, new byte[] { 0xEF, 0xCD, 0xAB, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        Assert.Equal("#ABCDEF", LoupeColour.SampleHex(image, 0, 0));
        Assert.Equal("#FFFFFF", LoupeColour.SampleHex(image, 1, 0));
        Assert.Null(LoupeColour.SampleHex(image, 2, 0));
        Assert.Null(LoupeColour.SampleHex(image, 0, -1));
    }
}
