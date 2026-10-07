// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Xunit;

namespace Lightshot.App.UiTests;

/// <summary>
/// A per-monitor-aware WPF window maps its DIPs to device pixels with the scale of the monitor it is on,
/// so a 100% secondary shows it at its DIP size and a 150% primary at 1.5 times that.
/// </summary>
internal static class MonitorScaleAssert
{
    private const double TolerancePx = 2.0;

    public static void RendersAtScaleOf(Window window, DisplayInfo display, string what) =>
        RendersAtScaleOf(window, display, window.ActualWidth, window.ActualHeight, what);

    /// <summary>
    /// The window is on the display's scale and its device-pixel size is the designed DIP size times that scale.
    /// </summary>
    public static void RendersAtScaleOf(Window window, DisplayInfo display, double dipWidth, double dipHeight, string what)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Assert.True(Win32Window.GetWindowRect(hwnd, out var rect), $"{what}: no window rect.");
        var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        Assert.True(Math.Abs(scale - display.ScaleFactor) < 0.001,
            $"{what} renders at scale {scale}, expected {display.ScaleFactor} of {display.DeviceName}.");

        double expectedWidth = dipWidth * display.ScaleFactor;
        double expectedHeight = dipHeight * display.ScaleFactor;
        Assert.True(
            Math.Abs(rect.Width - expectedWidth) <= TolerancePx && Math.Abs(rect.Height - expectedHeight) <= TolerancePx,
            $"{what} is {rect.Width}x{rect.Height} px, expected {dipWidth:0.#}x{dipHeight:0.#} DIP x {display.ScaleFactor} = {expectedWidth:0.#}x{expectedHeight:0.#} px.");
    }
}
