// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Displays;

/// <summary>
/// Pure math operations for virtual screen layouts, mixed DPI coordinate mapping,
/// and display intersection / overlap calculations.
/// </summary>
public static class DisplayMath
{
    /// <summary>
    /// Finds the display that has the largest overlapping area with the given physical-pixel rect.
    /// Returns null if displays collection is empty.
    /// If no display has a positive overlap area, returns the display closest to rect center.
    /// </summary>
    public static DisplayInfo? FindLargestOverlap(IEnumerable<DisplayInfo> displays, Rect rect)
    {
        var list = displays as IReadOnlyList<DisplayInfo> ?? displays.ToList();
        if (list.Count == 0) return null;

        var standardized = rect.Standardized;
        DisplayInfo? bestDisplay = null;
        double maxArea = 0;

        foreach (var d in list)
        {
            var intersection = standardized.Intersection(d.Bounds.Standardized);
            if (intersection.HasValue)
            {
                double area = intersection.Value.Width * intersection.Value.Height;
                if (area > maxArea)
                {
                    maxArea = area;
                    bestDisplay = d;
                }
            }
        }

        if (bestDisplay != null && maxArea > 0)
        {
            return bestDisplay;
        }

        // Fallback: pick display closest to rect center
        var center = standardized.Center;
        return list.OrderBy(d => center.Distance(d.Bounds.Center)).FirstOrDefault();
    }

    /// <summary>
    /// Scales a rect from DIPs (device-independent pixels at 96 DPI) to physical pixels using scaleFactor.
    /// </summary>
    public static Rect DipToPhysical(Rect dipRect, double scaleFactor)
    {
        if (scaleFactor <= 0) scaleFactor = 1.0;
        return new Rect(
            dipRect.X * scaleFactor,
            dipRect.Y * scaleFactor,
            dipRect.Width * scaleFactor,
            dipRect.Height * scaleFactor
        );
    }

    /// <summary>
    /// Scales a rect from physical pixels to DIPs using scaleFactor.
    /// </summary>
    public static Rect PhysicalToDip(Rect physicalRect, double scaleFactor)
    {
        if (scaleFactor <= 0) scaleFactor = 1.0;
        return new Rect(
            physicalRect.X / scaleFactor,
            physicalRect.Y / scaleFactor,
            physicalRect.Width / scaleFactor,
            physicalRect.Height / scaleFactor
        );
    }

    /// <summary>
    /// Scales a point from DIPs to physical pixels using scaleFactor.
    /// </summary>
    public static Point DipToPhysical(Point dipPoint, double scaleFactor)
    {
        if (scaleFactor <= 0) scaleFactor = 1.0;
        return new Point(dipPoint.X * scaleFactor, dipPoint.Y * scaleFactor);
    }

    /// <summary>
    /// Scales a point from physical pixels to DIPs using scaleFactor.
    /// </summary>
    public static Point PhysicalToDip(Point physicalPoint, double scaleFactor)
    {
        if (scaleFactor <= 0) scaleFactor = 1.0;
        return new Point(physicalPoint.X / scaleFactor, physicalPoint.Y / scaleFactor);
    }

    /// <summary>
    /// Maps a rect given in DIP coordinates to global physical pixels across a mixed-DPI display topology.
    /// Determines the display having the largest DIP overlap and maps relative to its physical origin.
    /// </summary>
    public static Rect MapDipRectToPhysical(Rect dipRect, IEnumerable<DisplayInfo> displays)
    {
        var list = displays as IReadOnlyList<DisplayInfo> ?? displays.ToList();
        if (list.Count == 0) return dipRect;

        // Calculate each display's DIP bounds in the virtual DIP layout
        DisplayInfo? bestDisplay = null;
        Rect bestDipBounds = Rect.Zero;
        double maxOverlap = 0;

        foreach (var d in list)
        {
            var dipBounds = PhysicalToDip(d.Bounds, d.ScaleFactor);
            var intersection = dipRect.Standardized.Intersection(dipBounds.Standardized);
            if (intersection.HasValue)
            {
                double area = intersection.Value.Width * intersection.Value.Height;
                if (area > maxOverlap)
                {
                    maxOverlap = area;
                    bestDisplay = d;
                    bestDipBounds = dipBounds;
                }
            }
        }

        if (bestDisplay == null)
        {
            bestDisplay = list.FirstOrDefault(d => d.IsPrimary) ?? list[0];
            bestDipBounds = PhysicalToDip(bestDisplay.Bounds, bestDisplay.ScaleFactor);
        }

        double scale = bestDisplay.ScaleFactor;
        double pxX = bestDisplay.Bounds.X + (dipRect.X - bestDipBounds.X) * scale;
        double pxY = bestDisplay.Bounds.Y + (dipRect.Y - bestDipBounds.Y) * scale;
        double pxW = dipRect.Width * scale;
        double pxH = dipRect.Height * scale;

        return new Rect(pxX, pxY, pxW, pxH);
    }

    /// <summary>
    /// Computes the bounding box of all displays in global physical pixel coordinates.
    /// </summary>
    public static Rect GetVirtualScreenBounds(IEnumerable<DisplayInfo> displays)
    {
        var list = displays as IReadOnlyList<DisplayInfo> ?? displays.ToList();
        if (list.Count == 0) return Rect.Zero;

        double minX = list.Min(d => d.Bounds.MinX);
        double minY = list.Min(d => d.Bounds.MinY);
        double maxX = list.Max(d => d.Bounds.MaxX);
        double maxY = list.Max(d => d.Bounds.MaxY);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Clamps a rect so that it stays within the virtual screen bounds.
    /// </summary>
    public static Rect ClampToVirtualScreen(Rect rect, IEnumerable<DisplayInfo> displays)
    {
        var vScreen = GetVirtualScreenBounds(displays);
        if (vScreen.Width <= 0 || vScreen.Height <= 0) return rect;

        double x = Math.Clamp(rect.X, vScreen.MinX, vScreen.MaxX);
        double y = Math.Clamp(rect.Y, vScreen.MinY, vScreen.MaxY);
        double w = Math.Min(rect.Width, vScreen.MaxX - x);
        double h = Math.Min(rect.Height, vScreen.MaxY - y);

        return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }
}
