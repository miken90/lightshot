// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
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
    /// Anchors an already-sized <paramref name="window"/> to an edge of the work area of the monitor that
    /// holds <paramref name="region"/> (the monitor the recording engine will capture), never the primary's.
    /// Call after the window has its size (shown or measured).
    /// </summary>
    public static void PlaceAnchoredOnRecordedDisplay(Window window, CaptureRegion region, PlacementAnchor anchor, double marginDip)
    {
        try
        {
            var workArea = RecordingDisplayResolver.Resolve(DisplayTopology.GetDisplays(), region).Display.WorkArea;
            new WindowInteropHelper(window).EnsureHandle();
            var frame = CalculateAnchoredFrame(
                workArea, VisualTreeHelper.GetDpi(window).DpiScaleX, window.ActualWidth, window.ActualHeight, anchor, marginDip);
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
