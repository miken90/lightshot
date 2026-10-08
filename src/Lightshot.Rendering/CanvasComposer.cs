// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Pure Skia compositor for styled screenshot canvas (backdrop fill, shadow, corner rounding).
/// </summary>
public static class CanvasComposer
{
    public static SKBitmap Compose(SKBitmap image, CanvasFrame frame, CanvasStyle style, SKBitmap? edgeSource)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));

        int width = Math.Max(1, frame.Width);
        int height = Math.Max(1, frame.Height);

        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(result))
        {
            PaintFill(canvas, frame, style.EffectiveFill, edgeSource ?? image);
            if (style.Shadow > 0) PaintShadow(canvas, frame, style);

            if (image.Width > 0 && image.Height > 0)
            {
                var dest = new SKRect(
                    (float)frame.ImageRect.MinX,
                    (float)frame.ImageRect.MinY,
                    (float)frame.ImageRect.MaxX,
                    (float)frame.ImageRect.MaxY);
                float radius = (float)(style.CornerRadius * frame.Scale);

                canvas.Save();
                if (radius > 0)
                {
                    canvas.ClipRoundRect(new SKRoundRect(dest, radius, radius), SKClipOperation.Intersect, antialias: true);
                }

                if (style.Inset > 0)
                {
                    var src = edgeSource ?? image;
                    var order = src.ColorType == SKColorType.Bgra8888 ? PixelOrder.Bgra : PixelOrder.Rgba;
                    var edgeColor = EdgeColorSampler.Sample(src.GetPixelSpan(), src.Width, src.Height, order);
                    using var fillPaint = new SKPaint
                    {
                        Color = ElementPainter.ToSKColor(edgeColor),
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    canvas.DrawRect(dest, fillPaint);
                }

                float insetScaled = (float)(Math.Max(0.0, style.Inset) * frame.Scale);
                var imageDest = new SKRect(
                    dest.Left + insetScaled,
                    dest.Top + insetScaled,
                    dest.Right - insetScaled,
                    dest.Bottom - insetScaled);

                using var skImage = SKImage.FromBitmap(image);
                canvas.DrawImage(skImage, imageDest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

                if (style.BorderWidth > 0)
                {
                    float strokeWidth = (float)(style.BorderWidth * frame.Scale);
                    if (strokeWidth > 0)
                    {
                        float halfStroke = strokeWidth / 2f;
                        var borderDest = new SKRect(
                            dest.Left + halfStroke,
                            dest.Top + halfStroke,
                            dest.Right - halfStroke,
                            dest.Bottom - halfStroke);
                        float borderRadius = Math.Max(0f, radius - halfStroke);

                        using var borderPaint = new SKPaint
                        {
                            Color = ElementPainter.ToSKColor(style.EffectiveBorderColor),
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = strokeWidth,
                            IsAntialias = true
                        };

                        if (borderRadius > 0)
                        {
                            canvas.DrawRoundRect(new SKRoundRect(borderDest, borderRadius, borderRadius), borderPaint);
                        }
                        else
                        {
                            canvas.DrawRect(borderDest, borderPaint);
                        }
                    }
                }

                canvas.Restore();
            }

            canvas.Flush();
        }

        return result;
    }

    public static SKBitmap RenderBackdrop(CanvasFrame frame, CanvasStyle style, SKBitmap? edgeSource)
    {
        int width = Math.Max(1, frame.Width);
        int height = Math.Max(1, frame.Height);

        var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            PaintFill(canvas, frame, style.EffectiveFill, edgeSource);
            if (style.Shadow > 0) PaintShadow(canvas, frame, style);
            canvas.Flush();
        }

        return bmp;
    }

    private static void PaintFill(SKCanvas canvas, CanvasFrame frame, CanvasFill fill, SKBitmap? edgeSource)
    {
        switch (fill.Kind)
        {
            case CanvasFillKind.Solid:
                var solidColor = fill.Color.HasValue
                    ? ElementPainter.ToSKColor(fill.Color.Value)
                    : SKColors.White;
                canvas.Clear(solidColor);
                break;

            case CanvasFillKind.Gradient:
                PaintGradient(canvas, frame, fill.GradientIndex);
                break;

            case CanvasFillKind.AutoEdge:
                RGBAColor sampled;
                if (edgeSource != null)
                {
                    var order = edgeSource.ColorType == SKColorType.Bgra8888 ? PixelOrder.Bgra : PixelOrder.Rgba;
                    sampled = EdgeColorSampler.Sample(edgeSource.GetPixelSpan(), edgeSource.Width, edgeSource.Height, order);
                }
                else
                {
                    sampled = EdgeColorSampler.Fallback;
                }
                canvas.Clear(ElementPainter.ToSKColor(sampled));
                break;

            case CanvasFillKind.Image:
                if (!string.IsNullOrEmpty(fill.ImagePath) && File.Exists(fill.ImagePath))
                {
                    using var bgBmp = SKBitmap.Decode(fill.ImagePath);
                    if (bgBmp != null)
                    {
                        PaintImageCover(canvas, frame, bgBmp);
                        break;
                    }
                }
                PaintGradient(canvas, frame, 0);
                break;
        }
    }

    private static void PaintGradient(SKCanvas canvas, CanvasFrame frame, int gradientIndex)
    {
        var preset = GradientPresets.At(gradientIndex);
        var start = new SKPoint(0, 0);
        var end = new SKPoint(frame.Width, frame.Height);

        using var shader = SKShader.CreateLinearGradient(
            start,
            end,
            [ElementPainter.ToSKColor(preset.From), ElementPainter.ToSKColor(preset.To)],
            [0.0f, 1.0f],
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            Shader = shader,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        canvas.DrawRect(new SKRect(0, 0, frame.Width, frame.Height), paint);
    }

    private static void PaintImageCover(SKCanvas canvas, CanvasFrame frame, SKBitmap bgImage)
    {
        if (bgImage.Width <= 0 || bgImage.Height <= 0)
        {
            PaintGradient(canvas, frame, 0);
            return;
        }

        float scale = Math.Max(
            (float)frame.Width / bgImage.Width,
            (float)frame.Height / bgImage.Height);

        float scaledW = bgImage.Width * scale;
        float scaledH = bgImage.Height * scale;
        float dx = (frame.Width - scaledW) / 2f;
        float dy = (frame.Height - scaledH) / 2f;

        var dest = new SKRect(dx, dy, dx + scaledW, dy + scaledH);
        using var skImg = SKImage.FromBitmap(bgImage);
        canvas.DrawImage(skImg, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    private static void PaintShadow(SKCanvas canvas, CanvasFrame frame, CanvasStyle style)
    {
        float radius = (float)(style.CornerRadius * frame.Scale);
        float dy = (float)(style.Shadow / 4.0);
        var dest = new SKRect(
            (float)frame.ImageRect.MinX,
            (float)frame.ImageRect.MinY,
            (float)frame.ImageRect.MaxX,
            (float)frame.ImageRect.MaxY);
        var shadowRect = new SKRect(dest.Left, dest.Top + dy, dest.Right, dest.Bottom + dy);
        float sigma = (float)(style.Shadow * 0.5);

        using var paint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 100),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma)
        };

        if (radius > 0)
        {
            canvas.DrawRoundRect(new SKRoundRect(shadowRect, radius, radius), paint);
        }
        else
        {
            canvas.DrawRect(shadowRect, paint);
        }
    }
}
