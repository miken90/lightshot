// Ported from LightshotKit/Sources/LightshotKit/PinBoardController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;

namespace Lightshot.App.Views.Pin;

public enum SizingEdge
{
    Left = 1,
    Right = 2,
    Top = 3,
    TopLeft = 4,
    TopRight = 5,
    Bottom = 6,
    BottomLeft = 7,
    BottomRight = 8
}

/// <summary>
/// Math helpers for pin window initial sizing and aspect-ratio locked resizing.
/// </summary>
public static class PinWindowMath
{
    public const double MinSide = 80.0;

    /// <summary>
    /// Calculates logical DIP size for a pin from image pixel dimensions, monitor scale factor,
    /// and monitor work area, keeping aspect ratio and capping to 90% of work area.
    /// </summary>
    public static Size CalculateInitialSize(int pixelWidth, int pixelHeight, double scaleFactor, Rect workArea)
    {
        double scale = scaleFactor > 0 ? scaleFactor : 1.0;
        double width = pixelWidth / scale;
        double height = pixelHeight / scale;

        if (width <= 0) width = MinSide;
        if (height <= 0) height = MinSide;

        if (workArea.Width > 0 && workArea.Height > 0)
        {
            double maxW = workArea.Width * 0.9;
            double maxH = workArea.Height * 0.9;
            double factor = Math.Min(1.0, Math.Min(maxW / width, maxH / height));
            width *= factor;
            height *= factor;
        }

        width = Math.Max(MinSide, width);
        height = Math.Max(MinSide, height);

        return new Size(width, height);
    }

    /// <summary>
    /// Adjusts the candidate window bounding rectangle during a WM_SIZING hook
    /// so the resulting window maintains the target aspect ratio (width / height).
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) AdjustSizingRect(
        SizingEdge edge,
        int left,
        int top,
        int right,
        int bottom,
        double targetAspect)
    {
        if (targetAspect <= 0)
        {
            return (left, top, right, bottom);
        }

        int width = right - left;
        int height = bottom - top;

        switch (edge)
        {
            case SizingEdge.Left:
            case SizingEdge.Right:
            {
                int newHeight = (int)Math.Round(width / targetAspect);
                bottom = top + newHeight;
                break;
            }

            case SizingEdge.Top:
            case SizingEdge.Bottom:
            {
                int newWidth = (int)Math.Round(height * targetAspect);
                right = left + newWidth;
                break;
            }

            case SizingEdge.TopLeft:
            case SizingEdge.TopRight:
            {
                int newHeight = (int)Math.Round(width / targetAspect);
                top = bottom - newHeight;
                break;
            }

            case SizingEdge.BottomLeft:
            case SizingEdge.BottomRight:
            {
                int newHeight = (int)Math.Round(width / targetAspect);
                bottom = top + newHeight;
                break;
            }
        }

        return (left, top, right, bottom);
    }
}
