// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using Lightshot.App.Views.Pin;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;
using Size = Lightshot.Core.Size;

namespace Lightshot.App.UiTests;

public class PinFlowTests
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

    private static CapturedImage CreateTestImage(int width = 300, int height = 200)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.CornflowerBlue);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    [Fact]
    [Desktop]
    public void CopyKeepsPinOpen()
    {
        RunInSta(() =>
        {
            // Clear clipboard to avoid stale data
            try
            {
                Clipboard.Clear();
            }
            catch
            {
                // Best effort
            }

            var image = CreateTestImage(300, 200);
            var initialSize = new Size(300, 200);
            var pin = new PinWindow(image, initialSize);

            pin.Show();
            Win32Window.PumpMessages(10);

            try
            {
                Assert.True(pin.IsVisible, "PinWindow should be visible when shown.");

                // Trigger copy action (Ctrl+S / Copy command)
                pin.CopyPin();
                Win32Window.PumpMessages(10);

                // Verify clipboard received the image
                Assert.True(Clipboard.ContainsImage(), "Clipboard should contain image after Copy on PinWindow.");

                // Verify PinWindow remains open and visible (Ctrl+S copies and the pin stays)
                Assert.True(pin.IsVisible, "PinWindow should remain open after copying.");
            }
            finally
            {
                pin.ClosePin();
                Win32Window.PumpMessages(5);
            }
        });
    }
}
