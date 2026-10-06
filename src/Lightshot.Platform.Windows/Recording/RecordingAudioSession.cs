// Ported for Lightshot Windows Port (Phase 7 R3)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.Platform.Windows.Audio;
using PlatformAudioMixer = Lightshot.Platform.Windows.Audio.AudioMixer;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Manages the lifecycle of audio input devices (microphone and process loopback)
/// and coordinates audio samples through AudioMixer into the MP4 sink writer.
/// </summary>
internal sealed class RecordingAudioSession : IDisposable
{
    private readonly AudioDeviceService _audioDeviceService;
    private readonly RecordingOptions _options;
    private readonly Action<int, byte[], long, long> _writeAudioSample;
    private readonly object _lock = new();

    private PlatformAudioMixer? _audioMixer;
    private WasapiMicrophone? _mic;
    private ProcessLoopbackCapture? _loopback;
    private IReadOnlyList<AudioTrackConfig> _trackConfigs = Array.Empty<AudioTrackConfig>();
    private bool _started;
    private bool _disposed;

    public IReadOnlyList<AudioTrackConfig> TrackConfigs => _trackConfigs;
    public bool HasMic => _mic != null;
    public bool HasLoopback => _loopback != null;

    public RecordingAudioSession(
        AudioDeviceService audioDeviceService,
        RecordingOptions options,
        Action<int, byte[], long, long> writeAudioSample)
    {
        _audioDeviceService = audioDeviceService ?? throw new ArgumentNullException(nameof(audioDeviceService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _writeAudioSample = writeAudioSample ?? throw new ArgumentNullException(nameof(writeAudioSample));

        bool recordMic = _options.Microphone is not InputDeviceSelection.Off;
        bool hasLoopback = _options.ComputerAudio;
        _trackConfigs = PlatformAudioMixer.ResolveTrackConfigs(_options, recordMic, hasLoopback);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _started) return;
            _started = true;

            bool recordMic = _options.Microphone is not InputDeviceSelection.Off;
            bool hasLoopback = _options.ComputerAudio;

            if (_trackConfigs.Count == 0) return;

            string? pickedDeviceId = null;
            if (recordMic)
            {
                string? requestedId = (_options.Microphone as InputDeviceSelection.Device)?.Id;
                var availableInputs = _audioDeviceService.AvailableInputsAsync().GetAwaiter().GetResult();

                if (!string.IsNullOrEmpty(requestedId) &&
                    availableInputs.Any(d => string.Equals(d.Id, requestedId, StringComparison.OrdinalIgnoreCase)))
                {
                    pickedDeviceId = requestedId;
                }
                else if (availableInputs.Count > 0)
                {
                    var defaultDev = availableInputs.FirstOrDefault(d => d.IsDefault);
                    pickedDeviceId = !string.IsNullOrEmpty(defaultDev.Id) ? defaultDev.Id : availableInputs[0].Id;
                }

                _audioDeviceService.ActiveDeviceId = pickedDeviceId;
                _audioDeviceService.AudioSourceLost += HandleAudioSourceLost;
            }

            _audioMixer = new PlatformAudioMixer(_options, recordMic, hasLoopback, _writeAudioSample);

            if (recordMic)
            {
                _mic = new WasapiMicrophone(
                    deviceId: pickedDeviceId,
                    mono: _options.MonoAudio,
                    volume: _options.MicrophoneVolume,
                    onFrames: frames => _audioMixer?.OnMicFrames(frames));
                _mic.Start();
            }

            if (hasLoopback)
            {
                _loopback = new ProcessLoopbackCapture();
                _loopback.OnAudioSample += frames => _audioMixer?.OnLoopbackFrames(frames);
                _loopback.Start();
            }
        }
    }

    private void HandleAudioSourceLost()
    {
        lock (_lock)
        {
            _audioMixer?.OnMicLost();
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            _mic?.Pause();
            _loopback?.Pause();
            _audioMixer?.Pause();
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _mic?.Resume();
            _loopback?.Resume();
            _audioMixer?.Resume();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _mic?.Stop();
            _loopback?.Stop();
            _audioMixer?.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _audioDeviceService.AudioSourceLost -= HandleAudioSourceLost;
            }
            catch { }

            _mic?.Dispose();
            _mic = null;

            _loopback?.Dispose();
            _loopback = null;

            _audioMixer?.Dispose();
            _audioMixer = null;
        }
    }
}
