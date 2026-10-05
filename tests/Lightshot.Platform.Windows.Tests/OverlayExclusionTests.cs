// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using Lightshot.Core;
using Lightshot.Platform.Windows.Capture;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class OverlayExclusionTests
{
    [Fact]
    [Desktop]
    public void ExcludedWindowAbsentFromDdaAndWgc()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];

        // 1. Create and show an OverlayWindow covering the primary monitor.
        // OverlayWindow constructor automatically applies Win32Window.SetCaptureExclusion(Handle, true).
        using var overlay = new OverlayWindow(primary.DisplayId, primary.Bounds);
        overlay.Show();
        Win32Window.PumpMessages(10);

        try
        {
            // 2. Verify DDA capture succeeds and captures the display beneath the excluded overlay
            var ddaResult = DdaDisplayCapture.CaptureDisplay(primary);
            if (!ddaResult.IsSuccess)
            {
                Assert.Fail($"DDA capture failed: {ddaResult.Error}");
            }
            var ddaImage = ddaResult.Value;
            Assert.Equal((int)primary.Bounds.Width, ddaImage.PixelWidth);
            Assert.Equal((int)primary.Bounds.Height, ddaImage.PixelHeight);

            // 3. Verify WGC window capture on the excluded window handle fails because
            // WDA_EXCLUDEFROMCAPTURE causes WGC to yield an all-black frame, which BlackFrameDetector flags.
            var wgcResult = WgcWindowCapture.CaptureWindow(overlay.Handle);
            Assert.False(wgcResult.IsSuccess, "WGC window capture of an excluded window should fail (detected as black/protected frame).");
        }
        finally
        {
            overlay.Hide();
            Win32Window.PumpMessages(5);
        }
    }
}
