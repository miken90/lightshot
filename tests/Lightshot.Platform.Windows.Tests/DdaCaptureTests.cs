// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.Platform.Windows.Capture;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class DdaCaptureTests
{
    [Fact]
    [Desktop]
    public void CapturesEachMonitorAtNativeSize()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);

        foreach (var display in displays)
        {
            var result = DdaDisplayCapture.CaptureDisplay(display);
            if (!result.IsSuccess)
            {
                Assert.Fail($"Failed to capture display {display.DeviceName}: {result.Error}");
            }

            var image = result.Value;
            Assert.Equal((int)display.Bounds.Width, image.PixelWidth);
            Assert.Equal((int)display.Bounds.Height, image.PixelHeight);
            Assert.False(image.Data.IsEmpty, "Captured image data should not be empty.");
            Assert.True(image.Data.Length >= image.PixelWidth * image.PixelHeight * 4,
                $"Image data size ({image.Data.Length}) must be >= {image.PixelWidth * image.PixelHeight * 4} bytes.");
        }
    }

    [Fact]
    [Desktop]
    public void UnknownDisplayFailsLoudly()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);

        var fake = displays[0] with { DeviceName = @"\\.\DISPLAY_NOT_THERE" };
        var ex = Assert.Throws<InvalidOperationException>(() => new DdaFrameSource(fake));
        Assert.Contains(@"\\.\DISPLAY_NOT_THERE", ex.Message);
    }
}
