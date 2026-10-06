// Ported from App/Sources/MicrophoneCapture.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Thread-safe holder for the latest microphone/input level in [0, 1].
/// Read by the UI recording pill and feeds Core MutedMicrophoneDetector.
/// </summary>
public sealed class AudioLevelMeter
{
    private readonly object _lock = new();
    private float _level;

    /// <summary>
    /// Current normalized audio level in range [0, 1].
    /// </summary>
    public float Level
    {
        get
        {
            lock (_lock) return _level;
        }
        set
        {
            lock (_lock) _level = Math.Clamp(value, 0f, 1f);
        }
    }

    public AudioLevelMeter()
    {
        _level = 0f;
    }

    /// <summary>
    /// Updates the level from measured PCM frames.
    /// </summary>
    public void Update(PcmFrames frames)
    {
        if (frames == null) return;
        Level = frames.Level;
    }

    /// <summary>
    /// Updates the level directly with a normalized float value.
    /// </summary>
    public void Update(float rawLevel)
    {
        Level = rawLevel;
    }

    /// <summary>
    /// Resets the level to zero.
    /// </summary>
    public void Reset()
    {
        lock (_lock) _level = 0f;
    }

    /// <summary>
    /// Feeds the current level into a Core MutedMicrophoneDetector.
    /// </summary>
    public void ObserveInto(ref MutedMicrophoneDetector detector, double intervalSeconds, bool paused = false)
    {
        detector.Observe(Level, intervalSeconds, paused);
    }
}
