// Ported from LightshotKit/Sources/LightshotKit/RecordingOverlayModel.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Recording;

namespace Lightshot.App.Views.Recording;

public class RecordingToolbarViewModel : INotifyPropertyChanged
{
    private readonly RecordingDefaults _defaults;
    private int _regionWidth;
    private int _regionHeight;
    private double _regionX;
    private double _regionY;
    private bool _microphoneEnabled;
    private bool _computerAudioEnabled;
    private bool _highlightClicksEnabled;
    private bool _showKeystrokesEnabled;
    private bool _recordCameraEnabled;
    private bool _countdownEnabled;
    private int _countdownSeconds;
    private RecordingOutputKind _outputKind = RecordingOutputKind.Video;
    private string? _selectedAudioInputId;
    private bool _isMicrophoneMuted;
    private bool _isHevcAvailable;
    private IReadOnlyList<AudioInputDevice> _audioInputs;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<RecordingChoice>? RecordingStarted;

    public RecordingToolbarViewModel(
        RecordingDefaults? defaults = null,
        IReadOnlyList<AudioInputDevice>? audioInputs = null,
        bool? isHevcAvailable = null,
        int initialWidth = 1280,
        int initialHeight = 720)
    {
        _defaults = defaults ?? new RecordingDefaults();
        _audioInputs = audioInputs ?? Array.Empty<AudioInputDevice>();

        if (isHevcAvailable.HasValue)
        {
            _isHevcAvailable = isHevcAvailable.Value;
        }
        else
        {
            try
            {
                _isHevcAvailable = new EncoderSelector().ProbeHardwareCapabilities().HasHardwareHevc;
            }
            catch
            {
                _isHevcAvailable = false;
            }
        }

        // Initialize from defaults
        _microphoneEnabled = _defaults.RecordMicrophone;
        _computerAudioEnabled = _defaults.RecordComputerAudio;
        _highlightClicksEnabled = _defaults.HighlightClicks;
        _showKeystrokesEnabled = _defaults.ShowKeystrokes;
        _recordCameraEnabled = _defaults.RecordCamera;
        _countdownEnabled = _defaults.CountdownEnabled;
        _countdownSeconds = _defaults.CountdownSeconds;
        _selectedAudioInputId = _defaults.MicrophoneDeviceID;

        _regionWidth = initialWidth;
        _regionHeight = initialHeight;
    }

