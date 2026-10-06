// Lightshot Windows Port
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Drift-tracking and jitter-smoothing filter for loopback and microphone audio clocks.
/// Implements Spike B gate decision: tracks render endpoint clock (IAudioClock) and eliminates delivery jitter.
/// </summary>
public sealed class AudioClockDriftFilter
{
    private readonly object _lock = new();
    private readonly double _alpha;
    private readonly long _discontinuityThresholdHns;

    private bool _initialized;
    private bool _hasRenderClock;
    private long _lastOutputHns;
    private double _smoothedOffsetHns;

    public bool IsInitialized
    {
        get { lock (_lock) return _initialized; }
    }

    public bool HasRenderClock
    {
        get { lock (_lock) return _hasRenderClock; }
    }

    public double SmoothedOffsetHns
    {
        get { lock (_lock) return _smoothedOffsetHns; }
    }

    public long LastOutputHns
    {
        get { lock (_lock) return _lastOutputHns; }
    }

    public AudioClockDriftFilter(double alpha = 0.05, long discontinuityThresholdHns = 2_000_000)
    {
        _alpha = Math.Clamp(alpha, 0.001, 1.0);
        _discontinuityThresholdHns = Math.Max(100_000, discontinuityThresholdHns); // at least 10 ms
        Reset();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _initialized = false;
            _hasRenderClock = false;
            _lastOutputHns = 0;
            _smoothedOffsetHns = 0;
        }
    }

    /// <summary>
    /// Feeds a reading from the render endpoint's IAudioClock to establish the reference timeline.
    /// </summary>
    public void UpdateRenderClock(ulong devicePosition, ulong qpcPositionHns, ulong frequency)
    {
        if (frequency == 0) return;

        lock (_lock)
        {
            long renderTimeHns = (long)((devicePosition * 10_000_000.0) / frequency);
            double offset = renderTimeHns - (long)qpcPositionHns;

            if (!_hasRenderClock)
            {
                _smoothedOffsetHns = offset;
                _hasRenderClock = true;
            }
            else
            {
                double error = offset - _smoothedOffsetHns;
                if (Math.Abs(error) > _discontinuityThresholdHns)
                {
                    _smoothedOffsetHns = offset;
                }
                else
                {
                    _smoothedOffsetHns += _alpha * error;
                }
            }
        }
    }

    /// <summary>
    /// Smooths a packet's timestamp against the filtered timeline, ensuring strictly monotonic presentation times.
    /// </summary>
    public long SmoothTimestamp(long rawTimestampHns, long expectedDurationHns)
    {
        lock (_lock)
        {
            long adjusted = rawTimestampHns + (_hasRenderClock ? (long)_smoothedOffsetHns : 0);

            if (!_initialized)
            {
                _initialized = true;
                _lastOutputHns = adjusted;
                return _lastOutputHns;
            }

            long expectedHns = _lastOutputHns + expectedDurationHns;
            long error = adjusted - expectedHns;

            if (Math.Abs(error) > _discontinuityThresholdHns)
            {
                // Discontinuity (gap, seek, mode change): snap to adjusted timestamp
                _lastOutputHns = Math.Max(adjusted, _lastOutputHns);
            }
            else
            {
                // Exponential smoothing of jitter
                long smoothed = expectedHns + (long)(_alpha * error);
                _lastOutputHns = Math.Max(smoothed, _lastOutputHns);
            }

            return _lastOutputHns;
        }
    }
}
