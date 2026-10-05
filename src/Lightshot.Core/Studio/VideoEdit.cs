// Ported from LightshotKit/Sources/LightshotKit/VideoEdit.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

public struct TrimRange : IEquatable<TrimRange>
{
    public const double MinimumLength = 0.5;

    public double Start { get; private set; }
    public double End { get; private set; }
    public double Duration { get; }

    public TrimRange(double duration)
    {
        Duration = Math.Max(0.0, duration);
        Start = 0.0;
        End = Duration;
    }

    public TrimRange(double start, double end, double duration)
        : this(duration)
    {
        SetStart(start);
        SetEnd(end);
    }

    public double Length => End - Start;
    public bool IsWholeClip => Start == 0.0 && End == Duration;

    public void SetStart(double value)
    {
        double latest = Math.Max(0.0, End - Math.Min(MinimumLength, Duration));
        Start = Math.Min(Math.Max(value, 0.0), latest);
    }

    public void SetEnd(double value)
    {
        double earliest = Math.Min(Duration, Start + Math.Min(MinimumLength, Duration));
        End = Math.Max(Math.Min(value, Duration), earliest);
    }

    public bool Equals(TrimRange other) => Start.Equals(other.Start) && End.Equals(other.End) && Duration.Equals(other.Duration);
    public override bool Equals(object? obj) => obj is TrimRange other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Start, End, Duration);
}

public enum DimensionPreset
{
    Original,
    P1080,
    P720,
    P480
}

public static class DimensionPresetExtensions
{
    public static double? MaxLongestEdge(this DimensionPreset preset) => preset switch
    {
        DimensionPreset.Original => null,
        DimensionPreset.P1080 => 1920.0,
        DimensionPreset.P720 => 1280.0,
        DimensionPreset.P480 => 854.0,
        _ => null
    };
}

public static class VideoDimensions
{
    public static Size CalculateSize(DimensionPreset preset, Size source)
    {
        double? cap = preset.MaxLongestEdge();
        if (!cap.HasValue || Math.Max(source.Width, source.Height) <= cap.Value)
        {
            return Even(source);
        }

        double scale = cap.Value / Math.Max(source.Width, source.Height);
        return Even(new Size(source.Width * scale, source.Height * scale));
    }

    public static Size CalculateSize(double? width, double? height, Size source)
    {
        double aspect = source.Height > 0 ? source.Width / source.Height : 1.0;
        Size result;

        if (width.HasValue && height.HasValue)
        {
            double scale = Math.Min(width.Value / Math.Max(source.Width, 1.0), height.Value / Math.Max(source.Height, 1.0));
            result = new Size(source.Width * scale, source.Height * scale);
        }
        else if (width.HasValue)
        {
            result = new Size(width.Value, width.Value / aspect);
        }
        else if (height.HasValue)
        {
            result = new Size(height.Value * aspect, height.Value);
        }
        else
        {
            result = source;
        }

        result = new Size(
            Math.Min(Math.Max(result.Width, 2.0), Math.Max(source.Width, 2.0)),
            Math.Min(Math.Max(result.Height, 2.0), Math.Max(source.Height, 2.0))
        );

        return Even(result);
    }

    private static Size Even(Size size) =>
        new(
            Math.Max(2.0, Math.Floor(size.Width / 2.0) * 2.0),
            Math.Max(2.0, Math.Floor(size.Height / 2.0) * 2.0)
        );
}

public abstract record AudioEdit
{
    public sealed record Unchanged : AudioEdit
    {
        public static readonly Unchanged Instance = new();
    }

    public sealed record Mute : AudioEdit
    {
        public static readonly Mute Instance = new();
    }

    public sealed record Volume(double Value) : AudioEdit;

    public sealed record Mono : AudioEdit
    {
        public static readonly Mono Instance = new();
    }

    public sealed record Remove : AudioEdit
    {
        public static readonly Remove Instance = new();
    }
}

public record VideoEditSettings
{
    public TrimRange Trim { get; set; }
    public Size Dimensions { get; set; }
    public double Quality { get; set; }
    public AudioEdit Audio { get; set; }

    public VideoEditSettings(TrimRange trim, Size dimensions, double quality = VideoBitRate.DefaultQuality, AudioEdit? audio = null)
    {
        Trim = trim;
        Dimensions = dimensions;
        Quality = Math.Clamp(quality, 0.0, 1.0);
        Audio = audio ?? AudioEdit.Unchanged.Instance;
    }
}

public static class VideoBitRate
{
    public const double DefaultQuality = 0.5;
    public const double MinimumBitsPerSecond = 500_000.0;
    public const double AudioBitsPerSecondPerChannel = 64_000.0;

    public static double BitsPerPixel(double quality)
    {
        double q = Math.Clamp(quality, 0.0, 1.0);
        return q <= DefaultQuality
            ? 0.025 + (0.1 - 0.025) * (q / DefaultQuality)
            : 0.1 + (0.2 - 0.1) * ((q - DefaultQuality) / (1.0 - DefaultQuality));
    }

    public static double VideoBitsPerSecond(Size size, double fps, double quality) =>
        Math.Max(MinimumBitsPerSecond, size.Width * size.Height * Math.Max(1.0, fps) * BitsPerPixel(quality));
}

public static class SizeEstimator
{
    public const double ContainerOverheadBytes = 4096.0;

    public static double EstimatedBytes(VideoEditSettings settings, double fps, int audioChannels)
    {
        double seconds = settings.Trim.Length;
        double video = VideoBitRate.VideoBitsPerSecond(settings.Dimensions, fps, settings.Quality);
        int channels = settings.Audio switch
        {
            AudioEdit.Remove => 0,
            AudioEdit.Mono => Math.Min(audioChannels, 1),
            _ => audioChannels
        };
        double audio = channels * VideoBitRate.AudioBitsPerSecondPerChannel;
        return (video + audio) * seconds / 8.0 + ContainerOverheadBytes;
    }

    public static double EstimatedTrimOnlyBytes(double sourceBytes, TrimRange trim)
    {
        if (trim.Duration <= 0) return sourceBytes;
        return sourceBytes * (trim.Length / trim.Duration);
    }
}