    public int RegionWidth
    {
        get => _regionWidth;
        set
        {
            if (_regionWidth != value)
            {
                _regionWidth = Math.Max(0, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public int RegionHeight
    {
        get => _regionHeight;
        set
        {
            if (_regionHeight != value)
            {
                _regionHeight = Math.Max(0, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public double RegionX
    {
        get => _regionX;
        set
        {
            if (Math.Abs(_regionX - value) > 0.001)
            {
                _regionX = value;
                OnPropertyChanged();
            }
        }
    }

    public double RegionY
    {
        get => _regionY;
        set
        {
            if (Math.Abs(_regionY - value) > 0.001)
            {
                _regionY = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasSelection => _regionWidth > 0 && _regionHeight > 0;

    public bool MicrophoneEnabled
    {
        get => _microphoneEnabled;
        set
        {
            if (_microphoneEnabled != value)
            {
                _microphoneEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ComputerAudioEnabled
    {
        get => _computerAudioEnabled;
        set
        {
            if (_computerAudioEnabled != value)
            {
                _computerAudioEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HighlightClicksEnabled
    {
        get => _highlightClicksEnabled;
        set
        {
            if (_highlightClicksEnabled != value)
            {
                _highlightClicksEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ShowKeystrokesEnabled
    {
        get => _showKeystrokesEnabled;
        set
        {
            if (_showKeystrokesEnabled != value)
            {
                _showKeystrokesEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool RecordCameraEnabled
    {
        get => _recordCameraEnabled;
        set
        {
            if (_recordCameraEnabled != value)
            {
                _recordCameraEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CountdownEnabled
    {
        get => _countdownEnabled;
        set
        {
            if (_countdownEnabled != value)
            {
                _countdownEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public int CountdownSeconds
    {
        get => _countdownSeconds;
        set
        {
            if (_countdownSeconds != value)
            {
                _countdownSeconds = Math.Max(0, value);
                OnPropertyChanged();
            }
        }
    }

    public RecordingOutputKind OutputKind
    {
        get => _outputKind;
        set
        {
            if (_outputKind != value)
            {
                _outputKind = value;
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<AudioInputDevice> AudioInputs
    {
        get => _audioInputs;
        set
        {
            _audioInputs = value ?? Array.Empty<AudioInputDevice>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMultipleAudioInputs));
        }
    }

    public bool HasMultipleAudioInputs => _audioInputs.Count > 1;

    public string? SelectedAudioInputId
    {
        get => _selectedAudioInputId;
        set
        {
            if (_selectedAudioInputId != value)
            {
                _selectedAudioInputId = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsMicrophoneMuted
    {
        get => _isMicrophoneMuted;
        set
        {
            if (_isMicrophoneMuted != value)
            {
                _isMicrophoneMuted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMicrophoneWarning));
            }
        }
    }

    public bool HasMicrophoneWarning => _microphoneEnabled && _isMicrophoneMuted;

    public bool IsHevcAvailable
    {
        get => _isHevcAvailable;
        set
        {
            if (_isHevcAvailable != value)
            {
                _isHevcAvailable = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Fills the display the current region is on (largest overlap, else primary), in physical
    /// virtual-screen pixels, so Fullscreen stays on a secondary monitor and is not scaled by DPI.
    /// </summary>
    public void SetFullscreen(IReadOnlyList<DisplayInfo> displays)
    {
        var current = new Rect(RegionX, RegionY, RegionWidth, RegionHeight);
        var bounds = RecordingDisplayResolver.Resolve(displays, current).Display.Bounds;
        RegionX = bounds.X;
        RegionY = bounds.Y;
        RegionWidth = (int)Math.Round(bounds.Width);
        RegionHeight = (int)Math.Round(bounds.Height);
    }

    public void ToggleMicrophone() => MicrophoneEnabled = !MicrophoneEnabled;
    public void ToggleComputerAudio() => ComputerAudioEnabled = !ComputerAudioEnabled;
    public void ToggleHighlightClicks() => HighlightClicksEnabled = !HighlightClicksEnabled;
    public void ToggleShowKeystrokes() => ShowKeystrokesEnabled = !ShowKeystrokesEnabled;

    public RecordingOverrides BuildOverrides()
    {
        return new RecordingOverrides(
            microphone: _microphoneEnabled,
            computerAudio: _computerAudioEnabled,
            camera: _recordCameraEnabled,
            highlightClicks: _highlightClicksEnabled,
            showKeystrokes: _showKeystrokesEnabled
        );
    }

    public RecordingChoice StartVideo()
    {
        OutputKind = RecordingOutputKind.Video;
        var region = new CaptureRegion.RectRegion(new Rect(RegionX, RegionY, RegionWidth, RegionHeight));
        var choice = new RecordingChoice(
            region,
            RecordingOutputKind.Video,
            BuildOverrides(),
            MicrophoneDeviceID: _selectedAudioInputId
        );
        RecordingStarted?.Invoke(this, choice);
        return choice;
    }

    public RecordingChoice StartGif()
    {
        OutputKind = RecordingOutputKind.Gif;
        var region = new CaptureRegion.RectRegion(new Rect(RegionX, RegionY, RegionWidth, RegionHeight));
        var choice = new RecordingChoice(
            region,
            RecordingOutputKind.Gif,
            BuildOverrides(),
            MicrophoneDeviceID: _selectedAudioInputId
        );
        RecordingStarted?.Invoke(this, choice);
        return choice;
    }

    public RecordingOptions ResolveOptions(CaptureRegion? customRegion = null)
    {
        var region = customRegion ?? new CaptureRegion.RectRegion(new Rect(RegionX, RegionY, RegionWidth, RegionHeight));
        var overrides = BuildOverrides();
        var defaults = _defaults with
        {
            MicrophoneDeviceID = _selectedAudioInputId,
            CountdownEnabled = _countdownEnabled,
            CountdownSeconds = _countdownSeconds
        };

        return RecordingOptions.Resolve(region, OutputKind, defaults, overrides);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
