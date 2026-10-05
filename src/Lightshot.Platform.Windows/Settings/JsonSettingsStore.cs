// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lightshot.Core;
using Lightshot.Platform.Windows.Files;

namespace Lightshot.Platform.Windows.Settings;

/// <summary>
/// Minimal file-backed ISettingsStore persisting configuration as JSON via AtomicFile.
/// </summary>
public class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly object _lock = new();

    private ImageFormat _defaultFormat = new ImageFormat.Png();
    private string _saveLocation = KnownFolders.DefaultSaveLocation;
    private string _filenamePattern = "Screenshot %Y-%m-%d at %H.%M.%S";
    private HotkeyBindings _hotkeys = HotkeyBindings.Defaults;
    private bool _openInEditor = true;
    private bool _includeCursor = false;
    private double _captureDelay = 0;
    private int _historyRetention = 50;
    private bool _launchAtLogin = false;
    private RecordingDefaults _recordingDefaults = new();
    private bool _rememberLastRecordingArea = false;
    private CaptureRegion? _lastRecordingRegion = null;
    private AppearancePreference _appearance = AppearancePreference.System;
    private bool _ocrKeepsLineBreaks = true;
    private bool _hideDesktopIcons = false;
    private bool _adjustAreaBeforeCapture = false;
    private QuickAccessSettings _quickAccess = new();

    public JsonSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(KnownFolders.AppData, "Lightshot", "settings.json");
        Load();
    }

    public string FilePath => _filePath;

    public ImageFormat DefaultFormat
    {
        get { lock (_lock) return _defaultFormat; }
        set { lock (_lock) { _defaultFormat = value; Save(); } }
    }

    public string SaveLocation
    {
        get { lock (_lock) return _saveLocation; }
        set { lock (_lock) { _saveLocation = value; Save(); } }
    }

    public string FilenamePattern
    {
        get { lock (_lock) return _filenamePattern; }
        set { lock (_lock) { _filenamePattern = value; Save(); } }
    }

    public HotkeyBindings Hotkeys
    {
        get { lock (_lock) return _hotkeys; }
        set { lock (_lock) { _hotkeys = value; Save(); } }
    }

    public bool OpenInEditor
    {
        get { lock (_lock) return _openInEditor; }
        set { lock (_lock) { _openInEditor = value; Save(); } }
    }

    public bool IncludeCursor
    {
        get { lock (_lock) return _includeCursor; }
        set { lock (_lock) { _includeCursor = value; Save(); } }
    }

    public double CaptureDelay
    {
        get { lock (_lock) return _captureDelay; }
        set { lock (_lock) { _captureDelay = value; Save(); } }
    }

    public int HistoryRetention
    {
        get { lock (_lock) return _historyRetention; }
        set { lock (_lock) { _historyRetention = value; Save(); } }
    }

    public bool LaunchAtLogin
    {
        get { lock (_lock) return _launchAtLogin; }
        set { lock (_lock) { _launchAtLogin = value; Save(); } }
    }

    public RecordingDefaults RecordingDefaults
    {
        get { lock (_lock) return _recordingDefaults; }
        set { lock (_lock) { _recordingDefaults = value; Save(); } }
    }

    public bool RememberLastRecordingArea
    {
        get { lock (_lock) return _rememberLastRecordingArea; }
        set { lock (_lock) { _rememberLastRecordingArea = value; Save(); } }
    }

    public CaptureRegion? LastRecordingRegion
    {
        get { lock (_lock) return _lastRecordingRegion; }
        set { lock (_lock) { _lastRecordingRegion = value; Save(); } }
    }

    public AppearancePreference Appearance
    {
        get { lock (_lock) return _appearance; }
        set { lock (_lock) { _appearance = value; Save(); } }
    }

    public bool OcrKeepsLineBreaks
    {
        get { lock (_lock) return _ocrKeepsLineBreaks; }
        set { lock (_lock) { _ocrKeepsLineBreaks = value; Save(); } }
    }

    public bool HideDesktopIcons
    {
        get { lock (_lock) return _hideDesktopIcons; }
        set { lock (_lock) { _hideDesktopIcons = value; Save(); } }
    }

    public bool AdjustAreaBeforeCapture
    {
        get { lock (_lock) return _adjustAreaBeforeCapture; }
        set { lock (_lock) { _adjustAreaBeforeCapture = value; Save(); } }
    }

    public QuickAccessSettings QuickAccess
    {
        get { lock (_lock) return _quickAccess; }
        set { lock (_lock) { _quickAccess = value; Save(); } }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var hotkeyMap = new Dictionary<string, HotkeyDto>();
                foreach (var (action, binding) in _hotkeys.Assignments)
                {
                    hotkeyMap[action.ToString()] = new HotkeyDto(binding.KeyCode, (int)binding.Modifiers, binding.KeyLabel);
                }

                string formatStr = _defaultFormat switch
                {
                    ImageFormat.Jpeg => "jpeg",
                    _ => "png"
                };

                double? jpegQuality = _defaultFormat is ImageFormat.Jpeg jp ? jp.Quality : null;

                var data = new SettingsDto(
                    DefaultFormat: formatStr,
                    JpegQuality: jpegQuality,
                    SaveLocation: _saveLocation,
                    FilenamePattern: _filenamePattern,
                    Hotkeys: hotkeyMap,
                    OpenInEditor: _openInEditor,
                    IncludeCursor: _includeCursor,
                    CaptureDelay: _captureDelay,
                    HistoryRetention: _historyRetention,
                    LaunchAtLogin: _launchAtLogin,
                    RememberLastRecordingArea: _rememberLastRecordingArea,
                    Appearance: _appearance.ToString(),
                    OcrKeepsLineBreaks: _ocrKeepsLineBreaks,
                    HideDesktopIcons: _hideDesktopIcons,
                    AdjustAreaBeforeCapture: _adjustAreaBeforeCapture
                );

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);
                AtomicFile.WriteAllText(_filePath, json);
            }
            catch
            {
                // Non-fatal if disk write fails
            }
        }
    }

    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                var data = JsonSerializer.Deserialize<SettingsDto>(json);
                if (data == null) return;

                if (data.DefaultFormat == "jpeg" || data.DefaultFormat == "jpg")
                {
                    _defaultFormat = new ImageFormat.Jpeg(data.JpegQuality ?? 0.85);
                }
                else
                {
                    _defaultFormat = new ImageFormat.Png();
                }

                if (!string.IsNullOrWhiteSpace(data.SaveLocation))
                {
                    _saveLocation = data.SaveLocation;
                }

                if (!string.IsNullOrWhiteSpace(data.FilenamePattern))
                {
                    _filenamePattern = data.FilenamePattern;
                }

                if (data.Hotkeys != null)
                {
                    var assignments = new Dictionary<CaptureAction, HotkeyBinding>();
                    foreach (var (key, dto) in data.Hotkeys)
                    {
                        if (Enum.TryParse<CaptureAction>(key, out var action))
                        {
                            assignments[action] = new HotkeyBinding(dto.KeyCode, (HotkeyModifiers)dto.Modifiers, dto.KeyLabel);
                        }
                    }
                    _hotkeys = new HotkeyBindings(assignments);
                }

                if (data.OpenInEditor.HasValue) _openInEditor = data.OpenInEditor.Value;
                if (data.IncludeCursor.HasValue) _includeCursor = data.IncludeCursor.Value;
                if (data.CaptureDelay.HasValue) _captureDelay = data.CaptureDelay.Value;
                if (data.HistoryRetention.HasValue) _historyRetention = data.HistoryRetention.Value;
                if (data.LaunchAtLogin.HasValue) _launchAtLogin = data.LaunchAtLogin.Value;
                if (data.RememberLastRecordingArea.HasValue) _rememberLastRecordingArea = data.RememberLastRecordingArea.Value;

                if (!string.IsNullOrEmpty(data.Appearance) && Enum.TryParse<AppearancePreference>(data.Appearance, true, out var app))
                {
                    _appearance = app;
                }

                if (data.OcrKeepsLineBreaks.HasValue) _ocrKeepsLineBreaks = data.OcrKeepsLineBreaks.Value;
                if (data.HideDesktopIcons.HasValue) _hideDesktopIcons = data.HideDesktopIcons.Value;
                if (data.AdjustAreaBeforeCapture.HasValue) _adjustAreaBeforeCapture = data.AdjustAreaBeforeCapture.Value;
            }
            catch
            {
                // Fall back to defaults on corrupt config
            }
        }
    }

    private record SettingsDto(
        string? DefaultFormat,
        double? JpegQuality,
        string? SaveLocation,
        string? FilenamePattern,
        Dictionary<string, HotkeyDto>? Hotkeys,
        bool? OpenInEditor,
        bool? IncludeCursor,
        double? CaptureDelay,
        int? HistoryRetention,
        bool? LaunchAtLogin,
        bool? RememberLastRecordingArea,
        string? Appearance,
        bool? OcrKeepsLineBreaks,
        bool? HideDesktopIcons,
        bool? AdjustAreaBeforeCapture
    );

    private record HotkeyDto(ushort KeyCode, int Modifiers, string KeyLabel);
}
