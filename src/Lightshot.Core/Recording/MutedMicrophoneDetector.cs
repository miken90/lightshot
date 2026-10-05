// Ported from LightshotKit/Sources/LightshotKit/MutedMicrophoneDetector.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Decides when to warn that the microphone might be muted.
/// </summary>
public struct MutedMicrophoneDetector : IEquatable<MutedMicrophoneDetector>
{
    public const float SilenceFloor = 0.04f;
    public const double DefaultSilentSeconds = 3.0;

    private readonly double _silentSeconds;
    private double _silentFor;
    private bool _heardSound;

    private double SilentSeconds => _silentSeconds <= 0 ? DefaultSilentSeconds : _silentSeconds;

    public bool ShowsWarning { get; private set; }

    public MutedMicrophoneDetector() : this(DefaultSilentSeconds) { }

    public MutedMicrophoneDetector(double silentSeconds)
    {
        _silentSeconds = silentSeconds <= 0 ? DefaultSilentSeconds : silentSeconds;
        _silentFor = 0;
        _heardSound = false;
        ShowsWarning = false;
    }

    public void Observe(float level, double interval, bool paused = false)
    {
        if (level > SilenceFloor)
        {
            _heardSound = true;
            ShowsWarning = false;
            return;
        }

        if (_heardSound || paused) return;

        _silentFor += interval;
        if (_silentFor + 1e-6 >= SilentSeconds)
        {
            ShowsWarning = true;
        }
    }

    public bool Equals(MutedMicrophoneDetector other) =>
        _silentSeconds.Equals(other._silentSeconds) &&
        _silentFor.Equals(other._silentFor) &&
        _heardSound == other._heardSound &&
        ShowsWarning == other.ShowsWarning;

    public override bool Equals(object? obj) => obj is MutedMicrophoneDetector other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_silentSeconds, _silentFor, _heardSound, ShowsWarning);
}
