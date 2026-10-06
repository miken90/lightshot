// Ported for Lightshot Windows Port (Phase 7 R2)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Drives constant-cadence frame delivery over Core FrameCadencePlanner.
/// When the underlying frame source is idle (e.g. desktop duplication timeout with no screen change),
/// repeats the last available frame to maintain constant output fps.
/// </summary>
public sealed class CadenceDriver<T>
{
    private readonly FrameCadencePlanner _planner;
    private T? _lastFrame;
    private bool _hasFrame;
    private bool _isNewFrame;
    private long _nextFrameIndex;

    public int Fps => _planner.Fps;
    public double IntervalSeconds => _planner.IntervalSeconds;
    public long FrameDurationHns { get; }

    public int TotalFramesDelivered { get; private set; }
    public int RepeatedFramesCount { get; private set; }
    public T? CurrentFrame => _lastFrame;
    public long NextFrameIndex => _nextFrameIndex;

    public CadenceDriver(int fps)
    {
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps), "Fps must be positive.");
        _planner = new FrameCadencePlanner(fps);
        FrameDurationHns = (long)Math.Round(10_000_000.0 / fps);
    }

    public CadenceDriver(FrameCadencePlanner planner)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        FrameDurationHns = (long)Math.Round(10_000_000.0 / planner.Fps);
    }

    /// <summary>
    /// Submits a newly acquired source frame to the driver.
    /// </summary>
    public void SubmitFrame(T frame)
    {
        _lastFrame = frame;
        _hasFrame = true;
        _isNewFrame = true;
    }

    /// <summary>
    /// Emits the next scheduled cadence frame if at least one source frame has been received.
    /// Returns true if emitted; false if no frame is available yet.
    /// </summary>
    public bool TryEmitNextFrame(Action<T, long, long, bool> onEmit)
    {
        ArgumentNullException.ThrowIfNull(onEmit);

        if (!_hasFrame || _lastFrame == null) return false;

        long sampleTimeHns = _nextFrameIndex * FrameDurationHns;
        bool isRepeat = !_isNewFrame;
        _isNewFrame = false;

        TotalFramesDelivered++;
        if (isRepeat) RepeatedFramesCount++;

        _nextFrameIndex++;
        onEmit(_lastFrame, sampleTimeHns, FrameDurationHns, isRepeat);
        return true;
    }

    /// <summary>
    /// Emits all frames due within the elapsed duration [0, elapsedDurationHns).
    /// </summary>
    public int EmitDueFrames(long elapsedDurationHns, Action<T, long, long, bool> onEmit)
    {
        ArgumentNullException.ThrowIfNull(onEmit);

        int totalDueFrames = (int)Math.Floor(elapsedDurationHns * (double)_planner.Fps / 10_000_000.0 + 1e-6);
        int emitted = 0;
        while (_hasFrame && _lastFrame != null && _nextFrameIndex < totalDueFrames)
        {
            if (TryEmitNextFrame(onEmit))
            {
                emitted++;
            }
        }
        return emitted;
    }

    /// <summary>
    /// Plans batch frames over a duration using Core FrameCadencePlanner.
    /// </summary>
    public IReadOnlyList<FrameCadencePlanner.PlannedFrame<T>> PlanBatch(
        IEnumerable<(double Timestamp, T Frame)> sourceFrames,
        double durationSeconds)
    {
        return _planner.Plan(sourceFrames, durationSeconds);
    }

    /// <summary>
    /// Resets the cadence driver state for a new recording take.
    /// </summary>
    public void Reset()
    {
        _lastFrame = default;
        _hasFrame = false;
        _isNewFrame = false;
        _nextFrameIndex = 0;
        TotalFramesDelivered = 0;
        RepeatedFramesCount = 0;
    }
}
