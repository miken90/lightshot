using System;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Tracks paused intervals in 100 ns units (HNS) so recorded sample timestamps
/// remain contiguous across pause and resume transitions.
/// </summary>
public sealed class PauseClock
{
    private long _pauseStartHns;
    private long _totalPauseDurationHns;
    private bool _isPaused;

    public bool IsPaused => _isPaused;

    public long TotalPauseDurationHns => _totalPauseDurationHns;

    public void Pause(long pauseTimestampHns)
    {
        if (_isPaused) return;
        _pauseStartHns = pauseTimestampHns;
        _isPaused = true;
    }

    public void Pause(TimeSpan timestamp) => Pause((long)(timestamp.TotalSeconds * 10_000_000.0));

    public void Resume(long resumeTimestampHns)
    {
        if (!_isPaused) return;
        if (resumeTimestampHns > _pauseStartHns)
        {
            _totalPauseDurationHns += (resumeTimestampHns - _pauseStartHns);
        }
        _isPaused = false;
    }

    public void Resume(TimeSpan timestamp) => Resume((long)(timestamp.TotalSeconds * 10_000_000.0));

    public bool ShouldDrop(long timestampHns) => _isPaused && timestampHns >= _pauseStartHns;

    public long AdjustTimestamp(long rawTimestampHns)
    {
        if (_isPaused && rawTimestampHns >= _pauseStartHns)
        {
            return _pauseStartHns - _totalPauseDurationHns;
        }
        return rawTimestampHns - _totalPauseDurationHns;
    }

    public TimeSpan AdjustTimestamp(TimeSpan timestamp)
    {
        long rawHns = (long)(timestamp.TotalSeconds * 10_000_000.0);
        long adjustedHns = AdjustTimestamp(rawHns);
        return TimeSpan.FromTicks(adjustedHns);
    }
}
