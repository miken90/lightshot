// Ported from App/Sources/SettingsModel.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Lightshot.Core;

namespace Lightshot.App.Views.Settings;

/// <summary>
/// ViewModel binding the settings window panes to ISettingsStore.
/// Edits write through immediately to persistence without an Apply button.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    // Settings keys from APP §5; switch to SettingsKeys once P5a completes.
    private static class SettingKeys
    {
        public const string Format = "save.format";
        public const string Quality = "save.jpegQuality";
        public const string Location = "save.location";
        public const string Pattern = "save.filenamePattern";
        public const string Hotkeys = "capture.hotkeys";
        public const string OpenInEditor = "capture.openInEditor";
        public const string IncludeCursor = "capture.includeCursor";
        public const string AdjustAreaBeforeCapture = "capture.adjustAreaBeforeCapture";
        public const string CaptureDelay = "capture.delay";
        public const string HistoryRetention = "history.retention";
        public const string RecordingDefaults = "recording.defaults";
        public const string RememberLastRecordingArea = "recording.rememberLastArea";
        public const string LastRecordingRegion = "recording.lastRegion";
        public const string Appearance = "app.appearance";
        public const string OcrKeepsLineBreaks = "ocr.keepLineBreaks";
        public const string HideDesktopIcons = "app.hideDesktopIcons";
        public const string QuickAccess = "quickAccess.settings";
        public const string Onboarded = "app.onboarded";
    }

    private readonly ISettingsStore _store;
    private readonly Func<HotkeyBindings, IReadOnlyList<CaptureAction>>? _applyHotkeys;
    private readonly Action<int>? _applyRetention;
    private readonly Action<AppearancePreference>? _applyAppearance;

    private HotkeyBindings _hotkeys;
    private IReadOnlyList<CaptureAction> _unregisterableActions = [];
    private bool _formatIsJpeg;
    private double _jpegQuality = 0.9;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(
        ISettingsStore store,
        Func<HotkeyBindings, IReadOnlyList<CaptureAction>>? applyHotkeys = null,
        Action<int>? applyRetention = null,
        Action<AppearancePreference>? applyAppearance = null)
    {
        _store = store;
        _applyHotkeys = applyHotkeys;
        _applyRetention = applyRetention;
        _applyAppearance = applyAppearance;

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
            }
        }
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

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
