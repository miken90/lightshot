// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

public sealed record GradientPreset(string Name, RGBAColor From, RGBAColor To, double AngleDegrees = 135);

public static class GradientPresets
{
    public static IReadOnlyList<GradientPreset> All { get; } =
    [
        new("Sunset", new RGBAColor(1.0, 0.4, 0.2), new RGBAColor(0.6, 0.1, 0.5)),
        new("Ocean", new RGBAColor(0.1, 0.6, 0.9), new RGBAColor(0.05, 0.2, 0.6)),
        new("Emerald", new RGBAColor(0.1, 0.8, 0.5), new RGBAColor(0.05, 0.4, 0.3)),
        new("Lavender", new RGBAColor(0.7, 0.5, 0.9), new RGBAColor(0.35, 0.2, 0.6)),
        new("Warm Flame", new RGBAColor(1.0, 0.6, 0.2), new RGBAColor(0.9, 0.2, 0.2)),
        new("Night Fade", new RGBAColor(0.6, 0.65, 0.8), new RGBAColor(0.15, 0.2, 0.35)),
        new("Spring Warmth", new RGBAColor(0.95, 0.75, 0.7), new RGBAColor(0.95, 0.55, 0.6)),
        new("Juicy Peach", new RGBAColor(1.0, 0.7, 0.6), new RGBAColor(0.95, 0.45, 0.55)),
        new("Deep Blue", new RGBAColor(0.2, 0.3, 0.8), new RGBAColor(0.1, 0.1, 0.3)),
        new("Neon Glow", new RGBAColor(0.0, 0.8, 0.9), new RGBAColor(0.8, 0.1, 0.8)),
        new("Sunfire", new RGBAColor(1.0, 0.85, 0.2), new RGBAColor(1.0, 0.3, 0.1)),
        new("Charcoal", new RGBAColor(0.35, 0.35, 0.4), new RGBAColor(0.1, 0.1, 0.15))
    ];

    public static GradientPreset At(int index)
    {
        if (All.Count == 0)
        {
            throw new InvalidOperationException("No gradient presets defined.");
        }
        int clamped = Math.Clamp(index, 0, All.Count - 1);
        return All[clamped];
    }
}
