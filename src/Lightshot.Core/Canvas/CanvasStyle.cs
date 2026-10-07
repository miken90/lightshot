// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

public enum CanvasSizeMode { Aspect, FixedSize }

public enum AspectPreset { Auto, Square, FourThree, ThreeTwo, SixteenNine, FiveThree, NineSixteen, ThreeFour, TwoThree }

public static class AspectPresets
{
    // Width / height; Auto has no ratio.
    public static double? Ratio(AspectPreset p) => p switch
    {
        AspectPreset.Square => 1.0,
        AspectPreset.FourThree => 4.0 / 3,
        AspectPreset.ThreeTwo => 3.0 / 2,
        AspectPreset.SixteenNine => 16.0 / 9,
        AspectPreset.FiveThree => 5.0 / 3,
        AspectPreset.NineSixteen => 9.0 / 16,
        AspectPreset.ThreeFour => 3.0 / 4,
        AspectPreset.TwoThree => 2.0 / 3,
        _ => null
    };
    public static string Label(AspectPreset p) => p switch
    {
        AspectPreset.Auto => "Auto", AspectPreset.Square => "1:1", AspectPreset.FourThree => "4:3",
        AspectPreset.ThreeTwo => "3:2", AspectPreset.SixteenNine => "16:9", AspectPreset.FiveThree => "5:3",
        AspectPreset.NineSixteen => "9:16", AspectPreset.ThreeFour => "3:4", _ => "2:3"
    };
}

public enum CanvasFillKind { Solid, Gradient, AutoEdge, Image }

public sealed record CanvasFill(CanvasFillKind Kind = CanvasFillKind.Gradient, RGBAColor? Color = null, int GradientIndex = 0, string? ImagePath = null);

public sealed record CanvasStyle(
    bool Enabled = false,
    CanvasSizeMode SizeMode = CanvasSizeMode.Aspect,
    AspectPreset Aspect = AspectPreset.Auto,
    int TargetWidth = 1920,
    int TargetHeight = 1080,
    bool DownscaleToFit = false,
    CanvasFill? Fill = null,
    double Padding = 40,
    double CornerRadius = 12,
    double Shadow = 20)
{
    public CanvasFill EffectiveFill => Fill ?? new CanvasFill();

    public static readonly IReadOnlyList<(string Label, int Width, int Height)> FixedPresets =
    [
        ("1920 x 1080 (FHD)", 1920, 1080),
        ("2560 x 1440 (2K)", 2560, 1440),
        ("3840 x 2160 (4K)", 3840, 2160),
        ("1200 x 630 (Social card)", 1200, 630),
        ("1080 x 1080 (Square)", 1080, 1080),
        ("1080 x 1920 (Vertical)", 1080, 1920)
    ];
}
