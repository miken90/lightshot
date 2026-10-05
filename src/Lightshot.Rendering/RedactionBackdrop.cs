// Ported from LightshotKit/Sources/LightshotKit/RedactionEffect.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Obscured pixels plus the image-space rect they cover (the redaction clipped to the frame).
/// </summary>
public sealed class RedactionPatch : IDisposable
{
    public SKBitmap Image { get; }
    public Rect Rect { get; }

    public RedactionPatch(SKBitmap image, Rect rect)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Rect = rect;
    }

    public void Dispose()
    {
        Image.Dispose();
    }
}

/// <summary>
/// The flattened pixels a redaction sits on: base image plus elements beneath it.
/// Generates blurred or pixelated patches over target regions.
/// </summary>
public sealed class RedactionBackdrop
{
    public SKBitmap Image { get; }
    public Rect Frame { get; }

    public RedactionBackdrop(SKBitmap image, Rect frame)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Frame = frame;
    }

    public RedactionPatch? Patch(Rect rect, RedactionStyle style, double strength, ulong seed)
    {
        if (style == RedactionStyle.Blackout)
        {
            return null;
        }

        var s = rect.Standardized;
        int cropX = (int)Math.Floor(s.MinX - Frame.MinX);
        int cropY = (int)Math.Floor(s.MinY - Frame.MinY);
        int cropW = (int)Math.Round(s.Width);
        int cropH = (int)Math.Round(s.Height);

        // Intersect with image extent
        int x0 = Math.Max(0, cropX);
        int y0 = Math.Max(0, cropY);
        int x1 = Math.Min(Image.Width, cropX + cropW);
        int y1 = Math.Min(Image.Height, cropY + cropH);

        int actualW = x1 - x0;
        int actualH = y1 - y0;

        if (actualW < 1 || actualH < 1)
        {
            return null;
        }

        using var subset = new SKBitmap();
        if (!Image.ExtractSubset(subset, new SKRectI(x0, y0, x1, y1)))
        {
            return null;
        }

        double amount = Math.Clamp(strength, 0.0, 1.0);
        SKBitmap? obscured = null;

        switch (style)
        {
            case RedactionStyle.Pixelate:
            {
                double block = 6.0 + amount * (40.0 - 6.0);
                obscured = Scramble.Apply(subset, block, seed);
                break;
            }
            case RedactionStyle.Blur:
            {
                double sigma = 3.0 + amount * (30.0 - 3.0);
                double block = Math.Max(2.0, sigma / 2.0);
                using var scrambled = Scramble.Apply(subset, block, seed);
                obscured = GaussianBlur.Apply(scrambled, sigma);
                break;
            }
        }

        if (obscured == null)
        {
            return null;
        }

        var patchRect = new Rect(x0 + Frame.MinX, y0 + Frame.MinY, actualW, actualH);
        return new RedactionPatch(obscured, patchRect);
    }
}
