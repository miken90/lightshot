// Ported from LightshotKit/Sources/LightshotKit/RecordingControlsController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lightshot.Core;

namespace Lightshot.App.Views.Recording;

public class ControlsPillViewModel : INotifyPropertyChanged
{
    private bool _isPaused;
    private double _elapsedSeconds;
    private string _formattedTime = "00:00";
    private float _microphoneLevel;
    private float _computerAudioLevel;
    private bool _hasMicrophone;
    private bool _hasComputerAudio;
    private MutedMicrophoneDetector _mutedDetector;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? PauseResumeRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? RestartRequested;
    public event EventHandler? DiscardRequested;

    public ControlsPillViewModel(
        bool hasMicrophone = true,
        bool hasComputerAudio = false,
        bool isPaused = false)
    {
        _hasMicrophone = hasMicrophone;
        _hasComputerAudio = hasComputerAudio;
        _isPaused = isPaused;
        _mutedDetector = new MutedMicrophoneDetector();
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused != value)
            {
                _isPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(PlayPauseIcon));
            }
        }
    }

    public string StatusText => _isPaused ? "Paused" : "Recording";
    public string PlayPauseIcon => _isPaused ? "\u25B6" : "\u23F8"; // Play : Pause

    public double ElapsedSeconds
    {
        get => _elapsedSeconds;
        set
        {
            if (Math.Abs(_elapsedSeconds - value) > 0.001)
            {
                _elapsedSeconds = value;
                FormattedTime = FormatTime(value);
                OnPropertyChanged();
            }
        }
    }

    public string FormattedTime
    {
        get => _formattedTime;
        private set
        {
            if (_formattedTime != value)
            {
                _formattedTime = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasMicrophone
    {
        get => _hasMicrophone;
        set
        {
            if (_hasMicrophone != value)
            {
                _hasMicrophone = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasComputerAudio
    {
        get => _hasComputerAudio;
        set
        {
            if (_hasComputerAudio != value)
            {
                _hasComputerAudio = value;
                OnPropertyChanged();
            }
        }
    }

    public float MicrophoneLevel
    {
        get => _microphoneLevel;
        set
        {
            _microphoneLevel = Math.Clamp(value, 0f, 1f);
            OnPropertyChanged();
        }
    }

    public float ComputerAudioLevel
    {
        get => _computerAudioLevel;
        set
        {
            _computerAudioLevel = Math.Clamp(value, 0f, 1f);
            OnPropertyChanged();
        }
    }

    public bool ShowsMutedWarning => _mutedDetector.ShowsWarning && _hasMicrophone;

    public void Update(double elapsedSeconds, float? micLevel = null, float? systemAudioLevel = null, double sampleInterval = 0.1)
    {
        ElapsedSeconds = elapsedSeconds;

        if (micLevel.HasValue && _hasMicrophone)
        {
            MicrophoneLevel = micLevel.Value;
            _mutedDetector.Observe(micLevel.Value, sampleInterval, _isPaused);
            OnPropertyChanged(nameof(ShowsMutedWarning));
        }

        if (systemAudioLevel.HasValue && _hasComputerAudio)
        {
            ComputerAudioLevel = systemAudioLevel.Value;
        }
    }

    public void PauseResume()
    {
        IsPaused = !IsPaused;
        PauseResumeRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Restart()
    {
        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Discard()
    {
        DiscardRequested?.Invoke(this, EventArgs.Empty);
    }

    public static string FormatTime(double seconds)
    {
        int whole = (int)Math.Floor(Math.Max(0, seconds));
        int minutes = whole / 60;
        int secs = whole % 60;
        return $"{minutes:D2}:{secs:D2}";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
