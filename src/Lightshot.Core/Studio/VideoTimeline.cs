// Ported from LightshotKit/Sources/LightshotKit/VideoTimeline.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

public readonly record struct TimelineScale(double Duration, double Width)
{
    public static readonly IReadOnlyList<double> TickSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];

    public double X(double time)
    {
        if (Duration <= 0) return 0;
        return Math.Min(Math.Max(time, 0.0), Duration) / Duration * Width;
    }

    public double Time(double x)
    {
        if (Width <= 0) return 0;
        return Math.Min(Math.Max(x, 0.0), Width) / Width * Duration;
    }

    public double TickStep(double minimumSpacing = 72)
    {
        if (Duration <= 0 || Width <= 0) return TickSteps[0];
        double pointsPerSecond = Width / Duration;
        return TickSteps.FirstOrDefault(s => s * pointsPerSecond >= minimumSpacing, TickSteps[^1]);
    }

    public IReadOnlyList<double> Ticks(double minimumSpacing = 72)
    {
        double step = TickStep(minimumSpacing);
        var result = new List<double>();
        for (double t = 0; t <= Duration + 1e-9; t += step)
        {
            result.Add(t);
        }
        return result;
    }

    public static string Label(double time)
    {
        int total = (int)Math.Floor(Math.Max(time, 0.0));
        int hours = total / 3600;
        int minutes = (total / 60) % 60;
        int seconds = total % 60;

        if (hours > 0)
        {
            return $"{hours}:{minutes:D2}:{seconds:D2}";
        }
        return $"{minutes:D2}:{seconds:D2}";
    }

    public static IReadOnlyList<int> FilmstripFrames(int slots, int frames)
    {
        if (slots <= 0 || frames <= 0) return [];
        return Enumerable.Range(0, slots)
            .Select(slot => Math.Min(frames - 1, (int)(((double)slot + 0.5) / slots * frames)))
            .ToList();
    }
}
