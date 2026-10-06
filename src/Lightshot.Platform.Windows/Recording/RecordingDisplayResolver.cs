// Ported from LightshotKit/Sources/LightshotKit/RecordingCoordinator.swift & APP §3
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Resolved recording target display and coordinate bounds.
/// </summary>
public record ResolvedRecordingDisplay(
    DisplayInfo Display,
    Rect GlobalRect,
    Rect LocalRect,
    int OutputWidth,
    int OutputHeight
);

/// <summary>
/// Resolves a recording region (rectangle, window, or display) to the display with the largest overlap,
/// rather than defaulting to the primary display. Clamps the capture bounds to that display.
/// </summary>
public static class RecordingDisplayResolver
{
    public static ResolvedRecordingDisplay Resolve(
        IEnumerable<DisplayInfo> displays,
        CaptureRegion region,
        MaxResolution maxResolution = MaxResolution.Original)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(region);

        var displayList = displays as IReadOnlyList<DisplayInfo> ?? displays.ToList();
        if (displayList.Count == 0)
        {
            throw new InvalidOperationException("No displays available for recording.");
        }

        return region switch
        {
            CaptureRegion.DisplayRegion d => ResolveDisplay(displayList, d.Id, maxResolution),
            CaptureRegion.RectRegion r => ResolveRect(displayList, r.Rect, maxResolution),
            CaptureRegion.WindowRegion w => ResolveRect(displayList, w.Frame, maxResolution),
            _ => throw new ArgumentOutOfRangeException(nameof(region), $"Unsupported capture region type: {region.GetType()}")
        };
    }

    public static ResolvedRecordingDisplay Resolve(
        IEnumerable<DisplayInfo> displays,
        Rect targetRect,
        MaxResolution maxResolution = MaxResolution.Original)
    {
        ArgumentNullException.ThrowIfNull(displays);

        var displayList = displays as IReadOnlyList<DisplayInfo> ?? displays.ToList();
        if (displayList.Count == 0)
        {
            throw new InvalidOperationException("No displays available for recording.");
        }

        return ResolveRect(displayList, targetRect, maxResolution);
    }

    private static ResolvedRecordingDisplay ResolveDisplay(
        IReadOnlyList<DisplayInfo> displays,
        uint displayId,
        MaxResolution maxResolution)
    {
        var targetDisplay = displays.FirstOrDefault(d => d.DisplayId == displayId)
            ?? displays.FirstOrDefault(d => d.IsPrimary)
            ?? displays[0];

        var globalRect = targetDisplay.Bounds.Standardized;
        var localRect = new Rect(0, 0, globalRect.Width, globalRect.Height);
        var (outW, outH) = CalculateOutputSize(localRect.Width, localRect.Height, maxResolution);

        return new ResolvedRecordingDisplay(targetDisplay, globalRect, localRect, outW, outH);
    }

    private static ResolvedRecordingDisplay ResolveRect(
        IReadOnlyList<DisplayInfo> displays,
        Rect targetRect,
        MaxResolution maxResolution)
    {
        var standardized = targetRect.Standardized;
        var bestDisplay = DisplayMath.FindLargestOverlap(displays, standardized)
            ?? displays.FirstOrDefault(d => d.IsPrimary)
            ?? displays[0];

        var intersection = standardized.Intersection(bestDisplay.Bounds.Standardized);
        var clampedGlobal = intersection ?? bestDisplay.Bounds.Standardized;

        // Ensure minimum 2x2 dimensions
        double width = Math.Max(2.0, clampedGlobal.Width);
        double height = Math.Max(2.0, clampedGlobal.Height);
        clampedGlobal = new Rect(clampedGlobal.X, clampedGlobal.Y, width, height);

        var localRect = new Rect(
            clampedGlobal.X - bestDisplay.Bounds.X,
            clampedGlobal.Y - bestDisplay.Bounds.Y,
            clampedGlobal.Width,
            clampedGlobal.Height
        );

        var (outW, outH) = CalculateOutputSize(localRect.Width, localRect.Height, maxResolution);

        return new ResolvedRecordingDisplay(bestDisplay, clampedGlobal, localRect, outW, outH);
    }

    private static (int Width, int Height) CalculateOutputSize(double width, double height, MaxResolution maxResolution)
    {
        int? cap = maxResolution.MaxLongestEdge();
        if (cap.HasValue && Math.Max(width, height) > cap.Value)
        {
            double scale = (double)cap.Value / Math.Max(width, height);
            width *= scale;
            height *= scale;
        }

        // Even dimensions, minimum 2
        int outW = Math.Max(2, (int)Math.Floor(width / 2.0) * 2);
        int outH = Math.Max(2, (int)Math.Floor(height / 2.0) * 2);
        return (outW, outH);
    }
}
