// Ported from LightshotKit/Tests/LightshotKitTests/AppearanceTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class AppearanceTests
{
    [Fact]
    [Unit]
    public void APreferenceResolvesToThePinnedAppearanceOrTheSystems()
    {
        foreach (AppearancePreference preference in Enum.GetValues<AppearancePreference>())
        {
            foreach (Appearance system in Enum.GetValues<Appearance>())
            {
                Appearance expected = preference switch
                {
                    AppearancePreference.System => system,
                    AppearancePreference.Light => Appearance.Light,
                    AppearancePreference.Dark => Appearance.Dark,
                    _ => system
                };
                Assert.Equal(expected, preference.Resolved(system));
            }
        }
    }

    [Fact]
    [Unit]
    public void AMissingOrUnknownStoredPreferenceMatchesTheSystem()
    {
        Assert.Equal(AppearancePreference.System, AppearancePreferenceExtensions.FromStoredValue(null));
        Assert.Equal(AppearancePreference.System, AppearancePreferenceExtensions.FromStoredValue("sepia"));
        Assert.Equal(AppearancePreference.Dark, AppearancePreferenceExtensions.FromStoredValue("dark"));
        Assert.Equal(AppearancePreference.Light, AppearancePreferenceExtensions.FromStoredValue("light"));

        var titles = Enum.GetValues<AppearancePreference>()
            .Select(p => p.Title())
            .ToList();
        Assert.Equal(["Match System", "Light", "Dark"], titles);
    }

    [Fact]
    [Unit]
    public void ContrastRatioMatchesWCAGKnownValues()
    {
        var white = new RGBAColor(1, 1, 1);
        var black = RGBAColor.Black;
        Assert.True(Math.Abs(white.ContrastRatio(black) - 21) < 0.001);
        Assert.True(Math.Abs(black.ContrastRatio(white) - 21) < 0.001);
        Assert.True(Math.Abs(RGBAColor.Red.ContrastRatio(RGBAColor.Red) - 1) < 0.001);

        var grey = new RGBAColor(0x77 / 255.0, 0x77 / 255.0, 0x77 / 255.0);
        Assert.True(Math.Abs(grey.ContrastRatio(white) - 4.48) < 0.01);

        var halfBlack = new RGBAColor(0, 0, 0, 0.5);
        Assert.Equal(new RGBAColor(0.5, 0.5, 0.5), halfBlack.Composited(white));
        Assert.True(Math.Abs(halfBlack.ContrastRatio(white) - new RGBAColor(0.5, 0.5, 0.5).ContrastRatio(white)) < 0.001);
    }

    [Fact]
    [Unit]
    public void EveryLegibilityPairMeetsItsContrastInBothAppearances()
    {
        foreach (Appearance appearance in Enum.GetValues<Appearance>())
        {
            foreach (var pair in ThemePalette.LegibilityPairs)
            {
                double contrast = pair.Contrast(appearance);
                Assert.True(contrast >= pair.Minimum, $"{pair} in {appearance}: {contrast} < {pair.Minimum}");
            }
        }
    }

    [Fact]
    [Unit]
    public void EverySurfaceTheChromeDrawsOnIsCheckedForText()
    {
        var surfaces = ThemePalette.LegibilityPairs
            .Where(p => p.Minimum >= ThemePalette.TextContrast)
            .Select(p => p.Surface)
            .ToHashSet();

        ThemeToken[] required = [
            ThemeToken.Panel, ThemeToken.Well, ThemeToken.TipBackground,
            ThemeToken.StudioCanvas, ThemeToken.StudioControl
        ];
        foreach (var req in required)
        {
            Assert.Contains(req, surfaces);
        }
    }

    [Fact]
    [Unit]
    public void DarkIsTheChromeLightshotAlwaysDrew()
    {
        static RGBAColor White(double a) => new(1, 1, 1, a);
        static RGBAColor Black(double a) => new(0, 0, 0, a);

        var before = new Dictionary<ThemeToken, RGBAColor>
        {
            [ThemeToken.Panel] = new(0.17, 0.19, 0.22),
            [ThemeToken.PanelEdge] = White(0.08),
            [ThemeToken.PanelEdgeStrong] = White(0.18),
            [ThemeToken.PanelShadow] = Black(0.3),
            [ThemeToken.GridLine] = White(0.09),
            [ThemeToken.ControlHover] = White(0.1),
            [ThemeToken.ControlOn] = White(0.14),
            [ThemeToken.ControlHoverEdge] = White(0.18),
            [ThemeToken.CellHover] = White(0.07),
            [ThemeToken.CellOn] = Black(0.3),
            [ThemeToken.CellOnHover] = Black(0.4),
            [ThemeToken.Well] = Black(0.35),
            [ThemeToken.RowHover] = White(0.08),
            [ThemeToken.HelpFill] = White(0.22),
            [ThemeToken.TipBackground] = Black(0.85),
            [ThemeToken.TipText] = White(1),
            [ThemeToken.TipEdge] = White(0.15),
            [ThemeToken.TextPrimary] = White(1),
            [ThemeToken.TextStrong] = White(0.85),
            [ThemeToken.Glyph] = White(0.75),
            [ThemeToken.GlyphOff] = White(0.6),
            [ThemeToken.TextSecondary] = White(0.55),
            [ThemeToken.TextTertiary] = White(0.5),
            [ThemeToken.TextDisabled] = White(0.35),
            [ThemeToken.StudioCanvas] = new(0.12, 0.12, 0.12),
            [ThemeToken.StudioDivider] = White(0.08),
            [ThemeToken.StudioControl] = White(0.08),
            [ThemeToken.StudioClip] = new(0.25, 0.27, 0.32),
            [ThemeToken.ClipEdge] = White(0.15),
            [ThemeToken.SelectionRing] = White(1),
            [ThemeToken.PreviewShadow] = Black(0.4),
            [ThemeToken.AccentBlue] = new(0.04, 0.52, 1),
            [ThemeToken.Playhead] = new(1, 0.27, 0.23),
            [ThemeToken.TimelineSelection] = new(1, 0.8, 0.2),
            [ThemeToken.ZoomAccent] = new(0.45, 0.35, 0.95),
            [ThemeToken.TextAccent] = new(0.95, 0.55, 0.2),
            [ThemeToken.StudioPink] = new(0.95, 0.4, 0.6),
            [ThemeToken.StudioViolet] = new(0.6, 0.45, 0.95)
        };

        foreach (var (token, color) in before)
        {
            Assert.Equal(color, ThemePalette.Color(token, Appearance.Dark));
        }
    }

    [Fact]
    [Unit]
    public void AccentsKeepTheirHueInBothAppearances()
    {
        ThemeToken[] tokens = [
            ThemeToken.AccentBlue, ThemeToken.Playhead, ThemeToken.TimelineSelection,
            ThemeToken.ZoomAccent, ThemeToken.TextAccent, ThemeToken.TrimAccent,
            ThemeToken.SpeedAccent, ThemeToken.StudioPink, ThemeToken.StudioViolet,
            ThemeToken.OnAccent
        ];

        foreach (var token in tokens)
        {
            Assert.Equal(ThemePalette.Color(token, Appearance.Light), ThemePalette.Color(token, Appearance.Dark));
        }
    }

    [Fact]
    [Unit]
    public void LightChromeIsLightAndDarkChromeIsDark()
    {
        ThemeToken[] surfaces = [ThemeToken.Panel, ThemeToken.StudioCanvas];
        foreach (var surface in surfaces)
        {
            Assert.True(ThemePalette.Color(surface, Appearance.Light).RelativeLuminance() > 0.7, surface.ToString());
            Assert.True(ThemePalette.Color(surface, Appearance.Dark).RelativeLuminance() < 0.05, surface.ToString());
        }
    }
}
