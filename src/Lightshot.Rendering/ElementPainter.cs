// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Renders individual annotation elements into a Skia canvas.
/// Matches upstream geometry, styles, and blend modes.
/// </summary>
public static class ElementPainter
{
    public const double HighlightAlphaCap = 0.35;

    public static void Draw(
        SKCanvas canvas,
        AnnotationElement element,
        Rect frame,
        Func<SKBitmap?>? snapshotProvider = null)
    {
        var style = element.Style;

        switch (element.ElementKind)
        {
            case AnnotationElement.Kind.Line line:
                DrawPolyline(canvas, [line.From, line.To], style, frame);
                break;

            case AnnotationElement.Kind.Arrow arrow:
                DrawArrow(canvas, arrow.From, arrow.To, arrow.Bend, arrow.Style, style, frame);
                break;

            case AnnotationElement.Kind.Rectangle rect:
                DrawRectangle(canvas, rect.Rect, style, frame);
                break;

            case AnnotationElement.Kind.Ellipse ellipse:
                DrawEllipse(canvas, ellipse.Rect, style, frame);
                break;

            case AnnotationElement.Kind.Freehand freehand:
                DrawPolyline(canvas, freehand.Points, style, frame);
                break;

            case AnnotationElement.Kind.Text text:
                DrawText(canvas, text.Content, text.Box, style, frame);
                break;

            case AnnotationElement.Kind.StepMarker marker:
                DrawStepMarker(canvas, marker.Number, marker.Center, marker.Radius, style, frame);
                break;

            case AnnotationElement.Kind.Highlight highlight:
                DrawHighlight(canvas, highlight.Rect, style, frame);
                break;

            case AnnotationElement.Kind.Redaction redaction:
                DrawRedaction(canvas, redaction.Rect, redaction.Style, redaction.Strength, redaction.Seed, frame, snapshotProvider);
                break;

            case AnnotationElement.Kind.Focus:
                // Focus dim is drawn once as a composite background layer
                break;
        }
    }

