// Ported from LightshotKit/Sources/LightshotKit/Appearance.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// The look Lightshot's chrome is drawn in right now.
/// </summary>
public enum Appearance
{
    Light,
    Dark
}

/// <summary>
/// The Settings choice behind the appearance.
/// </summary>
public enum AppearancePreference
{
    System,
    Light,
    Dark
}

public static class AppearancePreferenceExtensions
{
    public static string Title(this AppearancePreference preference) => preference switch
    {
        AppearancePreference.System => "Match System",
        AppearancePreference.Light => "Light",
        AppearancePreference.Dark => "Dark",
        _ => "Match System"
    };

    public static AppearancePreference FromStoredValue(string? storedValue) => storedValue switch
    {
        "light" => AppearancePreference.Light,
        "dark" => AppearancePreference.Dark,
        _ => AppearancePreference.System
    };

    public static Appearance Resolved(this AppearancePreference preference, Appearance system) => preference switch
    {
        AppearancePreference.System => system,
        AppearancePreference.Light => Appearance.Light,
        AppearancePreference.Dark => Appearance.Dark,
        _ => system
    };
}

public static class RGBAColorExtensions
{
    /// <summary>
    /// WCAG relative luminance of the colour's RGB (alpha ignored), from its sRGB components.
    /// </summary>
    public static double RelativeLuminance(this RGBAColor color)
    {
        static double Linear(double c)
        {
            double clamped = Math.Min(Math.Max(c, 0.0), 1.0);
            return clamped <= 0.03928 ? clamped / 12.92 : Math.Pow((clamped + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    /// <summary>
    /// This colour laid over backdrop (source-over, in sRGB); the result is opaque when the backdrop is.
    /// </summary>
    public static RGBAColor Composited(this RGBAColor foreground, RGBAColor backdrop)
    {
        double a = foreground.A + backdrop.A * (1.0 - foreground.A);
        if (a <= 0)
        {
            return new RGBAColor(0, 0, 0, 0);
        }

        double Mix(double top, double bottom) =>
            (top * foreground.A + bottom * backdrop.A * (1.0 - foreground.A)) / a;

        return new RGBAColor(
            Mix(foreground.R, backdrop.R),
            Mix(foreground.G, backdrop.G),
            Mix(foreground.B, backdrop.B),
            a
        );
    }

    /// <summary>
    /// The WCAG contrast ratio (1...21) of this colour drawn on background;
    /// a translucent foreground is composited over the background first.
    /// </summary>
    public static double ContrastRatio(this RGBAColor foreground, RGBAColor background)
    {
        double top = foreground.Composited(background).RelativeLuminance();
        double bottom = background.RelativeLuminance();
        return (Math.Max(top, bottom) + 0.05) / (Math.Min(top, bottom) + 0.05);
    }
}
