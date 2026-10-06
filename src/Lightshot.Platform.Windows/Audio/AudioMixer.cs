// Ported for Lightshot Windows Port (Phase 7 R3)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Audio mixer coordinating microphone and system loopback streams.
/// Mixes to one AAC track by default, or two separate tracks when enabled.
/// Handles volume scaling, mono fold-down, pause suppression, and source loss.
/// </summary>
public sealed class AudioMixer : IDisposable
{
    private readonly RecordingOptions _options;
    private readonly Action<int, byte[], long, long> _writeAudioSample;
    private readonly int _targetChannels;
    private readonly bool _separateTracks;
    private readonly object _lock = new();

    private Core.AudioMixer? _coreMixer;
    private bool _hasMic;
    private bool _hasLoopback;
    private bool _paused;
    private bool _disposed;

    public bool HasMic => _hasMic;
    public bool HasLoopback => _hasLoopback;
    public bool SeparateTracks => _separateTracks;
    public int TargetChannels => _targetChannels;

    public AudioMixer(
        RecordingOptions options,
        bool hasMic,
        bool hasLoopback,
        Action<int, byte[], long, long> writeAudioSample)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _writeAudioSample = writeAudioSample ?? throw new ArgumentNullException(nameof(writeAudioSample));
        _hasMic = hasMic;
        _hasLoopback = hasLoopback;
        _separateTracks = options.SeparateAudioTracks && hasMic && hasLoopback;
        _targetChannels = options.MonoAudio ? 1 : 2;

        if (!_separateTracks && _hasMic && _hasLoopback)
        {
            var sources = new List<Core.AudioMixer.Source>
            {
                Core.AudioMixer.Source.Microphone,
                Core.AudioMixer.Source.Computer
            };
            _coreMixer = new Core.AudioMixer(_targetChannels, sources);
        }
    }

    public static IReadOnlyList<AudioTrackConfig> ResolveTrackConfigs(RecordingOptions options, bool hasMic, bool hasLoopback)
    {
        int channels = options.MonoAudio ? 1 : 2;
        int bitrate = options.MonoAudio ? 96_000 : 160_000;
        var config = new AudioTrackConfig(channels, bitrate);

        if (options.SeparateAudioTracks && hasMic && hasLoopback) return [config, config];
        if (hasMic || hasLoopback) return [config];
        return Array.Empty<AudioTrackConfig>();
    }

    public static void ApplyGain(float gain, Span<float> samples) => AudioSampleProcessor.ApplyGain(gain, samples);
    public static float[] FoldDownToMono(ReadOnlySpan<float> stereo) => AudioSampleProcessor.FoldDownToMono(stereo);
    public static float[] UpmixToStereo(ReadOnlySpan<float> mono) => AudioSampleProcessor.UpmixToStereo(mono);
    public static float[] AdjustChannels(ReadOnlySpan<float> input, int inChannels, int outChannels) =>
        AudioSampleProcessor.AdjustChannels(input, inChannels, outChannels);
    public static float[] MixFrames(ReadOnlySpan<float> mic, float micVol, ReadOnlySpan<float> loopback, float loopbackVol) =>
        AudioSampleProcessor.MixFrames(mic, micVol, loopback, loopbackVol);
    public static byte[] ToPcm16(ReadOnlySpan<float> samples) => AudioSampleProcessor.ToPcm16(samples);

    public void OnMicFrames(PcmFrames frames)
    {
        lock (_lock)
        {
            if (_disposed || _paused || !_hasMic) return;
            var adjusted = PrepareSamples(frames.Samples, frames.Channels, (float)_options.MicrophoneVolume);

            if (_separateTracks)
            {
                _writeAudioSample(0, ToPcm16(adjusted), frames.TimestampHns, frames.DurationHns);
            }
            else if (_coreMixer.HasValue)
            {
                long sampleIndex = (long)Math.Round((frames.TimestampHns / 10_000_000.0) * 48000.0);
                var mixer = _coreMixer.Value;
                mixer.Push(Core.AudioMixer.Source.Microphone, adjusted, sampleIndex);
                _coreMixer = mixer;
                DrainAndWrite(0);
            }
            else
            {
                _writeAudioSample(0, ToPcm16(adjusted), frames.TimestampHns, frames.DurationHns);
            }
        }
    }

    public void OnLoopbackFrames(PcmFrames frames)
    {
        lock (_lock)
        {
            if (_disposed || _paused || !_hasLoopback) return;
            var adjusted = PrepareSamples(frames.Samples, frames.Channels, (float)_options.ComputerAudioVolume);

            if (_separateTracks)
            {
                _writeAudioSample(1, ToPcm16(adjusted), frames.TimestampHns, frames.DurationHns);
            }
            else if (_coreMixer.HasValue)
            {
                long sampleIndex = (long)Math.Round((frames.TimestampHns / 10_000_000.0) * 48000.0);
                var mixer = _coreMixer.Value;
                mixer.Push(Core.AudioMixer.Source.Computer, adjusted, sampleIndex);
                _coreMixer = mixer;
                DrainAndWrite(0);
            }
            else
            {
                _writeAudioSample(0, ToPcm16(adjusted), frames.TimestampHns, frames.DurationHns);
            }
        }
    }

    public void OnMicLost()
    {
        lock (_lock)
        {
            _hasMic = false;
            if (_coreMixer.HasValue)
            {
                var mixer = _coreMixer.Value;
                mixer.SetInactive(Core.AudioMixer.Source.Microphone);
                _coreMixer = mixer;
                DrainAndWrite(0);
            }
        }
    }

    public void Pause() { lock (_lock) _paused = true; }
    public void Resume() { lock (_lock) _paused = false; }

    public void Flush()
    {
        lock (_lock)
        {
            if (_coreMixer.HasValue)
            {
                var mixer = _coreMixer.Value;
                mixer.SetInactive(Core.AudioMixer.Source.Microphone);
                mixer.SetInactive(Core.AudioMixer.Source.Computer);
                _coreMixer = mixer;
                DrainAndWrite(0);
            }
        }
    }

    private float[] PrepareSamples(float[] samples, int channels, float volume)
    {
        float[] adjusted = AdjustChannels(samples, channels, _targetChannels);
        ApplyGain(volume, adjusted.AsSpan());
        return adjusted;
    }

    private void DrainAndWrite(int streamIndex)
    {
        if (!_coreMixer.HasValue) return;
        var mixer = _coreMixer.Value;
        while (mixer.Drain() is { } drained)
        {
            long sampleTimeHns = (long)Math.Round((drained.Start / 48000.0) * 10_000_000.0);
            int frameCount = drained.Frames.Length / _targetChannels;
            long durationHns = (long)Math.Round((frameCount / 48000.0) * 10_000_000.0);
            _writeAudioSample(streamIndex, ToPcm16(drained.Frames), sampleTimeHns, durationHns);
        }
        _coreMixer = mixer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
    }
}
