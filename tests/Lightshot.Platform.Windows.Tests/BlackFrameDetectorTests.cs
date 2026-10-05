// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.Platform.Windows.Capture;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class BlackFrameDetectorTests
{
    [Fact]
    [Unit]
    public void FlagsAllBlackFrame()
    {
        // 1. All zero bytes (10x10 BGRA)
        byte[] allZeros = new byte[10 * 10 * 4];
        var blackImage = new CapturedImage(10, 10, allZeros);
        Assert.True(BlackFrameDetector.IsBlackFrame(blackImage), "All-zero frame should be flagged as black.");

        // 2. All black with opaque alpha (B=0, G=0, R=0, A=255)
        byte[] blackWithAlpha = new byte[10 * 10 * 4];
        for (int i = 0; i < blackWithAlpha.Length; i += 4)
        {
            blackWithAlpha[i] = 0;       // B
            blackWithAlpha[i + 1] = 0;   // G
            blackWithAlpha[i + 2] = 0;   // R
            blackWithAlpha[i + 3] = 255; // A
        }
        var blackOpaque = new CapturedImage(10, 10, blackWithAlpha);
        Assert.True(BlackFrameDetector.IsBlackFrame(blackOpaque), "Black frame with alpha 255 should still be flagged as black.");

        // 3. One non-black pixel at the beginning
        byte[] firstPixelNonBlack = (byte[])blackWithAlpha.Clone();
        firstPixelNonBlack[0] = 1; // B = 1
        Assert.False(BlackFrameDetector.IsBlackFrame(new CapturedImage(10, 10, firstPixelNonBlack)), "Frame with non-black first pixel should not be flagged.");

        // 4. One non-black pixel at the end
        byte[] lastPixelNonBlack = (byte[])blackWithAlpha.Clone();
        lastPixelNonBlack[^2] = 1; // R = 1
        Assert.False(BlackFrameDetector.IsBlackFrame(new CapturedImage(10, 10, lastPixelNonBlack)), "Frame with non-black last pixel should not be flagged.");

        // 5. Empty image
        var emptyImage = new CapturedImage(0, 0, Array.Empty<byte>());
        Assert.False(BlackFrameDetector.IsBlackFrame(emptyImage), "Empty image should return false.");
    }
}
