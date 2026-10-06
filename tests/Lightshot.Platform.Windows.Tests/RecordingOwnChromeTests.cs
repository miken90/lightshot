// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Capture;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.TestSupport;
using Xunit;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Tests;

public class RecordingOwnChromeTests
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref Win32Window.RECT lprc, IntPtr hbr);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

    /// <summary>
    /// Helper helper window representing an unexcluded control window.
    /// Has no WDA_EXCLUDEFROMCAPTURE flag and draws a solid bright cyan pattern.
    /// </summary>
    private sealed class ControlTestWindow : IDisposable
    {
        private static readonly System.Collections.Generic.List<Win32Window.WndProc> s_procs = new();
        private readonly Win32Window.WndProc _proc;
        private readonly string _className;
        private readonly IntPtr _brushCyan;
        public IntPtr Handle { get; }
        public Rect Bounds { get; }

        public ControlTestWindow(Rect bounds)
        {
            Bounds = bounds;
            _proc = WndProc;
            lock (s_procs) { s_procs.Add(_proc); }
            _className = $"ControlTestWindow_{Guid.NewGuid():N}";
            _brushCyan = CreateSolidBrush(0xFFFF00); // Bright cyan in BGR: (B=255, G=255, R=0)

            IntPtr hInst = Win32Window.GetModuleHandleW(null);
            var wc = new Win32Window.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Win32Window.WNDCLASSEXW>(),
                style = 3,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = hInst,
                hbrBackground = _brushCyan,
                lpszClassName = _className
            };
            Win32Window.RegisterClassExW(ref wc);

            Handle = Win32Window.CreateWindowExW(
                Win32Window.WS_EX_TOPMOST | Win32Window.WS_EX_NOACTIVATE,
                _className,
                "ControlTestWindow",
                WS_POPUP | WS_VISIBLE,
                (int)bounds.MinX, (int)bounds.MinY, (int)bounds.Width, (int)bounds.Height,
                IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

            // Explicitly ensure NO capture exclusion on control window
            Win32Window.SetCaptureExclusion(Handle, false);
            Win32Window.ShowWindow(Handle, 5 /* SW_SHOW */);
            Win32Window.UpdateWindow(Handle);
        }

        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
        {
            if (uMsg == Win32Window.WM_PAINT)
            {
                IntPtr hdc = Win32Window.BeginPaint(hWnd, out var ps);
                var rc = new Win32Window.RECT { Left = 0, Top = 0, Right = (int)Bounds.Width, Bottom = (int)Bounds.Height };
                FillRect(hdc, ref rc, _brushCyan);
                Win32Window.EndPaint(hWnd, ref ps);
                return IntPtr.Zero;
            }
            return Win32Window.DefWindowProcW(hWnd, uMsg, wParam, lParam);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                Win32Window.DestroyWindow(Handle);
            }
            DeleteObject(_brushCyan);
        }
    }

    // =========================================================================
    // Acceptance Test: RecordingOwnChromeTests.ChromeAbsentFromRecordedFrames
    // =========================================================================

    [Fact]
    [Desktop]
    public void ChromeAbsentFromRecordedFrames()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];

        // 1. Position a control window (without capture exclusion) at (100, 100, 200, 200)
        var controlRect = new Rect(primary.Bounds.MinX + 100, primary.Bounds.MinY + 100, 200, 200);
        using var controlWindow = new ControlTestWindow(controlRect);

        // 2. Position the RecordingFrameWindow with a hole at (150, 150, 300, 200)
        // Its 3 px red border will sit around (147, 147, 306, 206)
        var holeRect = new Rect(primary.Bounds.MinX + 150, primary.Bounds.MinY + 150, 300, 200);
        using var frameWindow = new RecordingFrameWindow(primary.Bounds);
        frameWindow.Show(holeRect, dimsOutside: false, isPaused: true); // steady border for deterministic pixel check

        // 3. Show CountdownWindow at the center of the display
        using var countdownWindow = new CountdownWindow(primary.Bounds);
        // Position countdown window on screen
        Win32Window.ShowWindow(countdownWindow.Handle, 5 /* SW_SHOW */);
        Win32Window.UpdateWindow(countdownWindow.Handle);

        Win32Window.PumpMessages(15);

        try
        {
            // Verify window attributes: Both chrome windows must have WDA_EXCLUDEFROMCAPTURE
            GetWindowDisplayAffinity(frameWindow.Handle, out uint frameAffinity);
            Assert.Equal(Win32Window.WDA_EXCLUDEFROMCAPTURE, frameAffinity);

            GetWindowDisplayAffinity(countdownWindow.Handle, out uint countdownAffinity);
            Assert.Equal(Win32Window.WDA_EXCLUDEFROMCAPTURE, countdownAffinity);

            // Verify both windows are click-through (WS_EX_TRANSPARENT)
            int frameEx = GetWindowLongW(frameWindow.Handle, GWL_EXSTYLE);
            Assert.True((frameEx & WS_EX_TRANSPARENT) != 0, "RecordingFrameWindow must have WS_EX_TRANSPARENT for click-through.");

            int countdownEx = GetWindowLongW(countdownWindow.Handle, GWL_EXSTYLE);
            Assert.True((countdownEx & WS_EX_TRANSPARENT) != 0, "CountdownWindow must have WS_EX_TRANSPARENT for click-through.");

            // 4. Capture display frames through DdaDisplayCapture
            var captureResult = DdaDisplayCapture.CaptureDisplay(primary);
            if (!captureResult.IsSuccess)
            {
                Assert.Fail($"Capture failed: {captureResult.Error}");
            }

            var image = captureResult.Value;
            Assert.True(image.PixelWidth > 0 && image.PixelHeight > 0);
            byte[] pixels = image.Data.ToArray();

            // 5. Assert: Control window pixels (Cyan: B=255, G=255, R=0 in BGRA) ARE captured
            int cx = (int)(controlRect.MinX + 50 - primary.Bounds.MinX);
            int cy = (int)(controlRect.MinY + 50 - primary.Bounds.MinY);
            bool controlFound = false;

            if (cx >= 0 && cx < image.PixelWidth && cy >= 0 && cy < image.PixelHeight)
            {
                int idx = (cy * image.PixelWidth + cx) * 4;
                if (idx + 3 < pixels.Length)
                {
                    byte b = pixels[idx];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];
                    // Cyan has high green and blue, low red
                    if (g > 180 && b > 180 && r < 80)
                    {
                        controlFound = true;
                    }
                }
            }

            Assert.True(controlFound, "Control window without exclusion flag must be captured in the frame.");

            // 6. Assert: Chrome pixels from RecordingFrameWindow (pure bright red: R=255, G=0, B=0)
            // are ABSENT from the recorded frame.
            // Check along the red border line: (148, 150)
            int bx = (int)(holeRect.MinX - 2 - primary.Bounds.MinX);
            int by = (int)(holeRect.MinY + 50 - primary.Bounds.MinY);
            bool redBorderCaptured = false;

            if (bx >= 0 && bx < image.PixelWidth && by >= 0 && by < image.PixelHeight)
            {
                int idx = (by * image.PixelWidth + bx) * 4;
                if (idx + 3 < pixels.Length)
                {
                    byte b = pixels[idx];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];
                    // Pure red has high red, low green and blue
                    if (r > 200 && g < 50 && b < 50)
                    {
                        redBorderCaptured = true;
                    }
                }
            }

            Assert.False(redBorderCaptured, "Red border chrome of RecordingFrameWindow must be absent from recorded frames.");

            // 7. Assert: WGC window capture of the excluded window yields protection / black
            var wgcFrameResult = WgcWindowCapture.CaptureWindow(frameWindow.Handle);
            Assert.False(wgcFrameResult.IsSuccess, "WGC window capture of excluded frame window must fail (protected/black).");

            var wgcCountdownResult = WgcWindowCapture.CaptureWindow(countdownWindow.Handle);
            Assert.False(wgcCountdownResult.IsSuccess, "WGC window capture of excluded countdown window must fail (protected/black).");
        }
        finally
        {
            frameWindow.Hide();
            countdownWindow.Hide();
            Win32Window.PumpMessages(5);
        }
    }

    // =========================================================================
    // Unit Tests for Window Properties
    // =========================================================================

    [Fact]
    [Unit]
    public void RecordingFrameWindow_PropertiesAndClickThrough()
    {
        var bounds = new Rect(0, 0, 1920, 1080);
        using var frame = new RecordingFrameWindow(bounds);

        Assert.True(IsWindow(frame.Handle));
        Assert.Equal(bounds, frame.WindowBounds);
        Assert.False(frame.IsPaused);
        Assert.Equal(1.0, frame.CurrentOpacity);

        // Pause keeps opacity steady at 1.0
        frame.SetPaused(true);
        Assert.True(frame.IsPaused);
        Assert.Equal(1.0, frame.CurrentOpacity);

        // Resume pulses
        frame.SetPaused(false);
        Assert.False(frame.IsPaused);

        // Click-through style verified
        int ex = GetWindowLongW(frame.Handle, GWL_EXSTYLE);
        Assert.True((ex & WS_EX_TRANSPARENT) != 0);

        // Capture exclusion verified
        GetWindowDisplayAffinity(frame.Handle, out uint affinity);
        Assert.Equal(Win32Window.WDA_EXCLUDEFROMCAPTURE, affinity);
    }

    [Fact]
    [Unit]
    public void CountdownWindow_PropertiesAndClickThrough()
    {
        var bounds = new Rect(0, 0, 1920, 1080);
        using var countdown = new CountdownWindow(bounds);

        Assert.True(IsWindow(countdown.Handle));
        Assert.Equal(bounds, countdown.WindowBounds);
        Assert.False(countdown.IsRunning);
        Assert.Equal(3, countdown.RemainingSeconds);

        // Click-through style verified
        int ex = GetWindowLongW(countdown.Handle, GWL_EXSTYLE);
        Assert.True((ex & WS_EX_TRANSPARENT) != 0);

        // Capture exclusion verified
        GetWindowDisplayAffinity(countdown.Handle, out uint affinity);
        Assert.Equal(Win32Window.WDA_EXCLUDEFROMCAPTURE, affinity);
    }
}
