// Ported from LightshotKit/Sources/LightshotKit/TextLayout.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Reflection;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Loads and caches bundled Inter fonts from embedded resources.
/// Configures Skia fonts with grayscale antialiasing, no hinting, and subpixel positioning.
/// </summary>
public static class FontProvider
{
    private static readonly Lazy<SKTypeface> LazyRegular = new(LoadRegularTypeface);
    private static readonly Lazy<SKTypeface> LazyBold = new(LoadBoldTypeface);

    public static SKTypeface RegularTypeface => LazyRegular.Value;
    public static SKTypeface BoldTypeface => LazyBold.Value;

    public static SKFont GetFont(double fontSize, bool bold = false)
    {
        var typeface = bold ? BoldTypeface : RegularTypeface;
        return new SKFont(typeface, (float)fontSize)
        {
            Hinting = SKFontHinting.None,
            Subpixel = true,
            Edging = SKFontEdging.Antialias
        };
    }

    public static SKPaint CreateTextPaint(SKColor color)
    {
        return new SKPaint
        {
            Color = color,
            IsAntialias = true
        };
    }

    private static SKTypeface LoadRegularTypeface()
    {
        return LoadFromResource("Inter-Regular.ttf");
    }

    private static SKTypeface LoadBoldTypeface()
    {
        return LoadFromResource("Inter-Bold.ttf");
    }

    private static SKTypeface LoadFromResource(string fileName)
    {
        var assembly = typeof(FontProvider).Assembly;
        var names = assembly.GetManifestResourceNames();
        string? match = null;
        foreach (var name in names)
        {
            if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                match = name;
                break;
            }
        }

        if (match == null)
        {
            // Fallback to default sans-serif if resource loading fails
            return SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal);
        }

        using Stream? stream = assembly.GetManifestResourceStream(match);
        if (stream == null)
        {
            return SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal);
        }

        return SKTypeface.FromStream(stream) ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal);
    }
}
