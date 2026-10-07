// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Tests;

/// <summary>
/// Recording on a non-primary monitor: region-to-display mapping, the recording frame following
/// the recorded display, the capture source duplicating that display's output, and the selection
/// dim drawn over a frozen still like the screenshot overlay.
/// </summary>
public class RecordingMultiMonitorTests
{
    // A mixed-DPI desk: a 150% primary, a 100% monitor to its right and one to its left.
    private static readonly DisplayInfo Primary = new(1, @"\\.\DISPLAY1", (IntPtr)1,
        new Rect(0, 0, 2560, 1600), new Rect(0, 0, 2560, 1552), 1.5, 144, 144, true, 7);
    private static readonly DisplayInfo Right = new(2, @"\\.\DISPLAY5", (IntPtr)2,
        new Rect(2560, 0, 2532, 1170), new Rect(2560, 0, 2532, 1122), 1.0, 96, 96, false, 7);
    private static readonly DisplayInfo Left = new(3, @"\\.\DISPLAY2", (IntPtr)3,
        new Rect(-1920, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1032), 1.0, 96, 96, false, 7);
    private static readonly IReadOnlyList<DisplayInfo> Desk = [Primary, Right, Left];

    [Fact]
    [Unit]
    public void RegionOnSecondaryMonitor_ResolvesToThatMonitorInLocalPixels()
    {
        var right = RecordingDisplayResolver.Resolve(Desk, new CaptureRegion.RectRegion(new Rect(3626, 435, 400, 300)));
        Assert.Equal(Right.DeviceName, right.Display.DeviceName);
        Assert.Equal(new Rect(1066, 435, 400, 300), right.LocalRect);

        var left = RecordingDisplayResolver.Resolve(Desk, new CaptureRegion.RectRegion(new Rect(-1160, 390, 400, 300)));
        Assert.Equal(Left.DeviceName, left.Display.DeviceName);
        Assert.Equal(new Rect(760, 390, 400, 300), left.LocalRect);
    }

    [Fact]
    [Unit]
    public void RegionSpanningTwoMonitors_ClampsToTheMonitorHoldingMostOfIt()
    {
        // 100 px on the primary, 300 px on the right monitor.
        var spanning = new Rect(2460, 100, 400, 300);
        var resolved = RecordingDisplayResolver.Resolve(Desk, new CaptureRegion.RectRegion(spanning));

        Assert.Equal(Right.DeviceName, resolved.Display.DeviceName);
        Assert.Equal(new Rect(2560, 100, 300, 300), resolved.GlobalRect);
        Assert.Equal(new Rect(0, 100, 300, 300), resolved.LocalRect);
    }

    [Fact]
    [Unit]
    public void FrameBounds_FollowTheRecordedDisplay()
    {
        Assert.Equal(Right.Bounds, RecordingChromeCoordinator.FrameBoundsFor(Desk, new CaptureRegion.RectRegion(new Rect(3626, 435, 400, 300))));
        Assert.Equal(Left.Bounds, RecordingChromeCoordinator.FrameBoundsFor(Desk, new CaptureRegion.WindowRegion(9, new Rect(-1800, 50, 600, 400))));
        Assert.Equal(Left.Bounds, RecordingChromeCoordinator.FrameBoundsFor(Desk, new CaptureRegion.DisplayRegion(Left.DisplayId)));
        Assert.Null(RecordingChromeCoordinator.FrameBoundsFor([], new CaptureRegion.DisplayRegion(1)));
    }

    // The app keeps one coordinator for its lifetime. A frame window made for a take on one
    // display must not be reused for a later take on another display.
    [Fact]
    [Desktop]
    public void ShowFrame_LaterTakeOnAnotherDisplay_MovesTheFrameToThatDisplay()
    {
        IReadOnlyList<DisplayInfo> desk =
        [
            Primary with { Bounds = new Rect(0, 0, 800, 600) },
            Right with { Bounds = new Rect(800, 0, 640, 480) },
        ];
        using var chrome = new RecordingChromeCoordinator(() => desk);

        chrome.ShowFrame(new CaptureRegion.RectRegion(new Rect(100, 100, 200, 150)), dimsOutside: false);
        Assert.Equal(desk[0].Bounds, chrome.FrameWindow!.WindowBounds);
        chrome.HideFrame();

        chrome.ShowFrame(new CaptureRegion.RectRegion(new Rect(900, 100, 200, 150)), dimsOutside: false);
        Assert.Equal(desk[1].Bounds, chrome.FrameWindow!.WindowBounds);
        Assert.Equal(new Rect(900, 100, 200, 150), chrome.FrameWindow.RecordedHole);
        chrome.HideFrame();
    }

