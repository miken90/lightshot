// Ported from LightshotKit/Sources/LightshotKit/Style.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// An RGBA color with normalized 0.0...1.0 components.
/// A pure value so the domain core never reaches for platform colors.
/// </summary>
public readonly record struct RGBAColor(double R, double G, double B, double A = 1.0)
{
    public static readonly RGBAColor Red = new(1, 0, 0);
    public static readonly RGBAColor RedColor = Red;
    public static readonly RGBAColor Black = new(0, 0, 0);
    public static readonly RGBAColor White = new(1, 1, 1);
    public static readonly RGBAColor Transparent = new(0, 0, 0, 0);
}

/// <summary>
/// How a redaction region obscures the pixels beneath it.
/// Only blackout is secure redaction - an opaque fill that erases pixels.
/// </summary>
public enum RedactionStyle
{
    Blackout,
    Blur,
    Pixelate
}

public static class RedactionStyleDefaults
{
    /// <summary>
    /// Strength a new redaction starts at - midpoint of 0...1 range.
    /// </summary>
    public const double DefaultStrength = 0.5;
}

/// <summary>
/// How an arrow is drawn.
/// </summary>
public enum ArrowStyle
{
    /// <summary>Tapered shaft, solid head with softened corners.</summary>
    Standard,
    /// <summary>Tapered shaft, sharp swept-back (barbed) head.</summary>
    Fancy,
    /// <summary>The standard arrow's taper and solid head on a bendable shaft.</summary>
    Curved,
    /// <summary>The standard arrow's solid head at both ends of a bendable shaft.</summary>
    Double
}

public static class ArrowStyleExtensions
{
    public static bool IsBendable(this ArrowStyle style) =>
        style is ArrowStyle.Curved or ArrowStyle.Double;
}

/// <summary>
/// Visual attributes shared by every annotation element.
/// </summary>
public record Style
{
    public RGBAColor Color { get; set; }
    public double StrokeWidth { get; set; }
    public double FontSize { get; set; }
    public RGBAColor? Fill { get; set; }

    public Style(
        RGBAColor? color = null,
        double strokeWidth = 6.0,
        double fontSize = 20.0,
        RGBAColor? fill = null)
    {
        Color = color ?? RGBAColor.RedColor;
        StrokeWidth = strokeWidth;
        FontSize = fontSize;
        Fill = fill;
    }

    public static Style Default => new();
}
