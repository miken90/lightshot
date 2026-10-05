// MIT License, Copyright (c) 2026 Viet Le

using System.IO;
using Lightshot.Core;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

public class GifWriterTests
{
    [Fact]
    [Render]
    public void RoundTripsFramesAndDelays()
    {
        int width = 20;
        int height = 20;

        // Frame 1: all red
        byte[] redRgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            redRgba[i * 4] = 255;
            redRgba[i * 4 + 3] = 255;
        }
        var frame1 = GifQuantizer.Quantize(redRgba, width, height, isBgra: false, maxColors: 256);

        // Frame 2: all blue
        byte[] blueRgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            blueRgba[i * 4 + 2] = 255;
            blueRgba[i * 4 + 3] = 255;
        }
        var frame2 = GifQuantizer.Quantize(blueRgba, width, height, isBgra: false, maxColors: 256);

        byte[] gifBytes;
        using (var ms = new MemoryStream())
        {
            using (var writer = new GifWriter(ms, width, height, loopCount: 0, leaveOpen: true))
            {
                writer.WriteFrame(frame1, delayCentiseconds: 10); // 100ms
                writer.WriteFrame(frame2, delayCentiseconds: 20); // 200ms
            }
            gifBytes = ms.ToArray();
        }

        Assert.True(gifBytes.Length > 0);

        // Decode with SkiaSharp's SKCodec
        using var stream = new SKMemoryStream(gifBytes);
        using var codec = SKCodec.Create(stream);
        Assert.NotNull(codec);
        Assert.Equal(width, codec.Info.Width);
        Assert.Equal(height, codec.Info.Height);
        Assert.Equal(2, codec.FrameCount);

        var frameInfo = codec.FrameInfo;
        Assert.Equal(2, frameInfo.Length);
        Assert.Equal(100, frameInfo[0].Duration);
        Assert.Equal(200, frameInfo[1].Duration);
    }
}
