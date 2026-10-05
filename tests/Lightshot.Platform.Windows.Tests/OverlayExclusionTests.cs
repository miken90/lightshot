// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Interop;
using Lightshot.App.Views.QuickAccess;
using Lightshot.Core;
using Lightshot.Platform.Windows.Capture;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;
using Size = Lightshot.Core.Size;

namespace Lightshot.Platform.Windows.Tests;

public class OverlayExclusionTests
{
    private static void RunInSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    private static CapturedImage CreateTestImage(int width = 200, int height = 120)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

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

        // 4. Phase 5 extension: Verify Quick Access CardWindow is also excluded from capture (WDA_EXCLUDEFROMCAPTURE)
        RunInSta(() =>
        {
            var image = CreateTestImage(240, 160);
            var id = Guid.NewGuid();
            var cardVm = new CardViewModel(id, image);
            var cardWindow = new CardWindow(id, image, cardVm);
            cardWindow.Show();
            Win32Window.PumpMessages(10);

            var cardHwnd = new WindowInteropHelper(cardWindow).Handle;
            Assert.NotEqual(IntPtr.Zero, cardHwnd);

            try
            {
                // DDA capture of primary display succeeds while card window is visible
                var ddaResultCard = DdaDisplayCapture.CaptureDisplay(primary);
                if (!ddaResultCard.IsSuccess)
                {
                    Assert.Fail($"DDA capture failed with card window: {ddaResultCard.Error}");
                }

                // WGC window capture on card window handle fails because WDA_EXCLUDEFROMCAPTURE yields black frame
                var cardWgcResult = WgcWindowCapture.CaptureWindow(cardHwnd);
                Assert.False(cardWgcResult.IsSuccess, "WGC window capture of an excluded CardWindow should fail (detected as black/protected frame).");
            }
            finally
            {
                cardWindow.Close();
                Win32Window.PumpMessages(5);
            }
        });
    }
}
