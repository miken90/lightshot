// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Focus area dimming: darkens everything outside the union of focus areas.
/// Clears overlapping focus areas without double-dimming.
/// </summary>
public static class FocusDim
{
    public const double FocusDimAlpha = 0.55;

    public static double FocusCornerRadius(Rect area)
    {
        var s = area.Standardized;
        return Math.Min(12.0, Math.Min(s.Width / 4.0, s.Height / 4.0));
    }

    public static void Draw(SKCanvas canvas, int width, int height, IReadOnlyList<Rect> areas, Rect frame)
    {
        if (areas == null || areas.Count == 0)
        {
            return;
        }

        using var layerPaint = new SKPaint();
        canvas.SaveLayer(layerPaint);

        using (var dimPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)Math.Round(FocusDimAlpha * 255.0)),
            Style = SKPaintStyle.Fill
        })
        {
            canvas.DrawRect(new SKRect(0, 0, width, height), dimPaint);
        }

        using (var clearPaint = new SKPaint
        {
            BlendMode = SKBlendMode.Clear,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        })
        {
            foreach (var area in areas)
            {
                var s = area.Standardized;
                float x = (float)(s.MinX - frame.MinX);
                float y = (float)(s.MinY - frame.MinY);
                float w = (float)s.Width;
                float h = (float)s.Height;

                float r = (float)FocusCornerRadius(area);
                var roundRect = new SKRoundRect(new SKRect(x, y, x + w, y + h), r, r);
                canvas.DrawRoundRect(roundRect, clearPaint);
            }
        }

        canvas.Restore();
    }
}
