// Ported from LightshotKit/Tests/LightshotKitTests/AppearanceTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Lightshot.App.Theming;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class ThemeContrastTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lightshot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Lightshot.slnx not found above test output directory.");
    }

    private static ResourceDictionary LoadXamlDictionary(string fileName)
    {
        string path = Path.Combine(RepoRoot(), "src", "Lightshot.App", "Theming", "Themes", fileName);
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    [Fact]
    [Unit]
    public void AllTokenPairsMeetWcag()
    {
        var lightDict = LoadXamlDictionary("Light.xaml");
        var darkDict = LoadXamlDictionary("Dark.xaml");

        var testCases = new[]
        {
            (Appearance: Appearance.Light, Dict: lightDict),
            (Appearance: Appearance.Dark, Dict: darkDict)
        };

        foreach (var (appearance, dict) in testCases)
        {
            // 1. All ThemeTokens are present as Theme.<TokenName> SolidColorBrushes
            foreach (ThemeToken token in Enum.GetValues<ThemeToken>())
            {
                string key = PaletteBridge.KeyForToken(token);
                Assert.True(dict.Contains(key), $"{appearance} dictionary is missing key {key}");
                var brush = Assert.IsType<SolidColorBrush>(dict[key]);
                Assert.NotNull(brush);

                // Verify matches PaletteBridge
                var expectedColor = PaletteBridge.ToWpfColor(ThemePalette.Color(token, appearance));
                Assert.Equal(expectedColor, brush.Color);
            }

            // 2. All legibility pairs meet WCAG requirements
            foreach (var pair in ThemePalette.LegibilityPairs)
            {
                string fgKey = PaletteBridge.KeyForToken(pair.Foreground);
                string surfaceKey = PaletteBridge.KeyForToken(pair.Surface);

                var fgBrush = (SolidColorBrush)dict[fgKey];
                var surfaceBrush = (SolidColorBrush)dict[surfaceKey];

                RGBAColor fgColor = PaletteBridge.ToRgbaColor(fgBrush.Color);
                RGBAColor surfaceColor = PaletteBridge.ToRgbaColor(surfaceBrush.Color);

                if (pair.Backdrop.HasValue)
                {
                    string backdropKey = PaletteBridge.KeyForToken(pair.Backdrop.Value);
                    var backdropBrush = (SolidColorBrush)dict[backdropKey];
                    RGBAColor backdropColor = PaletteBridge.ToRgbaColor(backdropBrush.Color);
                    surfaceColor = surfaceColor.Composited(backdropColor);
                }

                double contrast = fgColor.ContrastRatio(surfaceColor);
                Assert.True(contrast >= pair.Minimum - 0.05,
                    $"{pair} in {appearance}: contrast {contrast:F2} < minimum {pair.Minimum}");
            }
        }
    }
}
