// Ported from App/Sources/MicrophoneCapture.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Threading;
using Lightshot.Platform.Windows.Recording;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Microphone capture using shared-mode WASAPI at 48 kHz float.
/// Stamped from hardware device position and QPC position before contiguity alignment.
/// Automatically falls back to silence frames if hardware microphone is missing.
/// </summary>
public sealed class WasapiMicrophone : IDisposable
{
    private readonly MMDevice? _device;
    private readonly int _channels;
    private readonly double _volume;
    private readonly Action<PcmFrames>? _onFrames;
    private readonly Action<float>? _onLevel;
    private readonly Action? _onLost;
    private readonly AudioLevelMeter? _levelMeter;
    private readonly PcmContiguityTracker _contiguityTracker;

    private WasapiRecorder? _recorder;
    private WaveFormat? _inputFormat;
    private Thread? _silenceThread;

    private bool _recording;
    private bool _paused;
    private long _startQpcHns;
    private long _pauseStartTicks;
    private long _totalPauseTicks;
    private bool _disposed;

    public bool IsAvailable => _device != null;
    public string DeviceName => _device?.FriendlyName ?? "Unavailable";
    public string? DeviceId => _device?.ID;
    public int Channels => _channels;

    public WasapiMicrophone(
        string? deviceId = null,
        bool mono = false,
        double volume = 1.0,
        Action<PcmFrames>? onFrames = null,
        Action<float>? onLevel = null,
        Action? onLost = null,
        AudioLevelMeter? levelMeter = null)
    {
        _channels = mono ? 1 : 2;
        _volume = volume;
        _onFrames = onFrames;
        _onLevel = onLevel;
        _onLost = onLost;
        _levelMeter = levelMeter;
        _contiguityTracker = new PcmContiguityTracker(_channels, 48000.0);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!string.IsNullOrEmpty(deviceId))
            {
                try { _device = enumerator.GetDevice(deviceId); }
                catch { _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); }
            }
            else
            {
                _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }
        }
        catch { _device = null; }
    }

    public void Start()
    {
        if (_recording) return;
        _startQpcHns = QpcClock.ToHns(Stopwatch.GetTimestamp());
        _totalPauseTicks = 0;
        _contiguityTracker.Reset();

        if (_device == null) { StartSilencePump(); return; }

        try
        {
            var builder = new WasapiRecorderBuilder().WithDevice(_device).WithSharedMode();
            _recorder = builder.BuildAsync().GetAwaiter().GetResult();
            _inputFormat = _recorder.WaveFormat;
            _recorder.DataAvailable += HandleRecorderDataAvailable;
            _recorder.StartRecording();
            _recording = true;
        }
        catch { StartSilencePump(); }
    }

    private void StartSilencePump()
    {
        _recording = true;
        _silenceThread = new Thread(SilencePump) { IsBackground = true, Name = "dev.lightshot.mic-silence" };
        _silenceThread.Start();
    }

    private void SilencePump()
    {
        long producedSamples = 0;
        long startTick = Stopwatch.GetTimestamp();

        while (_recording)
        {
            if (!_paused)
            {
                long nowTick = Stopwatch.GetTimestamp();
                long pauseTicks = _totalPauseTicks;
                double elapsedSec = Math.Max(0, (nowTick - startTick - pauseTicks) / (double)Stopwatch.Frequency);
                long targetSamples = (long)Math.Round(elapsedSec * 48000.0);

                long neededSamples = targetSamples - producedSamples;
                if (neededSamples >= 480)
                {
                    int samplesToEmit = (int)Math.Min(neededSamples, 4800);
                    long timestampHns = (long)Math.Round((producedSamples / 48000.0) * 10_000_000.0);
                    var frames = PcmFrames.CreateSilence(samplesToEmit, _channels, 48000.0, timestampHns);
                    _onFrames?.Invoke(frames);
                    producedSamples += samplesToEmit;
                }
            }
            Thread.Sleep(5);
        }

        long finalNowTick = Stopwatch.GetTimestamp();
        long finalPauseTicks = _totalPauseTicks;
        double finalElapsedSec = Math.Max(0, (finalNowTick - startTick - finalPauseTicks) / (double)Stopwatch.Frequency);
        long finalTargetSamples = (long)Math.Round(finalElapsedSec * 48000.0);
        long finalRemaining = finalTargetSamples - producedSamples;
        if (finalRemaining > 0)
        {
            long timestampHns = (long)Math.Round((producedSamples / 48000.0) * 10_000_000.0);
            var frames = PcmFrames.CreateSilence((int)finalRemaining, _channels, 48000.0, timestampHns);
            _onFrames?.Invoke(frames);
        }
    }

    private void HandleRecorderDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (_paused || buffer.IsEmpty || _inputFormat == null) return;
        try
        {
            byte[] rawBytes = buffer.ToArray();
            if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(rawBytes, 0, rawBytes.Length);

            float[] samples = PcmContiguityTracker.ConvertToFloatSamples(rawBytes, _inputFormat, _channels, 48000.0);
            if (samples.Length == 0) return;

            var rawFrames = new PcmFrames(samples, _channels, 48000.0, 0);
            float level = rawFrames.Level;
            _levelMeter?.Update(level);
            _onLevel?.Invoke(level);

            rawFrames.ApplyGain((float)_volume);

            long pauseHns = QpcClock.ToHns(_totalPauseTicks);
            long rawTimestampHns = qpcPosition != 0
                ? qpcPosition - pauseHns - _startQpcHns
                : QpcClock.ToHns(Stopwatch.GetTimestamp()) - pauseHns - _startQpcHns;
            if (rawTimestampHns < 0) rawTimestampHns = 0;

            var aligned = _contiguityTracker.Align(rawFrames.Samples, rawTimestampHns);
            _onFrames?.Invoke(aligned);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Microphone buffer processing error: {ex.Message}");
        }
    }

    public void Pause()
    {
        if (!_recording || _paused) return;
        _paused = true;
        _pauseStartTicks = Stopwatch.GetTimestamp();
    }

    public void Resume()
    {
        if (!_recording || !_paused) return;
        _paused = false;
        _totalPauseTicks += Stopwatch.GetTimestamp() - _pauseStartTicks;
    }

    public void Stop()
    {
        if (!_recording) return;
        _recording = false;
        try { _silenceThread?.Join(500); } catch { }
        _silenceThread = null;
        try { _recorder?.StopRecording(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _recorder?.Dispose();
        _recorder = null;
        _device?.Dispose();
    }
}
