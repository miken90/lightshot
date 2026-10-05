// Ported from LightshotKit/Sources/LightshotKit/ThemePalette.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

/// <summary>
/// One named colour of Lightshot's custom chrome.
/// </summary>
public enum ThemeToken
{
    Panel, PanelEdge, PanelEdgeStrong, PanelShadow, GridLine,
    ControlHover, ControlOn, ControlHoverEdge,
    CellHover, CellOn, CellOnHover,
    Well, RowHover, HelpFill,
    TipBackground, TipText, TipEdge,

    TextPrimary, TextStrong, Glyph, GlyphOff, TextSecondary, TextTertiary, TextDisabled,
    OnAccent,
    Warning,

    StudioCanvas, StudioDivider, StudioControl, StudioClip, ClipEdge, SelectionRing, PreviewShadow,

    AccentBlue, Playhead, TimelineSelection, ZoomAccent, TextAccent, TrimAccent, SpeedAccent, StudioPink, StudioViolet
}

/// <summary>
/// Every ThemeToken's colour in Light and Dark.
/// </summary>
public static class ThemePalette
{
    public const double TextContrast = 4.5;
    public const double GlyphContrast = 3.0;
    public const double StateContrast = 1.12;

    public static RGBAColor Color(ThemeToken token, Appearance appearance)
    {
        var (light, dark) = Values(token);
        return appearance == Appearance.Dark ? dark : light;
    }

    private static RGBAColor White(double alpha) => new(1, 1, 1, alpha);
    private static RGBAColor Black(double alpha) => new(0, 0, 0, alpha);
    private static RGBAColor Grey(double level) => new(level, level, level, 1.0);
    private static readonly RGBAColor Ink = new(0.11, 0.11, 0.13, 1.0);

    private static (RGBAColor Light, RGBAColor Dark) Values(ThemeToken token) => token switch
    {
        ThemeToken.Panel => (new RGBAColor(0.965, 0.965, 0.97), new RGBAColor(0.17, 0.19, 0.22)),
        ThemeToken.PanelEdge => (Black(0.1), White(0.08)),
        ThemeToken.PanelEdgeStrong => (Black(0.16), White(0.18)),
        ThemeToken.PanelShadow => (Black(0.16), Black(0.3)),
        ThemeToken.GridLine => (Black(0.08), White(0.09)),
        ThemeToken.ControlHover => (Black(0.06), White(0.1)),
        ThemeToken.ControlOn => (Black(0.1), White(0.14)),
        ThemeToken.ControlHoverEdge => (Black(0.14), White(0.18)),
        ThemeToken.CellHover => (Black(0.05), White(0.07)),
        ThemeToken.CellOn => (Black(0.09), Black(0.3)),
        ThemeToken.CellOnHover => (Black(0.13), Black(0.4)),
        ThemeToken.Well => (Black(0.07), Black(0.35)),
        ThemeToken.RowHover => (Black(0.06), White(0.08)),
        ThemeToken.HelpFill => (Black(0.12), White(0.22)),
        ThemeToken.TipBackground => (White(0.98), Black(0.85)),
        ThemeToken.TipText => (Ink, White(1)),
        ThemeToken.TipEdge => (Black(0.12), White(0.15)),

        ThemeToken.TextPrimary => (Ink, White(1)),
        ThemeToken.TextStrong => (Black(0.8), White(0.85)),
        ThemeToken.Glyph => (Black(0.7), White(0.75)),
        ThemeToken.GlyphOff => (Black(0.55), White(0.6)),
        ThemeToken.TextSecondary => (Black(0.6), White(0.55)),
        ThemeToken.TextTertiary => (Black(0.6), White(0.5)),
        ThemeToken.TextDisabled => (Black(0.3), White(0.35)),
        ThemeToken.OnAccent => (White(1), White(1)),
        ThemeToken.Warning => (new RGBAColor(0.72, 0.33, 0), new RGBAColor(1, 0.62, 0.04)),

        ThemeToken.StudioCanvas => (new RGBAColor(0.93, 0.93, 0.94), Grey(0.12)),
        ThemeToken.StudioDivider => (Black(0.1), White(0.08)),
        ThemeToken.StudioControl => (Black(0.07), White(0.08)),
        ThemeToken.StudioClip => (new RGBAColor(0.78, 0.8, 0.85), new RGBAColor(0.25, 0.27, 0.32)),
        ThemeToken.ClipEdge => (Black(0.15), White(0.15)),
        ThemeToken.SelectionRing => (new RGBAColor(0.04, 0.52, 1), White(1)),
        ThemeToken.PreviewShadow => (Black(0.18), Black(0.4)),

        ThemeToken.AccentBlue => Same(new RGBAColor(0.04, 0.52, 1)),
        ThemeToken.Playhead => Same(new RGBAColor(1, 0.27, 0.23)),
        ThemeToken.TimelineSelection => Same(new RGBAColor(1, 0.8, 0.2)),
        ThemeToken.ZoomAccent => Same(new RGBAColor(0.45, 0.35, 0.95)),
        ThemeToken.TextAccent => Same(new RGBAColor(0.95, 0.55, 0.2)),
        ThemeToken.TrimAccent => Same(new RGBAColor(0.88, 0.24, 0.3)),
        ThemeToken.SpeedAccent => Same(new RGBAColor(0.08, 0.55, 0.42)),
        ThemeToken.StudioPink => Same(new RGBAColor(0.95, 0.4, 0.6)),
        ThemeToken.StudioViolet => Same(new RGBAColor(0.6, 0.45, 0.95)),
        _ => (Ink, White(1))
    };

