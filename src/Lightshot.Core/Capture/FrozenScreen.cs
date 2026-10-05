// Ported from LightshotKit/Sources/LightshotKit/FrozenScreen.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

/// <summary>
/// The desktop at the moment a capture starts: a still of every display,
/// plus each candidate window's own clean image.
/// </summary>
public record FrozenScreen
{
    private readonly List<FrozenDisplay> _displays;
    private readonly List<FrozenWindow> _windowList;

    public IReadOnlyList<FrozenDisplay> Displays => _displays;
    public IReadOnlyList<FrozenWindow> WindowList => _windowList;
    public IReadOnlyList<FrozenWindow> Windows => _windowList;

    public FrozenScreen(IEnumerable<FrozenDisplay> displays, IEnumerable<FrozenWindow>? windows = null)
    {
        _displays = displays.ToList();
        _windowList = windows?.ToList() ?? [];
    }

    public virtual bool Equals(FrozenScreen? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _displays.SequenceEqual(other._displays)
            && _windowList.SequenceEqual(other._windowList);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var d in _displays) hash.Add(d);
        foreach (var w in _windowList) hash.Add(w);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Cuts the specified region from the frozen screen.
    /// Returns null if the region touches no display or is a window without an image.
    /// </summary>
    public CapturedImage? ImageOf(CaptureRegion region)
    {
        switch (region)
        {
            case CaptureRegion.RectRegion rectRegion:
            {
                var selection = rectRegion.Rect.Standardized;
                FrozenDisplay? bestDisplay = null;
                double maxOverlap = 0;

                foreach (var display in _displays)
                {
                    double overlap = Overlap(display.Frame, selection);
                    if (overlap > maxOverlap)
                    {
                        maxOverlap = overlap;
                        bestDisplay = display;
                    }
                }

                if (bestDisplay == null || maxOverlap <= 0)
                {
                    return null;
                }

                return bestDisplay.Crop(selection);
            }

            case CaptureRegion.WindowRegion windowRegion:
            {
                var win = _windowList.FirstOrDefault(w => w.Id == windowRegion.Id);
                return win?.Image;
            }

            case CaptureRegion.DisplayRegion:
            default:
                return null;
        }
    }

    /// <summary>
    /// Returns a new FrozenScreen with window images populated from the map.
    /// </summary>
    public FrozenScreen WithWindowImages(IReadOnlyDictionary<uint, CapturedImage> images)
    {
        var updatedWindows = _windowList.Select(w =>
            images.TryGetValue(w.Id, out var img) ? w with { Image = img } : w
        );
        return new FrozenScreen(_displays, updatedWindows);
    }

    /// <summary>
    /// Assigns window images from the provided map by window id.
    /// </summary>
    public void SetWindowImages(IReadOnlyDictionary<uint, CapturedImage> images)
    {
        for (int i = 0; i < _windowList.Count; i++)
        {
            if (images.TryGetValue(_windowList[i].Id, out var img))
            {
                _windowList[i] = _windowList[i] with { Image = img };
            }
        }
    }

    private static double Overlap(Rect a, Rect b)
    {
        double width = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
        double height = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
        return width > 0 && height > 0 ? width * height : 0;
    }
}

/// <summary>
/// One display's still at the moment of freeze.
/// </summary>
public record FrozenDisplay(uint DisplayId, Rect Frame, CapturedImage Image)
{
    public CapturedImage? Crop(Rect selection)
    {
        if (Frame.Width <= 0 || Frame.Height <= 0 || Image.PixelWidth <= 0 || Image.PixelHeight <= 0)
        {
            return null;
        }

        double scale = (double)Image.PixelWidth / Frame.Width;

        int pxX = (int)Math.Floor((selection.MinX - Frame.MinX) * scale);
        int pxY = (int)Math.Floor((selection.MinY - Frame.MinY) * scale);
        int pxW = (int)Math.Round(selection.Width * scale);
        int pxH = (int)Math.Round(selection.Height * scale);

        int x0 = Math.Max(0, pxX);
        int y0 = Math.Max(0, pxY);
        int x1 = Math.Min(Image.PixelWidth, pxX + pxW);
        int y1 = Math.Min(Image.PixelHeight, pxY + pxH);

        int cropW = x1 - x0;
        int cropH = y1 - y0;

        if (cropW <= 0 || cropH <= 0)
        {
            return null;
        }

        // If raw pixel bytes are present (at least 4 bytes per pixel)
        int expectedSize = Image.PixelWidth * Image.PixelHeight * 4;
        if (Image.Data.Length >= expectedSize)
        {
            byte[] cropped = new byte[cropW * cropH * 4];
            var srcSpan = Image.Data.Span;
            for (int r = 0; r < cropH; r++)
            {
                int srcOffset = ((y0 + r) * Image.PixelWidth + x0) * 4;
                int dstOffset = r * cropW * 4;
                srcSpan.Slice(srcOffset, cropW * 4).CopyTo(cropped.AsSpan(dstOffset, cropW * 4));
            }
            return new CapturedImage(cropW, cropH, cropped);
        }

        // Dummy/stub data fallback
        return new CapturedImage(cropW, cropH, Image.Data);
    }
}

/// <summary>
/// A window picker candidate at the trigger.
/// </summary>
public record FrozenWindow(uint Id, Rect Frame, CapturedImage? Image = null);
