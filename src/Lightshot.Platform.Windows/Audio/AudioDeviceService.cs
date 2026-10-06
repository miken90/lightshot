// Ported from LightshotKit/Sources/LightshotKit/AudioInputService.swift & App/Sources/AVAudioInputService.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lightshot.Core;
using NAudio.CoreAudioApi;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Device service for enumerating microphones and monitoring device connect/disconnect events.
/// Implements IAudioInputService and IMMNotificationClient.
/// </summary>
public sealed class AudioDeviceService : IAudioInputService, IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly object _lock = new();
    private string? _activeDeviceId;
    private bool _disposed;

    public event Action? AudioSourceLost;
    public event Action? DevicesChanged;

    /// <summary>
    /// Currently active recording microphone device ID being monitored.
    /// </summary>
    public string? ActiveDeviceId
    {
        get { lock (_lock) return _activeDeviceId; }
        set { lock (_lock) _activeDeviceId = value; }
    }

    public AudioDeviceService()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
        }
        catch
        {
            // Fallback for environments with no audio service
            _enumerator = null;
        }
    }

    /// <summary>
    /// Lists all active audio capture devices (microphones), with the default device flagged.
    /// </summary>
    public Task<IReadOnlyList<AudioInputDevice>> AvailableInputsAsync()
    {
        var list = new List<AudioInputDevice>();

        if (_enumerator == null)
        {
            return Task.FromResult<IReadOnlyList<AudioInputDevice>>(list);
        }

        try
        {
            string? defaultId = null;
            try
            {
                using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                defaultId = defaultDevice?.ID;
            }
            catch
            {
                // No default endpoint available
            }

            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                try
                {
                    string id = device.ID;
                    string name = device.FriendlyName;
                    bool isDefault = !string.IsNullOrEmpty(defaultId) && string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                    list.Add(new AudioInputDevice(id, name, isDefault));
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch
        {
            // Return empty list on COM or hardware failure
        }

        return Task.FromResult<IReadOnlyList<AudioInputDevice>>(list);
    }

    // MARK: - IMMNotificationClient Implementation

    public void OnDeviceRemoved(string pwstrDeviceId)
    {
        bool isLost = false;
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_activeDeviceId) &&
                string.Equals(pwstrDeviceId, _activeDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                isLost = true;
            }
        }

        if (isLost)
        {
            AudioSourceLost?.Invoke();
        }

        DevicesChanged?.Invoke();
    }

    public void OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState)
    {
        // 1 = DEVICE_STATE_ACTIVE
        const uint DEVICE_STATE_ACTIVE = 1;
        bool isLost = false;
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_activeDeviceId) &&
                string.Equals(pwstrDeviceId, _activeDeviceId, StringComparison.OrdinalIgnoreCase) &&
                dwNewState != DEVICE_STATE_ACTIVE)
            {
                isLost = true;
            }
        }

        if (isLost)
        {
            AudioSourceLost?.Invoke();
        }

        DevicesChanged?.Invoke();
    }

    public void OnDefaultDeviceChanged(int flow, int role, string pwstrDefaultDeviceId)
    {
        // 1 = eCapture
        const int eCapture = 1;
        if (flow == eCapture)
        {
            DevicesChanged?.Invoke();
        }
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        DevicesChanged?.Invoke();
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, nint key)
    {
        // Property changes do not affect stream connectivity
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _enumerator?.Dispose();
    }
}