    private static void DrawRectangle(SKCanvas canvas, Rect rect, Style style, Rect frame)
    {
        var s = rect.Standardized;
        var r = new SKRect(
            (float)(s.MinX - frame.MinX),
            (float)(s.MinY - frame.MinY),
            (float)(s.MaxX - frame.MinX),
            (float)(s.MaxY - frame.MinY));

        if (style.Fill != null)
        {
            using var fillPaint = new SKPaint
            {
                Color = ToSKColor(style.Fill.Value),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(r, fillPaint);
        }

        if (style.StrokeWidth > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ToSKColor(style.Color),
                StrokeWidth = (float)style.StrokeWidth,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            canvas.DrawRect(r, strokePaint);
        }
    }

    private static void DrawEllipse(SKCanvas canvas, Rect rect, Style style, Rect frame)
    {
        var s = rect.Standardized;
        var r = new SKRect(
            (float)(s.MinX - frame.MinX),
            (float)(s.MinY - frame.MinY),
            (float)(s.MaxX - frame.MinX),
            (float)(s.MaxY - frame.MinY));

        if (style.Fill != null)
        {
            using var fillPaint = new SKPaint
            {
                Color = ToSKColor(style.Fill.Value),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawOval(r, fillPaint);
        }

        if (style.StrokeWidth > 0)
        {
            using var strokePaint = new SKPaint
            {
                Color = ToSKColor(style.Color),
                StrokeWidth = (float)style.StrokeWidth,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            canvas.DrawOval(r, strokePaint);
        }
    }

    private static void DrawPolyline(SKCanvas canvas, IReadOnlyList<Point> points, Style style, Rect frame)
    {
        if (points == null || points.Count < 2) return;

        using var path = new SKPath();
        path.MoveTo((float)(points[0].X - frame.MinX), (float)(points[0].Y - frame.MinY));

        for (int i = 1; i < points.Count; i++)
        {
            path.LineTo((float)(points[i].X - frame.MinX), (float)(points[i].Y - frame.MinY));
        }

        using var strokePaint = new SKPaint
        {
            Color = ToSKColor(style.Color),
            StrokeWidth = (float)style.StrokeWidth,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        canvas.DrawPath(path, strokePaint);
    }

    private static void DrawArrow(
        SKCanvas canvas,
        Point from,
        Point to,
        Point? bend,
        ArrowStyle arrowStyle,
        Style style,
        Rect frame)
    {
        Point relFrom = new(from.X - frame.MinX, from.Y - frame.MinY);
        Point relTo = new(to.X - frame.MinX, to.Y - frame.MinY);
        Point? relBend = bend != null ? new(bend.Value.X - frame.MinX, bend.Value.Y - frame.MinY) : null;

        var shape = ArrowGeometry.ComputeArrowShape(relFrom, relTo, relBend, arrowStyle, style.StrokeWidth);
        if (shape.Path.Count == 0) return;

        using var path = new SKPath();
        foreach (var elem in shape.Path)
        {
            switch (elem)
            {
                case PathElement.Move m:
                    path.MoveTo((float)m.Point.X, (float)m.Point.Y);
                    break;
                case PathElement.Line l:
                    path.LineTo((float)l.Point.X, (float)l.Point.Y);
                    break;
                case PathElement.QuadCurve q:
                    path.QuadTo((float)q.Control.X, (float)q.Control.Y, (float)q.To.X, (float)q.To.Y);
                    break;
                case PathElement.Close:
                    path.Close();
                    break;
            }
        }

        var color = ToSKColor(style.Color);

        switch (shape.Paint)
        {
            case ArrowShapePaint.Fill fill:
                using (var fillPaint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                })
                {
                    canvas.DrawPath(path, fillPaint);
                }

                if (fill.Rounding > 0)
                {
                    using var strokePaint = new SKPaint
                    {
                        Color = color,
                        StrokeWidth = (float)fill.Rounding,
                        Style = SKPaintStyle.Stroke,
                        StrokeCap = SKStrokeCap.Round,
                        StrokeJoin = SKStrokeJoin.Round,
                        IsAntialias = true
                    };
                    canvas.DrawPath(path, strokePaint);
                }
                break;

            case ArrowShapePaint.Stroke stroke:
                using (var strokePaint = new SKPaint
                {
                    Color = color,
                    StrokeWidth = (float)stroke.Width,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    IsAntialias = true
                })
                {
                    canvas.DrawPath(path, strokePaint);
                }
                break;
        }
    }

    private static void DrawText(SKCanvas canvas, string text, Rect box, Style style, Rect frame)
    {
        double pad = SkiaTextLayout.Padding(style.FontSize);
        var s = box.Standardized;
        var content = new SKRect(
            (float)(s.MinX - frame.MinX + pad),
            (float)(s.MinY - frame.MinY + pad),
            (float)(s.MaxX - frame.MinX - pad),
            (float)(s.MaxY - frame.MinY - pad));

        SkiaTextLayout.Draw(canvas, text, style.FontSize, ToSKColor(style.Color), content);
    }

    private static void DrawStepMarker(SKCanvas canvas, int number, Point center, double radius, Style style, Rect frame)
    {
        float cx = (float)(center.X - frame.MinX);
        float cy = (float)(center.Y - frame.MinY);
        float r = (float)radius;

        // Draw solid disc
        using (var discPaint = new SKPaint
        {
            Color = ToSKColor(style.Color),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            canvas.DrawCircle(cx, cy, r, discPaint);
        }

        // Draw centered number in white
        string text = number.ToString();
        double numFontSize = radius * 1.2;
        var font = FontProvider.GetFont(numFontSize, bold: true);
        using var textPaint = FontProvider.CreateTextPaint(SKColors.White);

        font.GetFontMetrics(out var metrics);
        float textWidth = font.MeasureText(text);
        float tx = cx - textWidth / 2f;
        float ty = cy - (metrics.Ascent + metrics.Descent) / 2f;

        canvas.DrawText(text, tx, ty, font, textPaint);
    }

    private static void DrawHighlight(SKCanvas canvas, Rect rect, Style style, Rect frame)
    {
        var s = rect.Standardized;
        var r = new SKRect(
            (float)(s.MinX - frame.MinX),
            (float)(s.MinY - frame.MinY),
            (float)(s.MaxX - frame.MinX),
            (float)(s.MaxY - frame.MinY));

        double washAlpha = Math.Min(style.Color.A, HighlightAlphaCap);
        var washColor = new SKColor(
            (byte)Math.Round(style.Color.R * 255.0),
            (byte)Math.Round(style.Color.G * 255.0),
            (byte)Math.Round(style.Color.B * 255.0),
            (byte)Math.Round(washAlpha * 255.0));

        using var paint = new SKPaint
        {
            Color = washColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(r, paint);
    }

    private static void DrawRedaction(
        SKCanvas canvas,
        Rect rect,
        RedactionStyle style,
        double strength,
        ulong seed,
        Rect frame,
        Func<SKBitmap?>? snapshotProvider)
    {
        var s = rect.Standardized;
        var r = new SKRect(
            (float)(s.MinX - frame.MinX),
            (float)(s.MinY - frame.MinY),
            (float)(s.MaxX - frame.MinX),
            (float)(s.MaxY - frame.MinY));

        if (style == RedactionStyle.Blackout)
        {
            using var blackPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };
            canvas.DrawRect(r, blackPaint);
            return;
        }

        if (snapshotProvider == null) return;
        using var snapshot = snapshotProvider();
        if (snapshot == null) return;

        var backdrop = new RedactionBackdrop(snapshot, frame);
        using var patch = backdrop.Patch(rect, style, strength, seed);
        if (patch == null) return;

        var patchRect = new SKRect(
            (float)(patch.Rect.MinX - frame.MinX),
            (float)(patch.Rect.MinY - frame.MinY),
            (float)(patch.Rect.MaxX - frame.MinX),
            (float)(patch.Rect.MaxY - frame.MinY));

        using var copyPaint = new SKPaint
        {
            BlendMode = SKBlendMode.Src, // Replace destination pixels outright
            IsAntialias = false
        };
        canvas.DrawBitmap(patch.Image, patchRect, copyPaint);
    }

    public static SKColor ToSKColor(RGBAColor c)
    {
        return new SKColor(
            (byte)Math.Round(Math.Clamp(c.R, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(c.G, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(c.B, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(c.A, 0.0, 1.0) * 255.0));
    }
}
