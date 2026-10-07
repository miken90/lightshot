// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Interop;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Recording;

namespace Lightshot.App.Views;

public enum PlacementAnchor
{
    TopCenter,
    BottomCenter,
    BottomRight,
    Center
}

/// <summary>
/// Opens a top-level window centred, and fully visible, in the work area of the monitor under a
/// physical point. CenterScreen sized from the primary's work area put the title bar above a
/// shorter secondary monitor.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// Moves <paramref name="window"/>'s HWND (created if needed, shown or not) onto the monitor that holds
    /// <paramref name="physicalWorkArea"/> and returns the device pixels per DIP WPF then applies to its
    /// Left/Top/Width/Height. A per-monitor aware WPF window takes the scale of the monitor it is on, so
    /// DIP placement for another monitor is only right once the HWND is there.
    /// </summary>
    public static double MoveOntoDisplay(Window window, Lightshot.Core.Rect physicalWorkArea)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (Win32Window.GetWindowRect(hwnd, out var rect))
        {
            // Centred on the work area, so the monitor that holds most of the window is the target.
            var center = physicalWorkArea.Center;
            Win32Window.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                (int)Math.Round(center.X - rect.Width / 2.0),
                (int)Math.Round(center.Y - rect.Height / 2.0),
                0,
                0,
                Win32Window.SWP_NOSIZE | Win32Window.SWP_NOZORDER | Win32Window.SWP_NOACTIVATE);
        }
        // A hidden window's visual tree keeps its old scale until it is shown, but WPF already maps
        // Left/Top/Width/Height with the DPI of the monitor the HWND is now on.
        return Dpi.ScaleFactorFromDpi(Dpi.GetWindowDpi(hwnd));
    }

    /// <summary>
    /// A size function for <see cref="PlaceOnDisplayAt"/>: the designed size, capped so a small
    /// secondary monitor still shows the whole window with a margin.
    /// </summary>
    public static Func<double, double, (double Width, double Height)> CappedSize(double width, double height) =>
        (workAreaWidth, workAreaHeight) => (Math.Min(width, workAreaWidth * 0.9), Math.Min(height, workAreaHeight * 0.9));

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
            var frame = CalculateFrame(display.WorkArea, MoveOntoDisplay(window, display.WorkArea), sizeForWorkArea);
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
    /// Anchors an already-sized <paramref name="window"/> to an edge of the work area of the monitor that
    /// holds <paramref name="region"/> (the monitor the recording engine will capture), never the primary's.
    /// Call after the window has its size (shown or measured).
    /// </summary>
    public static void PlaceAnchoredOnRecordedDisplay(Window window, CaptureRegion region, PlacementAnchor anchor, double marginDip)
    {
        try
        {
            var workArea = RecordingDisplayResolver.Resolve(DisplayTopology.GetDisplays(), region).Display.WorkArea;
            var frame = CalculateAnchoredFrame(
                workArea, MoveOntoDisplay(window, workArea), window.ActualWidth, window.ActualHeight, anchor, marginDip);
            window.Left = frame.X;
            window.Top = frame.Y;
        }
        catch
        {
            // Topology failures keep the window where it is
        }
    }

    /// <summary>
    /// The frame, in the window's DIPs, of a window of the given DIP size anchored inside a physical work
    /// area with a DIP margin to the anchored edges; always fully inside the work area.
    /// </summary>
    public static Lightshot.Core.Rect CalculateAnchoredFrame(
        Lightshot.Core.Rect workAreaPhysical,
        double windowScale,
        double width,
        double height,
        PlacementAnchor anchor,
        double marginDip)
    {
        var workArea = DisplayMath.PhysicalToDip(workAreaPhysical, windowScale);
        double x = anchor == PlacementAnchor.BottomRight
            ? workArea.MaxX - width - marginDip
            : workArea.MinX + (workArea.Width - width) / 2;
        double y = anchor switch
        {
            PlacementAnchor.TopCenter => workArea.MinY + marginDip,
            PlacementAnchor.Center => workArea.MinY + (workArea.Height - height) / 2,
            _ => workArea.MaxY - height - marginDip
        };
        return DisplayMath.FitInside(new Lightshot.Core.Rect(x, y, width, height), workArea);
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
