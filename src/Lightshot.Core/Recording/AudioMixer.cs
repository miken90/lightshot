// Ported from LightshotKit/Sources/LightshotKit/AudioMixer.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

public struct AudioMixer : IEquatable<AudioMixer>
{
    public enum Source
    {
        Microphone,
        Computer
    }

    private sealed class Run
    {
        public long Start { get; set; }
        public List<float> Samples { get; set; } = [];

        public Run(long start, IEnumerable<float> samples)
        {
            Start = start;
            Samples = samples.ToList();
        }
    }

    public int Channels { get; }
    public long MaxLag { get; }
    private readonly Dictionary<Source, Run> _runs = [];
    private readonly HashSet<Source> _active;
    private long? _cursor;

    public AudioMixer(int channels, IEnumerable<Source> sources, long maxLag = 96_000)
    {
        Channels = Math.Max(1, channels);
        _active = new HashSet<Source>(sources);
        MaxLag = Math.Max(0, maxLag);
    }

    public static void ApplyGain(float gain, Span<float> samples)
    {
        if (gain == 1.0f) return;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Math.Clamp(samples[i] * gain, -1.0f, 1.0f);
        }
    }

    public IReadOnlySet<Source> ActiveSources => _active;

    public void Push(Source source, float[] frames, long index)
    {
        if (!_active.Contains(source) || frames.Length == 0) return;
        _cursor ??= index;

        if (!_runs.TryGetValue(source, out Run? run))
        {
            run = new Run(index, []);
            _runs[source] = run;
        }

        long end = run.Start + (run.Samples.Count / Channels);
        if (run.Samples.Count == 0)
        {
            run.Start = index;
            run.Samples.AddRange(frames);
        }
        else if (index >= end)
        {
            int gapFrames = (int)(index - end);
            run.Samples.AddRange(new float[gapFrames * Channels]);
            run.Samples.AddRange(frames);
        }
        else
        {
            int skip = (int)(end - index) * Channels;
            if (skip < frames.Length)
            {
                run.Samples.AddRange(frames.AsSpan(skip).ToArray());
            }
        }
    }

    public void SetInactive(Source source)
    {
        _active.Remove(source);
    }

    public (long Start, float[] Frames)? Drain()
    {
        if (!_cursor.HasValue) return null;
        long cursor = _cursor.Value;

        long slowest = long.MaxValue;
        long fastest = cursor;

        foreach (Source source in _active)
        {
            long end = _runs.TryGetValue(source, out Run? r)
                ? r.Start + (r.Samples.Count / Channels)
                : cursor;
            slowest = Math.Min(slowest, end);
            fastest = Math.Max(fastest, end);
        }

        long upTo;
        int channels = Channels;
        if (_active.Count == 0)
        {
            upTo = _runs.Values.Count > 0
                ? _runs.Values.Max(r => r.Start + (r.Samples.Count / channels))
                : cursor;
        }
        else
        {
            upTo = Math.Max(slowest, fastest - MaxLag);
        }

        upTo = Math.Max(upTo, cursor);
        if (upTo <= cursor) return null;

        int count = (int)(upTo - cursor) * Channels;
        float[] mixed = new float[count];

        foreach (var (_, run) in _runs)
        {
            for (int i = 0; i < count; i++)
            {
                long frame = cursor + (i / Channels);
                int local = (int)(frame - run.Start) * Channels + (i % Channels);
                if (frame >= run.Start && local < run.Samples.Count)
                {
                    mixed[i] += run.Samples[local];
                }
            }

            int consumed = (int)Math.Min(Math.Max(upTo - run.Start, 0), run.Samples.Count / Channels);
            run.Samples.RemoveRange(0, consumed * Channels);
            run.Start = Math.Max(run.Start, upTo);
        }

        for (int i = 0; i < count; i++)
        {
            mixed[i] = Math.Clamp(mixed[i], -1.0f, 1.0f);
        }

        _cursor = upTo;
        return (cursor, mixed);
    }

    public bool Equals(AudioMixer other) =>
        Channels == other.Channels && MaxLag == other.MaxLag && _cursor == other._cursor && _active.SetEquals(other._active);

    public override bool Equals(object? obj) => obj is AudioMixer other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Channels, MaxLag, _cursor);
}
