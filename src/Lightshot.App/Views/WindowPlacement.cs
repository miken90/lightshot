// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lightshot.Platform.Windows.Displays;

namespace Lightshot.App.Views;

/// <summary>
/// Opens a top-level window centred, and fully visible, in the work area of the monitor under a
/// physical point. CenterScreen sized from the primary's work area put the title bar above a
/// shorter secondary monitor.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Sizes and centres <paramref name="window"/> before it is shown. <paramref name="sizeForWorkArea"/>
    /// maps the target work area (DIP width, height) to the window's size in DIPs.
    /// </summary>
    public static void PlaceOnDisplayAt(
        Window window,
        Lightshot.Core.Point physicalPoint,
        Func<double, double, (double Width, double Height)> sizeForWorkArea)
    {
        try
        {
            var display = DisplayMath.FindDisplayAt(DisplayTopology.GetDisplays(), physicalPoint);
            if (display == null) return;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            // The created HWND's scale, not the target monitor's, maps this window's DIPs.
            new WindowInteropHelper(window).EnsureHandle();
            var frame = CalculateFrame(display.WorkArea, VisualTreeHelper.GetDpi(window).DpiScaleX, sizeForWorkArea);
            window.MinWidth = Math.Min(window.MinWidth, frame.Width);
            window.MinHeight = Math.Min(window.MinHeight, frame.Height);
            window.Width = frame.Width;
            window.Height = frame.Height;
            window.Left = frame.X;
            window.Top = frame.Y;
        }
        catch
        {
            // Topology failures keep the window's default centred placement
        }
    }

    /// <summary>
    /// The window frame, in the window's DIPs, centred in a physical work area. WPF maps a window's
    /// DIPs to device pixels with the window's own scale across the whole desktop, so the work area is
    /// divided by that scale rather than by its monitor's.
    /// </summary>
    public static Lightshot.Core.Rect CalculateFrame(
        Lightshot.Core.Rect workAreaPhysical,
        double windowScale,
        Func<double, double, (double Width, double Height)> sizeForWorkArea)
    {
        var workArea = DisplayMath.PhysicalToDip(workAreaPhysical, windowScale);
        var (width, height) = sizeForWorkArea(workArea.Width, workArea.Height);
        var centred = new Lightshot.Core.Rect(
            workArea.MinX + (workArea.Width - width) / 2,
            workArea.MinY + (workArea.Height - height) / 2,
            width,
            height);
        return DisplayMath.FitInside(centred, workArea);
    }
}
