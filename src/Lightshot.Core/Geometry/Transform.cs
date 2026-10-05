// Ported from LightshotKit/Sources/LightshotKit/Transform.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// A geometric edit applied to an element via the transform command.
/// </summary>
public abstract record Transform
{
    public sealed record Move(double Dx, double Dy) : Transform;
    public sealed record Resize(Handle Handle, double Dx, double Dy) : Transform;
    public sealed record Reshape(EndpointHandle Handle, double Dx, double Dy) : Transform;
}

public static class RectTransformExtensions
{
    /// <summary>
    /// The new bounding box produced by dragging handle by (dx, dy).
    /// </summary>
    public static Rect Resized(this Rect rect, Handle handle, double dx, double dy)
    {
        double left = rect.MinX;
        double top = rect.MinY;
        double right = rect.MaxX;
        double bottom = rect.MaxY;

        switch (handle)
        {
            case Handle.TopLeft:
                left += dx;
                top += dy;
                break;
            case Handle.Top:
                top += dy;
                break;
            case Handle.TopRight:
                right += dx;
                top += dy;
                break;
            case Handle.Right:
                right += dx;
                break;
            case Handle.BottomRight:
                right += dx;
                bottom += dy;
                break;
            case Handle.Bottom:
                bottom += dy;
                break;
            case Handle.BottomLeft:
                left += dx;
                bottom += dy;
                break;
            case Handle.Left:
                left += dx;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
        }

        return new Rect(left, top, right - left, bottom - top).Standardized;
    }
}
