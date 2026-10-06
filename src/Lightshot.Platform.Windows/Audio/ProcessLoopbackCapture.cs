// Lightshot Windows Port
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using Lightshot.Platform.Windows.Recording;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Loopback capture that excludes Lightshot's own process audio tree.
/// Stamped against the render endpoint clock with the drift filter per Spike B gate decision.
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    private readonly uint _targetPidToExclude;
    private readonly AudioClockDriftFilter _driftFilter;
    private readonly AudioLevelMeter? _levelMeter;
    private readonly PcmContiguityTracker _contiguityTracker;

    private MMDevice? _renderDevice;
    private AudioClient? _renderClient;
    private AudioClockClient? _renderClock;
    private WasapiRecorder? _recorder;
    private WaveFormat? _inputFormat;

    private bool _recording;
    private bool _paused;
    private long _startQpcHns;
    private long _pauseStartTicks;
    private long _totalPauseTicks;
    private bool _disposed;

    public uint ExcludedProcessId => _targetPidToExclude;
    public string EndpointName => _renderDevice?.FriendlyName ?? "Default Speakers";
    public AudioClockDriftFilter DriftFilter => _driftFilter;

    public event Action<PcmFrames>? OnAudioSample;
    public event Action<float>? OnLevel;

    public ProcessLoopbackCapture(
        uint? targetPidToExclude = null,
        AudioClockDriftFilter? driftFilter = null,
        AudioLevelMeter? levelMeter = null)
    {
        _targetPidToExclude = targetPidToExclude ?? (uint)Environment.ProcessId;
        _driftFilter = driftFilter ?? new AudioClockDriftFilter();
        _levelMeter = levelMeter;
        _contiguityTracker = new PcmContiguityTracker(2, 48000.0);

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _renderClient = _renderDevice?.CreateAudioClient();
            _renderClock = _renderClient?.AudioClockClient;
        }
        catch
        {
            _renderDevice = null;
            _renderClient = null;
            _renderClock = null;
        }
    }

    public void Start()
    {
        if (_recording) return;

        _startQpcHns = QpcClock.ToHns(Stopwatch.GetTimestamp());
        _totalPauseTicks = 0;
        _driftFilter.Reset();
        _contiguityTracker.Reset();

        var builder = new WasapiRecorderBuilder()
            .WithProcessLoopback(_targetPidToExclude, ProcessLoopbackMode.ExcludeTargetProcessTree);

        _recorder = builder.BuildAsync().GetAwaiter().GetResult();
        _inputFormat = _recorder.WaveFormat;
        _recorder.DataAvailable += HandleRecorderDataAvailable;
        _recorder.StartRecording();
        _recording = true;
    }

    private void HandleRecorderDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (_paused || buffer.IsEmpty || _inputFormat == null) return;

        try
        {
            byte[] rawBytes = buffer.ToArray();
            if ((flags & AudioClientBufferFlags.Silent) != 0)
            {
                Array.Clear(rawBytes, 0, rawBytes.Length);
            }

            // Convert to 48 kHz stereo float samples
            float[] samples = PcmContiguityTracker.ConvertToFloatSamples(rawBytes, _inputFormat, 2, 48000.0);
            if (samples.Length == 0) return;

            int sampleCount = samples.Length / 2;
            long durationHns = (long)((sampleCount / 48000.0) * 10_000_000.0);

            // Sample render endpoint clock if available
            if (_renderClock != null)
            {
                try
                {
                    ulong renderDevicePos = (ulong)_renderClock.AdjustedPosition;
                    ulong renderFreq = (ulong)_renderClock.Frequency;
                    if (renderFreq > 0)
                    {
                        ulong renderQpc = (ulong)QpcClock.ToHns(Stopwatch.GetTimestamp());
                        _driftFilter.UpdateRenderClock(renderDevicePos, renderQpc, renderFreq);
                    }
                }
                catch { }
            }

            // QPC timestamp minus pause offset
            long pauseHns = QpcClock.ToHns(_totalPauseTicks);
            long rawQpcHns = qpcPosition != 0
                ? qpcPosition - pauseHns - _startQpcHns
                : QpcClock.ToHns(Stopwatch.GetTimestamp()) - pauseHns - _startQpcHns;

            if (rawQpcHns < 0) rawQpcHns = 0;

            // Apply drift filter
            long smoothedHns = _driftFilter.SmoothTimestamp(rawQpcHns, durationHns);

            // Maintain contiguity
            var frames = _contiguityTracker.Align(samples, smoothedHns);

            float level = frames.Level;
            _levelMeter?.Update(level);
            OnLevel?.Invoke(level);

            OnAudioSample?.Invoke(frames);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Loopback audio processing warning: {ex.Message}");
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

        try { _recorder?.StopRecording(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        _recorder?.Dispose();
        _recorder = null;

        _renderClock?.Dispose();
        _renderClock = null;

        _renderClient?.Dispose();
        _renderClient = null;

        _renderDevice?.Dispose();
        _renderDevice = null;
    }
}
