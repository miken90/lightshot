// Ported from App/Sources/SettingsModel.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Startup;
using Lightshot.Platform.Windows.Windows;

namespace Lightshot.App.Views.Settings;

/// <summary>
/// ViewModel binding the settings window panes to ISettingsStore.
/// Edits write through immediately to persistence without an Apply button.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore _store;
    private readonly Func<HotkeyBindings, IReadOnlyList<CaptureAction>>? _applyHotkeys;
    private readonly Action<int>? _applyRetention;
    private readonly Action<AppearancePreference>? _applyAppearance;

    private HotkeyBindings _hotkeys;
    private IReadOnlyList<CaptureAction> _unregisterableActions = [];
    private bool _formatIsJpeg;
    private double _jpegQuality = 0.9;

    private bool _isHevcAvailable;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(
        ISettingsStore store,
        Func<HotkeyBindings, IReadOnlyList<CaptureAction>>? applyHotkeys = null,
        Action<int>? applyRetention = null,
        Action<AppearancePreference>? applyAppearance = null,
        bool? isHevcAvailable = null,
        UpdateSettingsViewModel? updates = null)
    {
        _store = store;
        Updates = updates ?? new UpdateSettingsViewModel(store, typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0", isInstalled: false);
        _applyHotkeys = applyHotkeys;
        _applyRetention = applyRetention;
        _applyAppearance = applyAppearance;
        _isHevcAvailable = isHevcAvailable ?? ProbeHevcSupport();

        if (!_isHevcAvailable && _store.RecordingDefaults.Video.Codec == VideoCodec.Hevc)
        {
            _store.RecordingDefaults.Video.Codec = VideoCodec.H264;
        }

        _hotkeys = _store.Hotkeys ?? HotkeyBindings.Defaults;
        if (_applyHotkeys != null)
        {
            _unregisterableActions = _applyHotkeys(_hotkeys);
        }

        if (_store.DefaultFormat is ImageFormat.Jpeg jpeg)
        {
            _formatIsJpeg = true;
            _jpegQuality = jpeg.Quality;
        }
        else
        {
            _formatIsJpeg = false;
            _jpegQuality = 0.9;
        }
    }

    public UpdateSettingsViewModel Updates { get; }

    public HotkeyBindings Hotkeys
    {
        get => _hotkeys;
        set
        {
            _hotkeys = value;
            _store.Hotkeys = value;
            _unregisterableActions = _applyHotkeys?.Invoke(value) ?? [];
            OnPropertyChanged();
            OnPropertyChanged(nameof(Conflicts));
            OnPropertyChanged(nameof(HasConflicts));
            OnPropertyChanged(nameof(UnregisterableActions));
        }
    }

    public IReadOnlyList<HotkeyConflict> Conflicts => _hotkeys.Conflicts;
    public bool HasConflicts => Conflicts.Count > 0;
    public IReadOnlyList<CaptureAction> UnregisterableActions => _unregisterableActions;

    public void SetBinding(CaptureAction action, HotkeyBinding? binding)
    {
        var copy = new Dictionary<CaptureAction, HotkeyBinding>(_hotkeys.Assignments);
        if (binding.HasValue)
        {
            copy[action] = binding.Value;
        }
        else
        {
            copy.Remove(action);
        }
        Hotkeys = new HotkeyBindings(copy);
    }

    public void ResetHotkeysToDefaults()
    {
        Hotkeys = HotkeyBindings.Defaults;
    }

    public static bool IsReservedChord(HotkeyBinding chord)
    {
        // Win+L (Lock Workstation)
        if (chord.Modifiers.HasFlag(HotkeyModifiers.Command) && (chord.KeyCode == 'L' || chord.KeyLabel.Equals("L", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        // Ctrl+Alt+Del
        if (chord.Modifiers.HasFlag(HotkeyModifiers.Control) && chord.Modifiers.HasFlag(HotkeyModifiers.Option) && chord.KeyCode == 0x2E)
        {
            return true;
        }
        return false;
    }

    public bool FormatIsJpeg
    {
        get => _formatIsJpeg;
        set
        {
            if (_formatIsJpeg != value)
            {
                _formatIsJpeg = value;
                PersistFormat();
                OnPropertyChanged();
            }
        }
    }

    public double JpegQuality
    {
        get => _jpegQuality;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_jpegQuality - clamped) > 0.001)
            {
                _jpegQuality = clamped;
                if (_formatIsJpeg) PersistFormat();
                OnPropertyChanged();
            }
        }
    }

    private void PersistFormat()
    {
        _store.DefaultFormat = _formatIsJpeg
            ? new ImageFormat.Jpeg(_jpegQuality)
            : new ImageFormat.Png();
    }

    public string SaveLocation
    {
        get => _store.SaveLocation;
        set
        {
            if (_store.SaveLocation != value)
            {
                _store.SaveLocation = value;
                OnPropertyChanged();
            }
        }
    }

    public string FilenamePattern
    {
        get => _store.FilenamePattern;
        set
        {
            if (_store.FilenamePattern != value)
            {
                _store.FilenamePattern = value;
                OnPropertyChanged();
            }
        }
    }

    public bool OpenInEditor
    {
        get => _store.OpenInEditor;
        set
        {
            if (_store.OpenInEditor != value)
            {
                _store.OpenInEditor = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AfterShowQuickAccess
    {
        get => _store.AfterCapture.ShowQuickAccess;
        set { if (_store.AfterCapture.ShowQuickAccess != value) { _store.AfterCapture = _store.AfterCapture with { ShowQuickAccess = value }; OnPropertyChanged(); } }
    }

    public bool AfterCopyToClipboard
    {
        get => _store.AfterCapture.CopyToClipboard;
        set { if (_store.AfterCapture.CopyToClipboard != value) { _store.AfterCapture = _store.AfterCapture with { CopyToClipboard = value }; OnPropertyChanged(); } }
    }

    public bool AfterSaveToFile
    {
        get => _store.AfterCapture.SaveToFile;
        set { if (_store.AfterCapture.SaveToFile != value) { _store.AfterCapture = _store.AfterCapture with { SaveToFile = value }; OnPropertyChanged(); } }
    }

    public bool IncludeCursor
    {
        get => _store.IncludeCursor;
        set
        {
            if (_store.IncludeCursor != value)
            {
                _store.IncludeCursor = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AdjustAreaBeforeCapture
    {
        get => _store.AdjustAreaBeforeCapture;
        set
        {
            if (_store.AdjustAreaBeforeCapture != value)
            {
                _store.AdjustAreaBeforeCapture = value;
                OnPropertyChanged();
            }
        }
    }

    public double CaptureDelay
    {
        get => _store.CaptureDelay;
        set
        {
            double val = Math.Max(0, value);
            if (Math.Abs(_store.CaptureDelay - val) > 0.001)
            {
                _store.CaptureDelay = val;
                OnPropertyChanged();
            }
        }
    }

    public int HistoryRetention
    {
        get => _store.HistoryRetention;
        set
        {
            int val = Math.Max(0, value);
            if (_store.HistoryRetention != val)
            {
                _store.HistoryRetention = val;
                _applyRetention?.Invoke(val);
                OnPropertyChanged();
            }
        }
    }

    public bool HideDesktopIcons
    {
        get => _store.HideDesktopIcons;
        set
        {
            if (_store.HideDesktopIcons != value)
            {
                _store.HideDesktopIcons = value;
                OnPropertyChanged();
            }
        }
    }

    public AppearancePreference Appearance
    {
        get => _store.Appearance;
        set
        {
            if (_store.Appearance != value)
            {
                _store.Appearance = value;
                _applyAppearance?.Invoke(value);
                OnPropertyChanged();
            }
        }
    }

    public bool LaunchAtLogin
    {
        get => _store.LaunchAtLogin;
        set
        {
            if (_store.LaunchAtLogin != value)
            {
                _store.LaunchAtLogin = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStartupDisabledByTaskManager));
            }
        }
    }

    public bool IsStartupDisabledByTaskManager => Lightshot.Platform.Windows.Startup.LaunchAtLogin.IsDisabledByTaskManager();

    public void OpenStartupSettings()
    {
        Explorer.OpenSettingsUri(Explorer.StartupAppsUri);
    }

    public bool OcrKeepsLineBreaks
    {
        get => _store.OcrKeepsLineBreaks;
        set
        {
            if (_store.OcrKeepsLineBreaks != value)
            {
                _store.OcrKeepsLineBreaks = value;
                OnPropertyChanged();
            }
        }
    }

    #region Recording Defaults

    public RecordingDefaults RecordingDefaults => _store.RecordingDefaults;

    public bool IsHevcAvailable => _isHevcAvailable;

    public VideoCodec VideoCodec
    {
        get => _isHevcAvailable ? _store.RecordingDefaults.Video.Codec : VideoCodec.H264;
        set
        {
            var target = _isHevcAvailable ? value : VideoCodec.H264;
            if (_store.RecordingDefaults.Video.Codec != target)
            {
                _store.RecordingDefaults.Video.Codec = target;
                NotifyRecordingChanged();
            }
        }
    }

    public int VideoFps
    {
        get => _store.RecordingDefaults.Video.Fps;
        set
        {
            if (_store.RecordingDefaults.Video.Fps != value)
            {
                _store.RecordingDefaults.Video.Fps = value;
                NotifyRecordingChanged();
            }
        }
    }

    public MaxResolution VideoMaxResolution
    {
        get => _store.RecordingDefaults.Video.MaxResolution;
        set
        {
            if (_store.RecordingDefaults.Video.MaxResolution != value)
            {
                _store.RecordingDefaults.Video.MaxResolution = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool VideoScaleRetinaTo1x
    {
        get => _store.RecordingDefaults.Video.ScaleRetinaTo1x;
        set
        {
            if (_store.RecordingDefaults.Video.ScaleRetinaTo1x != value)
            {
                _store.RecordingDefaults.Video.ScaleRetinaTo1x = value;
                NotifyRecordingChanged();
            }
        }
    }

    public int GifFps
    {
        get => _store.RecordingDefaults.Gif.Fps;
        set
        {
            if (_store.RecordingDefaults.Gif.Fps != value)
            {
                _store.RecordingDefaults.Gif.Fps = value;
                NotifyRecordingChanged();
            }
        }
    }

    public double GifQuality
    {
        get => _store.RecordingDefaults.Gif.Quality;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_store.RecordingDefaults.Gif.Quality - clamped) > 0.001)
            {
                _store.RecordingDefaults.Gif.Quality = clamped;
                NotifyRecordingChanged();
            }
        }
    }

    public int? GifMaxWidth
    {
        get => _store.RecordingDefaults.Gif.MaxWidth;
        set
        {
            if (_store.RecordingDefaults.Gif.MaxWidth != value)
            {
                _store.RecordingDefaults.Gif.MaxWidth = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool GifOptimize
    {
        get => _store.RecordingDefaults.Gif.Optimize;
        set
        {
            if (_store.RecordingDefaults.Gif.Optimize != value)
            {
                _store.RecordingDefaults.Gif.Optimize = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool RecordMicrophone
    {
        get => _store.RecordingDefaults.RecordMicrophone;
        set
        {
            if (_store.RecordingDefaults.RecordMicrophone != value)
            {
                _store.RecordingDefaults.RecordMicrophone = value;
                NotifyRecordingChanged();
            }
        }
    }

    public string? MicrophoneDeviceId
    {
        get => _store.RecordingDefaults.MicrophoneDeviceID;
        set
        {
            if (_store.RecordingDefaults.MicrophoneDeviceID != value)
            {
                _store.RecordingDefaults.MicrophoneDeviceID = value;
                NotifyRecordingChanged();
            }
        }
    }

    public double MicrophoneVolume
    {
        get => _store.RecordingDefaults.MicrophoneVolume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 2.0);
            if (Math.Abs(_store.RecordingDefaults.MicrophoneVolume - clamped) > 0.001)
            {
                _store.RecordingDefaults.MicrophoneVolume = clamped;
                NotifyRecordingChanged();
            }
        }
    }

    public bool MonoAudio
    {
        get => _store.RecordingDefaults.MonoAudio;
        set
        {
            if (_store.RecordingDefaults.MonoAudio != value)
            {
                _store.RecordingDefaults.MonoAudio = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool RecordComputerAudio
    {
        get => _store.RecordingDefaults.RecordComputerAudio;
        set
        {
            if (_store.RecordingDefaults.RecordComputerAudio != value)
            {
                _store.RecordingDefaults.RecordComputerAudio = value;
                NotifyRecordingChanged();
            }
        }
    }

    public double ComputerAudioVolume
    {
        get => _store.RecordingDefaults.ComputerAudioVolume;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 2.0);
            if (Math.Abs(_store.RecordingDefaults.ComputerAudioVolume - clamped) > 0.001)
            {
                _store.RecordingDefaults.ComputerAudioVolume = clamped;
                NotifyRecordingChanged();
            }
        }
    }

    public bool SeparateAudioTracks
    {
        get => _store.RecordingDefaults.SeparateAudioTracks;
        set
        {
            if (_store.RecordingDefaults.SeparateAudioTracks != value)
            {
                _store.RecordingDefaults.SeparateAudioTracks = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool RecordCamera
    {
        get => _store.RecordingDefaults.RecordCamera;
        set
        {
            if (_store.RecordingDefaults.RecordCamera != value)
            {
                _store.RecordingDefaults.RecordCamera = value;
                NotifyRecordingChanged();
            }
        }
    }

    public string? CameraDeviceId
    {
        get => _store.RecordingDefaults.CameraDeviceID;
        set
        {
            if (_store.RecordingDefaults.CameraDeviceID != value)
            {
                _store.RecordingDefaults.CameraDeviceID = value;
                NotifyRecordingChanged();
            }
        }
    }

    public CameraBubbleSize CameraBubbleSize
    {
        get => _store.RecordingDefaults.CameraBubble.Size;
        set
        {
            if (_store.RecordingDefaults.CameraBubble.Size != value)
            {
                _store.RecordingDefaults.CameraBubble.Size = value;
                NotifyRecordingChanged();
            }
        }
    }

    public CameraBubbleShape CameraBubbleShape
    {
        get => _store.RecordingDefaults.CameraBubble.Shape;
        set
        {
            if (_store.RecordingDefaults.CameraBubble.Shape != value)
            {
                _store.RecordingDefaults.CameraBubble.Shape = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool CameraBubbleMirror
    {
        get => _store.RecordingDefaults.CameraBubble.Mirror;
        set
        {
            if (_store.RecordingDefaults.CameraBubble.Mirror != value)
            {
                _store.RecordingDefaults.CameraBubble.Mirror = value;
                NotifyRecordingChanged();
            }
        }
    }

    public Point? CameraBubbleAnchor
    {
        get => _store.RecordingDefaults.CameraBubble.Anchor;
        set
        {
            if (_store.RecordingDefaults.CameraBubble.Anchor != value)
            {
                _store.RecordingDefaults.CameraBubble.Anchor = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool ShowCursor
    {
        get => _store.RecordingDefaults.ShowCursor;
        set
        {
            if (_store.RecordingDefaults.ShowCursor != value)
            {
                _store.RecordingDefaults.ShowCursor = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool HighlightClicks
    {
        get => _store.RecordingDefaults.HighlightClicks;
        set
        {
            if (_store.RecordingDefaults.HighlightClicks != value)
            {
                _store.RecordingDefaults.HighlightClicks = value;
                NotifyRecordingChanged();
            }
        }
    }

    public CursorHighlightStyle ClickHighlightStyle
    {
        get => _store.RecordingDefaults.ClickHighlight.Style;
        set
        {
            if (_store.RecordingDefaults.ClickHighlight.Style != value)
            {
                _store.RecordingDefaults.ClickHighlight.Style = value;
                NotifyRecordingChanged();
                OnPropertyChanged(nameof(ClickHighlight));
            }
        }
    }

    public CursorHighlightSize ClickHighlightSize
    {
        get => _store.RecordingDefaults.ClickHighlight.Size;
        set
        {
            if (_store.RecordingDefaults.ClickHighlight.Size != value)
            {
                _store.RecordingDefaults.ClickHighlight.Size = value;
                NotifyRecordingChanged();
                OnPropertyChanged(nameof(ClickHighlight));
            }
        }
    }

    public CursorHighlightColor ClickHighlightColor
    {
        get => _store.RecordingDefaults.ClickHighlight.Color;
        set
        {
            if (_store.RecordingDefaults.ClickHighlight.Color != value)
            {
                _store.RecordingDefaults.ClickHighlight.Color = value;
                NotifyRecordingChanged();
                OnPropertyChanged(nameof(ClickHighlight));
            }
        }
    }

    public bool ClickHighlightAnimateClicks
    {
        get => _store.RecordingDefaults.ClickHighlight.AnimateClicks;
        set
        {
            if (_store.RecordingDefaults.ClickHighlight.AnimateClicks != value)
            {
                _store.RecordingDefaults.ClickHighlight.AnimateClicks = value;
                NotifyRecordingChanged();
                OnPropertyChanged(nameof(ClickHighlight));
            }
        }
    }

    public ClickHighlightSettings ClickHighlight => _store.RecordingDefaults.ClickHighlight;

    public bool ShowKeystrokes
    {
        get => _store.RecordingDefaults.ShowKeystrokes;
        set
        {
            if (_store.RecordingDefaults.ShowKeystrokes != value)
            {
                _store.RecordingDefaults.ShowKeystrokes = value;
                NotifyRecordingChanged();
            }
        }
    }

    public KeystrokeDisplayMode KeystrokeMode
    {
        get => _store.RecordingDefaults.KeystrokeOverlay.Mode;
        set
        {
            if (_store.RecordingDefaults.KeystrokeOverlay.Mode != value)
            {
                _store.RecordingDefaults.KeystrokeOverlay.Mode = value;
                NotifyRecordingChanged();
            }
        }
    }

    public KeystrokeOverlayPosition KeystrokePosition
    {
        get => _store.RecordingDefaults.KeystrokeOverlay.Position;
        set
        {
            if (_store.RecordingDefaults.KeystrokeOverlay.Position != value)
            {
                _store.RecordingDefaults.KeystrokeOverlay.Position = value;
                NotifyRecordingChanged();
            }
        }
    }

    public KeystrokeOverlaySize KeystrokeSize
    {
        get => _store.RecordingDefaults.KeystrokeOverlay.Size;
        set
        {
            if (_store.RecordingDefaults.KeystrokeOverlay.Size != value)
            {
                _store.RecordingDefaults.KeystrokeOverlay.Size = value;
                NotifyRecordingChanged();
            }
        }
    }

    public KeystrokeOverlayAppearance KeystrokeAppearance
    {
        get => _store.RecordingDefaults.KeystrokeOverlay.Appearance;
        set
        {
            if (_store.RecordingDefaults.KeystrokeOverlay.Appearance != value)
            {
                _store.RecordingDefaults.KeystrokeOverlay.Appearance = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool KeystrokeBlurBackground
    {
        get => _store.RecordingDefaults.KeystrokeOverlay.BlurBackground;
        set
        {
            if (_store.RecordingDefaults.KeystrokeOverlay.BlurBackground != value)
            {
                _store.RecordingDefaults.KeystrokeOverlay.BlurBackground = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool CountdownEnabled
    {
        get => _store.RecordingDefaults.CountdownEnabled;
        set
        {
            if (_store.RecordingDefaults.CountdownEnabled != value)
            {
                _store.RecordingDefaults.CountdownEnabled = value;
                NotifyRecordingChanged();
            }
        }
    }

    public int CountdownSeconds
    {
        get => _store.RecordingDefaults.CountdownSeconds;
        set
        {
            int val = Math.Max(0, value);
            if (_store.RecordingDefaults.CountdownSeconds != val)
            {
                _store.RecordingDefaults.CountdownSeconds = val;
                NotifyRecordingChanged();
            }
        }
    }

    public bool PlaySounds
    {
        get => _store.RecordingDefaults.PlaySounds;
        set
        {
            if (_store.RecordingDefaults.PlaySounds != value)
            {
                _store.RecordingDefaults.PlaySounds = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool ShowRecordingControls
    {
        get => _store.RecordingDefaults.ShowRecordingControls;
        set
        {
            if (_store.RecordingDefaults.ShowRecordingControls != value)
            {
                _store.RecordingDefaults.ShowRecordingControls = value;
                NotifyRecordingChanged();
            }
        }
    }

    public RecordingControlsPosition ControlsPosition
    {
        get => _store.RecordingDefaults.ControlsPosition;
        set
        {
            if (_store.RecordingDefaults.ControlsPosition != value)
            {
                _store.RecordingDefaults.ControlsPosition = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool DimScreenWhileRecording
    {
        get => _store.RecordingDefaults.DimScreenWhileRecording;
        set
        {
            if (_store.RecordingDefaults.DimScreenWhileRecording != value)
            {
                _store.RecordingDefaults.DimScreenWhileRecording = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool ConfirmBeforeDiscard
    {
        get => _store.RecordingDefaults.ConfirmBeforeDiscard;
        set
        {
            if (_store.RecordingDefaults.ConfirmBeforeDiscard != value)
            {
                _store.RecordingDefaults.ConfirmBeforeDiscard = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool ShowRecordingTimeInMenuBar
    {
        get => _store.RecordingDefaults.ShowRecordingTimeInMenuBar;
        set
        {
            if (_store.RecordingDefaults.ShowRecordingTimeInMenuBar != value)
            {
                _store.RecordingDefaults.ShowRecordingTimeInMenuBar = value;
                NotifyRecordingChanged();
            }
        }
    }

    public AfterRecordingAction AfterRecording
    {
        get => _store.RecordingDefaults.AfterRecording;
        set
        {
            if (_store.RecordingDefaults.AfterRecording != value)
            {
                _store.RecordingDefaults.AfterRecording = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool HideNotifications
    {
        get => _store.RecordingDefaults.HideNotifications;
        set
        {
            if (_store.RecordingDefaults.HideNotifications != value)
            {
                _store.RecordingDefaults.HideNotifications = value;
                NotifyRecordingChanged();
            }
        }
    }

    public bool RememberLastRecordingArea
    {
        get => _store.RememberLastRecordingArea;
        set
        {
            if (_store.RememberLastRecordingArea != value)
            {
                _store.RememberLastRecordingArea = value;
                NotifyRecordingChanged();
            }
        }
    }

    private void NotifyRecordingChanged([CallerMemberName] string? propertyName = null)
    {
        _store.RecordingDefaults = _store.RecordingDefaults;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(RecordingDefaults));
    }

    private static bool ProbeHevcSupport()
    {
        try
        {
            var selector = new Lightshot.Platform.Windows.Recording.EncoderSelector();
            return selector.ProbeHardwareCapabilities().HasHardwareHevc;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
