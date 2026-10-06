// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Renders an AnnotationDocument into a flattened RenderedImage.
/// Implements IImageRenderer for the core domain.
/// </summary>
public class DocumentRenderer : IImageRenderer
{
    public RenderedImage Render(AnnotationDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var frame = document.VisibleFrame;
        int width = Math.Max(1, (int)Math.Round(frame.Width));
        int height = Math.Max(1, (int)Math.Round(frame.Height));

        using var bitmap = Flatten(document, document.Elements);
        if (bitmap == null)
        {
            return new RenderedImage(width, height, document.BaseImage.Data.ToArray());
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] pngBytes = data != null ? data.ToArray() : Array.Empty<byte>();

        return new RenderedImage(width, height, pngBytes);
    }

    /// <summary>
    /// Flattens the base image plus the specified subset of elements in z-order into an SKBitmap.
    /// Shared by Render and redaction backdrop generation.
    /// </summary>
    /// <param name="focusElements">Elements whose focus areas dim the image; defaults to the
    /// document's. The editor passes what it shows, including a focus area still being drawn.</param>
    public static SKBitmap? Flatten(
        AnnotationDocument document,
        IReadOnlyList<AnnotationElement> elements,
        IEnumerable<AnnotationElement>? focusElements = null)
    {
        var frame = document.VisibleFrame;
        int width = Math.Max(1, (int)Math.Round(frame.Width));
        int height = Math.Max(1, (int)Math.Round(frame.Height));

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            // 1. Draw base image offset by frame origin
            DrawBaseImage(canvas, document.BaseImage, frame);

            // 2. Draw focus area dimming under the marks
            var focusAreas = (focusElements ?? document.Elements)
                .Select(e => e.ElementKind.FocusRect)
                .Where(r => r.HasValue)
                .Select(r => r!.Value)
                .ToList();

            if (focusAreas.Count > 0)
            {
                FocusDim.Draw(canvas, width, height, focusAreas, frame);
            }

            // 3. Draw elements in z-order
            foreach (var element in elements)
            {
                // Snapshot provider for blur and pixelate redactions
                Func<SKBitmap?> snapshotProvider = () =>
                {
                    var snapshot = new SKBitmap(info);
                    bitmap.CopyTo(snapshot);
                    return snapshot;
                };

                ElementPainter.Draw(canvas, element, frame, snapshotProvider);
            }

            canvas.Flush();
        }

        return bitmap;
    }

    /// <summary>
    /// Creates a RedactionBackdrop for the elements below index.
    /// </summary>
    public static RedactionBackdrop? CreateRedactionBackdrop(AnnotationDocument document, int belowIndex)
    {
        int count = Math.Clamp(belowIndex, 0, document.Elements.Count);
        var subElements = document.Elements.Take(count).ToList();
        var bitmap = Flatten(document, subElements);
        if (bitmap == null) return null;

        return new RedactionBackdrop(bitmap, document.VisibleFrame);
    }

    private static void DrawBaseImage(SKCanvas canvas, CapturedImage baseImage, Rect frame)
    {
        if (baseImage.Data.IsEmpty) return;

        int srcW = baseImage.PixelWidth;
        int srcH = baseImage.PixelHeight;
        if (srcW <= 0 || srcH <= 0) return;

        SKBitmap? baseBitmap = null;

        // Try decoding as compressed image format (PNG/JPEG)
        try
        {
            using var ms = new MemoryStream(baseImage.Data.ToArray());
            baseBitmap = SKBitmap.Decode(ms);
        }
        catch
        {
            baseBitmap = null;
        }

        // If decoding failed and data size matches uncompressed RGBA/BGRA
        if (baseBitmap == null && baseImage.Data.Length >= srcW * srcH * 4)
        {
            var info = new SKImageInfo(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Premul);
            baseBitmap = new SKBitmap(info);
            var span = baseBitmap.GetPixelSpan();
            baseImage.Data.Span.Slice(0, srcW * srcH * 4).CopyTo(span);
        }

        if (baseBitmap != null)
        {
            using (baseBitmap)
            {
                float dx = (float)(0.0 - frame.MinX);
                float dy = (float)(0.0 - frame.MinY);
                canvas.DrawBitmap(baseBitmap, dx, dy);
            }
        }
    }
}
