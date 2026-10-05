// Ported from LightshotKit/Sources/LightshotKit/GIFFramePlan.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Core;

public readonly record struct GIFFramePlan(Size OutputSize, double FrameDelay, int FrameCount)
{
    public GIFFramePlan(double duration, Size sourceSize, GIFSettings settings)
        : this(
            CalculateOutputSize(sourceSize, settings.MaxWidth),
            Delay(settings.Fps),
            CalculateFrameCount(duration, Delay(settings.Fps)))
    {
    }

    private static double DelayInternal(int fps) =>
        Math.Max(2.0, Math.Round(100.0 / Math.Max(fps, 1))) / 100.0;

    public static double Delay(int fps) => DelayInternal(fps);

    private static int CalculateFrameCount(double duration, double frameDelay) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(duration, 0.0) / frameDelay - 1e-6));

    private static Size CalculateOutputSize(Size sourceSize, int? maxWidth)
    {
        if (maxWidth.HasValue && sourceSize.Width > maxWidth.Value && sourceSize.Width > 0)
        {
            double scale = (double)maxWidth.Value / sourceSize.Width;
            return new Size(maxWidth.Value, Math.Max(1.0, Math.Round(sourceSize.Height * scale)));
        }
        return new Size(Math.Max(1.0, Math.Round(sourceSize.Width)), Math.Max(1.0, Math.Round(sourceSize.Height)));
    }

    public double SampleTime(int frameIndex) => frameIndex * FrameDelay;

    public int? OutputIndex(double time, int? lastFilled)
    {
        int next = (lastFilled ?? -1) + 1;
        if (next >= FrameCount) return null;
        return time + 1e-6 >= SampleTime(next) ? next : null;
    }

    public static int BitsPerChannel(double quality) =>
        4 + (int)Math.Round(Math.Clamp(quality, 0.0, 1.0) * 4.0);
}

/// <summary>
/// The OS GIF seam: turns a finished video into a GIF.
/// </summary>
public interface IGifEncoding
{
    Task EncodeAsync(string videoPath, string outputPath, GIFSettings settings, Action<double> progress, CancellationToken cancellationToken = default);
}
