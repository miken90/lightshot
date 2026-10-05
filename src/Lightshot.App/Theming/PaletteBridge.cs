// Ported from LightshotKit/Sources/LightshotKit/ThemePalette.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Media;
using Lightshot.Core;

namespace Lightshot.App.Theming;

/// <summary>
/// Bridges Core ThemePalette tokens to WPF Brushes and ResourceDictionary entries.
/// Resource-key contract: "Theme.<TokenName>", e.g. "Theme.Panel", "Theme.TextPrimary".
/// All consumers bind via DynamicResource.
/// </summary>
public static class PaletteBridge
{
    public static string KeyForToken(ThemeToken token) => $"Theme.{token}";

    public static Color ToWpfColor(RGBAColor color)
    {
        return Color.FromArgb(
            (byte)Math.Clamp((int)Math.Round(color.A * 255.0, MidpointRounding.AwayFromZero), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.R * 255.0, MidpointRounding.AwayFromZero), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * 255.0, MidpointRounding.AwayFromZero), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * 255.0, MidpointRounding.AwayFromZero), 0, 255));
    }


    public static RGBAColor ToRgbaColor(Color color)
    {
        return new RGBAColor(color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);
    }

    public static SolidColorBrush CreateBrush(ThemeToken token, Appearance appearance)
    {
        var brush = new SolidColorBrush(ToWpfColor(ThemePalette.Color(token, appearance)));
        brush.Freeze();
        return brush;
    }

    public static ResourceDictionary CreateThemeDictionary(Appearance appearance)
    {
        var dict = new ResourceDictionary();
        PopulateThemeDictionary(dict, appearance);
        return dict;
    }

    public static void PopulateThemeDictionary(ResourceDictionary target, Appearance appearance)
    {
        foreach (ThemeToken token in Enum.GetValues<ThemeToken>())
        {
            target[KeyForToken(token)] = CreateBrush(token, appearance);
        }
    }
}