    private static (RGBAColor, RGBAColor) Same(RGBAColor color) => (color, color);

    public record LegibilityPair(
        ThemeToken Foreground,
        ThemeToken Surface,
        ThemeToken? Backdrop,
        double Minimum)
    {
        public override string ToString() =>
            $"{Foreground} on {Surface}" + (Backdrop.HasValue ? $" over {Backdrop.Value}" : "");

        public double Contrast(Appearance appearance)
        {
            RGBAColor background = ThemePalette.Color(Surface, appearance);
            if (Backdrop.HasValue)
            {
                background = background.Composited(ThemePalette.Color(Backdrop.Value, appearance));
            }
            return ThemePalette.Color(Foreground, appearance).ContrastRatio(background);
        }
    }

    public static readonly IReadOnlyList<LegibilityPair> LegibilityPairs =
    [
        // Toolbar text and glyphs.
        new(ThemeToken.TextPrimary, ThemeToken.Panel, null, TextContrast),
        new(ThemeToken.TextStrong, ThemeToken.Panel, null, TextContrast),
        new(ThemeToken.TextTertiary, ThemeToken.Panel, null, TextContrast),
        new(ThemeToken.Glyph, ThemeToken.Panel, null, GlyphContrast),
        new(ThemeToken.GlyphOff, ThemeToken.Panel, null, GlyphContrast),
        new(ThemeToken.Warning, ThemeToken.Panel, null, TextContrast),
        new(ThemeToken.TextPrimary, ThemeToken.Well, ThemeToken.Panel, TextContrast),
        new(ThemeToken.TextPrimary, ThemeToken.CellOn, ThemeToken.Panel, TextContrast),
        new(ThemeToken.TextPrimary, ThemeToken.ControlOn, ThemeToken.Panel, TextContrast),
        new(ThemeToken.TextStrong, ThemeToken.ControlOn, ThemeToken.Panel, TextContrast),
        new(ThemeToken.TipText, ThemeToken.TipBackground, ThemeToken.Panel, TextContrast),
        new(ThemeToken.Warning, ThemeToken.TipBackground, ThemeToken.Panel, TextContrast),

        // Toolbar states.
        new(ThemeToken.ControlOn, ThemeToken.Panel, null, StateContrast),
        new(ThemeToken.CellOn, ThemeToken.Panel, null, StateContrast),
        new(ThemeToken.ControlHover, ThemeToken.Panel, null, StateContrast),

        // Studio text and glyphs.
        new(ThemeToken.TextPrimary, ThemeToken.StudioCanvas, null, TextContrast),
        new(ThemeToken.TextSecondary, ThemeToken.StudioCanvas, null, TextContrast),
        new(ThemeToken.Glyph, ThemeToken.StudioCanvas, null, TextContrast),
        new(ThemeToken.TextPrimary, ThemeToken.StudioControl, ThemeToken.StudioCanvas, TextContrast),
        new(ThemeToken.TextStrong, ThemeToken.StudioControl, ThemeToken.StudioCanvas, TextContrast),
        new(ThemeToken.TextSecondary, ThemeToken.StudioControl, ThemeToken.StudioCanvas, TextContrast),
        new(ThemeToken.SelectionRing, ThemeToken.StudioCanvas, null, GlyphContrast),

        // Studio states and shapes.
        new(ThemeToken.StudioControl, ThemeToken.StudioCanvas, null, StateContrast),
        new(ThemeToken.StudioClip, ThemeToken.StudioCanvas, null, StateContrast),

        // Labels on accents.
        new(ThemeToken.OnAccent, ThemeToken.AccentBlue, null, GlyphContrast),
        new(ThemeToken.OnAccent, ThemeToken.ZoomAccent, null, GlyphContrast),
        new(ThemeToken.OnAccent, ThemeToken.TrimAccent, null, GlyphContrast),
        new(ThemeToken.OnAccent, ThemeToken.SpeedAccent, null, GlyphContrast),
    ];
}
