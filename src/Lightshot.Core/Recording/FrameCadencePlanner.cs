// Ported for Lightshot Windows Port (Phase 3 Core domain)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

/// <summary>
/// Plans constant-cadence frames from a variable-rate or change-driven source (e.g. Desktop Duplication).
/// When the source is idle, repeats the last frame to preserve constant output fps.
/// </summary>
public sealed class FrameCadencePlanner
{
    public int Fps { get; }
    public double IntervalSeconds { get; }

    public FrameCadencePlanner(int fps)
    {
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps), "Fps must be positive");
        Fps = fps;
        IntervalSeconds = 1.0 / fps;
    }

    public readonly record struct PlannedFrame<T>(T Frame, double PresentationTime, bool IsRepeat);

    /// <summary>
    /// Plans output frames at target cadence for the given duration.
    /// If no new source frame is available when an output frame is due, repeats the last available frame.
    /// </summary>
    public IReadOnlyList<PlannedFrame<T>> Plan<T>(IEnumerable<(double Timestamp, T Frame)> sourceFrames, double duration)
    {
        var sorted = sourceFrames.OrderBy(s => s.Timestamp).ToList();
        if (sorted.Count == 0 || duration <= 0)
        {
            return [];
        }

        var results = new List<PlannedFrame<T>>();
        int totalFrames = Math.Max(1, (int)Math.Ceiling(duration * Fps - 1e-6));

        int sourceIndex = 0;
        T lastFrame = sorted[0].Frame;
        bool hasFrame = false;
        T? previousDelivered = default;

        for (int i = 0; i < totalFrames; i++)
        {
            double targetTime = i * IntervalSeconds;

            while (sourceIndex < sorted.Count && sorted[sourceIndex].Timestamp <= targetTime + 1e-6)
            {
                lastFrame = sorted[sourceIndex].Frame;
                hasFrame = true;
                sourceIndex++;
            }

            if (!hasFrame)
            {
                lastFrame = sorted[0].Frame;
                hasFrame = true;
            }

            bool isRepeat = results.Count > 0 && EqualityComparer<T>.Default.Equals(lastFrame, previousDelivered);
            results.Add(new PlannedFrame<T>(lastFrame, targetTime, isRepeat));
            previousDelivered = lastFrame;
        }

        return results;
    }
}