    [Fact]
    [Desktop]
    public void CaptureSource_DuplicatesEachDisplaysOwnOutput()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);

        foreach (var display in displays)
        {
            using var source = new DdaFrameSource(display);
            Assert.Equal(display.DeviceName, source.OutputDeviceName, StringComparer.OrdinalIgnoreCase);
            Assert.Equal((int)display.Bounds.Width, source.Width);
            Assert.Equal((int)display.Bounds.Height, source.Height);
        }
    }

    // =========================================================================
    // Selection dim
    // =========================================================================

    [Fact]
    [Unit]
    public void RecordingSelectionDim_MatchesTheScreenshotOverlayOverTheSameStill()
    {
        const int w = 400, h = 300;
        var bounds = new Rect(0, 0, w, h);
        var selection = new Rect(150, 120, 100, 60);
        var still = Solid(w, h, 0xFF, 0xFF, 0xFF);

        var recording = PaintToPixels(w, h, hdc => RecordingSelectionPainter.Paint(hdc, w, h, bounds, selection, false, still));
        var screenshot = PaintToPixels(w, h, hdc => SelectionPainter.Paint(hdc, w, h, bounds, selection, false, still));

        Assert.Equal((byte)110, RecordingSelectionPainter.DimAlpha);
        // Outside the selection, away from the screenshot overlay's size badge, the still
        // shows through the same ~43% tint in both overlays.
        var dimmed = Pixel(recording, w, w - 3, h - 3);
        Assert.Equal(Pixel(screenshot, w, w - 3, h - 3), dimmed);
        Assert.InRange(dimmed.G, 140, 160); // 255 * (1 - 110/255) + 0x10 * 110/255 = 152
        // Inside the selection the still is untouched.
        Assert.Equal((byte)0xFF, Pixel(recording, w, 200, 150).G);
    }

    [Fact]
    [Desktop]
    public void RecordingSelection_PaintsEveryMonitorOverItsFrozenStill()
    {
        var frozen = FrozenGrey(OverlayHost.DiscoverMonitors());
        using var chrome = new RecordingChromeCoordinator();

        _ = chrome.ShowSelectionAsync(null, new RecordingDefaults(), frozen, TestContext.Current.CancellationToken);

        Assert.Equal(frozen.Displays.Count, chrome.SelectionWindows.Count);
        Assert.All(chrome.SelectionWindows, win => Assert.NotNull(win.Backdrop));
    }

    [Fact]
    [Desktop]
    public async Task SelectRecording_FreezesTheScreenBeforeShowingTheOverlay()
    {
        var frozen = FrozenGrey(OverlayHost.DiscoverMonitors());
        int freezes = 0;
        using var controller = new WindowsOverlayController(freezeForRecording: () =>
        {
            Interlocked.Increment(ref freezes);
            return Task.FromResult<FrozenScreen?>(frozen);
        });

        var pick = controller.SelectRecordingAsync(null, new RecordingDefaults());

        // Cancel through this process's own overlay windows; never send global input.
        bool cancelled = false;
        for (int i = 0; i < 50 && !cancelled; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            cancelled = PostEscapeToOwnOverlays();
        }

        Assert.True(cancelled, "No recording selection overlay appeared.");
        Assert.Null(await pick.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(1, freezes);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static FrozenScreen FrozenGrey(IReadOnlyList<OverlayHost.MonitorInfo> monitors)
    {
        Assert.NotEmpty(monitors);
        return new FrozenScreen(monitors.Select(m => new FrozenDisplay(m.Id, m.Bounds,
            Solid((int)m.Bounds.Width, (int)m.Bounds.Height, 0x80, 0x80, 0x80))));
    }

    private static bool PostEscapeToOwnOverlays()
    {
        uint pid = (uint)Environment.ProcessId;
        bool posted = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == pid && IsWindowVisible(hwnd))
            {
                var title = new System.Text.StringBuilder(64);
                GetWindowTextW(hwnd, title, title.Capacity);
                if (title.ToString() == "LightshotOverlay")
                {
                    posted |= PostMessageW(hwnd, Win32Window.WM_KEYDOWN, (IntPtr)0x1B, IntPtr.Zero);
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return posted;
    }

    private static CapturedImage Solid(int w, int h, byte b, byte g, byte r)
    {
        var data = new byte[w * h * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = b;
            data[i + 1] = g;
            data[i + 2] = r;
            data[i + 3] = 0xFF;
        }
        return new CapturedImage(w, h, data);
    }

    private static (byte B, byte G, byte R) Pixel(byte[] bgra, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (bgra[i], bgra[i + 1], bgra[i + 2]);
    }

    private static byte[] PaintToPixels(int w, int h, Action<IntPtr> paint)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr dib = CreateDIBSection(dc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(dc, dib);
        try
        {
            paint(dc);
            GdiFlush();
            var pixels = new byte[w * h * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(dib);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
