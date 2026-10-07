// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lightshot.Core;
using Lightshot.Platform.Windows.Files;
using Lightshot.Platform.Windows.Startup;

namespace Lightshot.Platform.Windows.Settings;

/// <summary>
/// File-backed ISettingsStore persisting configuration as JSON via AtomicFile.
/// Fully conforms to APP §5 settings keys, additive migrations, change notifications,
/// and graceful fallback for unknown or missing keys.
/// </summary>
public class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly object _lock = new();

    private ImageFormat _defaultFormat = new ImageFormat.Png();
    private string _saveLocation = KnownFolders.DefaultSaveLocation;
    private string _filenamePattern = SettingsKeys.DefaultFilenamePattern;
    private HotkeyBindings _hotkeys = HotkeyBindings.Defaults;
    private bool _openInEditor = SettingsKeys.DefaultOpenInEditor;
    private bool _includeCursor = SettingsKeys.DefaultIncludeCursor;
    private double _captureDelay = SettingsKeys.DefaultCaptureDelay;
    private int _historyRetention = SettingsKeys.DefaultHistoryRetention;
    private int _historyMaxAgeDays = SettingsKeys.DefaultHistoryMaxAgeDays;
    private bool _launchAtLoginFallback = false;
    private RecordingDefaults _recordingDefaults = new();
    private bool _rememberLastRecordingArea = SettingsKeys.DefaultRememberLastArea;
    private CaptureRegion? _lastRecordingRegion = null;
    private AppearancePreference _appearance = AppearancePreference.System;
    private bool _ocrKeepsLineBreaks = SettingsKeys.DefaultOcrKeepLineBreaks;
    private bool _hideDesktopIcons = SettingsKeys.DefaultHideDesktopIcons;
    private bool _adjustAreaBeforeCapture = SettingsKeys.DefaultAdjustAreaBeforeCapture;
    private QuickAccessSettings _quickAccess = new();
    private AfterCaptureSettings _afterCapture = new();

    private readonly Dictionary<string, string?> _customSettings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Event raised whenever any setting is modified.
    /// </summary>
    public event EventHandler<string>? SettingChanged;

    /// <summary>
    /// Action notification callback for setting changes.
    /// </summary>
    public event Action<string>? Changed;

    public JsonSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(KnownFolders.AppData, "Lightshot", "settings.json");
        Load();
    }

    public string FilePath => _filePath;

    public ImageFormat DefaultFormat
    {
        get { lock (_lock) return _defaultFormat; }
        set
        {
            lock (_lock)
            {
                if (Equals(_defaultFormat, value)) return;
                _defaultFormat = value;
                Save();
            }
            NotifyChanged(SettingsKeys.SaveFormat);
        }
    }

    public string SaveLocation
    {
        get { lock (_lock) return _saveLocation; }
        set
        {
            lock (_lock)
            {
                if (_saveLocation == value) return;
                _saveLocation = value;
                Save();
            }
            NotifyChanged(SettingsKeys.SaveLocation);
        }
    }

    public string FilenamePattern
    {
        get { lock (_lock) return _filenamePattern; }
        set
        {
            lock (_lock)
            {
                if (_filenamePattern == value) return;
                _filenamePattern = value;
                Save();
            }
            NotifyChanged(SettingsKeys.SaveFilenamePattern);
        }
    }

    public HotkeyBindings Hotkeys
    {
        get { lock (_lock) return _hotkeys; }
        set
        {
            lock (_lock)
            {
                if (Equals(_hotkeys, value)) return;
                _hotkeys = value;
                Save();
            }
            NotifyChanged(SettingsKeys.CaptureHotkeys);
        }
    }

    public bool OpenInEditor
    {
        get { lock (_lock) return _openInEditor; }
        set
        {
            lock (_lock)
            {
                if (_openInEditor == value) return;
                _openInEditor = value;
                Save();
            }
            NotifyChanged(SettingsKeys.CaptureOpenInEditor);
        }
    }

    public bool IncludeCursor
    {
        get { lock (_lock) return _includeCursor; }
        set
        {
            lock (_lock)
            {
                if (_includeCursor == value) return;
                _includeCursor = value;
                Save();
            }
            NotifyChanged(SettingsKeys.CaptureIncludeCursor);
        }
    }

    public double CaptureDelay
    {
        get { lock (_lock) return _captureDelay; }
        set
        {
            lock (_lock)
            {
                double clamped = Math.Max(0, value);
                if (Math.Abs(_captureDelay - clamped) < 0.0001) return;
                _captureDelay = clamped;
                Save();
            }
            NotifyChanged(SettingsKeys.CaptureDelay);
        }
    }

    public int HistoryRetention
    {
        get { lock (_lock) return _historyRetention; }
        set
        {
            lock (_lock)
            {
                int clamped = Math.Max(0, value);
                if (_historyRetention == clamped) return;
                _historyRetention = clamped;
                Save();
            }
            NotifyChanged(SettingsKeys.HistoryRetention);
        }
    }

    public int HistoryMaxAgeDays
    {
        get { lock (_lock) return _historyMaxAgeDays; }
        set
        {
            lock (_lock)
            {
                int clamped = Math.Clamp(value, 0, 3650);
                if (_historyMaxAgeDays == clamped) return;
                _historyMaxAgeDays = clamped;
                Save();
            }
            NotifyChanged(SettingsKeys.HistoryMaxAgeDays);
        }
    }

    /// <summary>
    /// Source of truth is the real HKCU Run login-item registration (APP §5).
    /// </summary>
    public bool LaunchAtLogin
    {
        get
        {
            try
            {
                return LaunchAtLoginHelper.IsEnabled();
            }
            catch
            {
                lock (_lock) return _launchAtLoginFallback;
            }
        }
        set
        {
            try
            {
                LaunchAtLoginHelper.SetEnabled(value);
            }
            catch
            {
                lock (_lock) { _launchAtLoginFallback = value; Save(); }
            }
            NotifyChanged("launchAtLogin");
        }
    }

    public RecordingDefaults RecordingDefaults
    {
        get { lock (_lock) return _recordingDefaults; }
        set
        {
            lock (_lock)
            {
                _recordingDefaults = value;
                Save();
            }
            NotifyChanged(SettingsKeys.RecordingDefaults);
        }
    }

    public bool RememberLastRecordingArea
    {
        get { lock (_lock) return _rememberLastRecordingArea; }
        set
        {
            lock (_lock)
            {
                if (_rememberLastRecordingArea == value) return;
                _rememberLastRecordingArea = value;
                Save();
            }
            NotifyChanged(SettingsKeys.RecordingRememberLastArea);
        }
    }

    public CaptureRegion? LastRecordingRegion
    {
        get { lock (_lock) return _lastRecordingRegion; }
        set
        {
            lock (_lock)
            {
                if (Equals(_lastRecordingRegion, value)) return;
                _lastRecordingRegion = value;
                Save();
            }
            NotifyChanged(SettingsKeys.RecordingLastRegion);
        }
    }

    public AppearancePreference Appearance
    {
        get { lock (_lock) return _appearance; }
        set
        {
            lock (_lock)
            {
                if (_appearance == value) return;
                _appearance = value;
                Save();
            }
            NotifyChanged(SettingsKeys.AppAppearance);
        }
    }

    public bool OcrKeepsLineBreaks
    {
        get { lock (_lock) return _ocrKeepsLineBreaks; }
        set
        {
            lock (_lock)
            {
                if (_ocrKeepsLineBreaks == value) return;
                _ocrKeepsLineBreaks = value;
                Save();
            }
            NotifyChanged(SettingsKeys.OcrKeepLineBreaks);
        }
    }

    public bool HideDesktopIcons
    {
        get { lock (_lock) return _hideDesktopIcons; }
        set
        {
            lock (_lock)
            {
                if (_hideDesktopIcons == value) return;
                _hideDesktopIcons = value;
                Save();
            }
            NotifyChanged(SettingsKeys.AppHideDesktopIcons);
        }
    }

    public bool AdjustAreaBeforeCapture
    {
        get { lock (_lock) return _adjustAreaBeforeCapture; }
        set
        {
            lock (_lock)
            {
                if (_adjustAreaBeforeCapture == value) return;
                _adjustAreaBeforeCapture = value;
                Save();
            }
            NotifyChanged(SettingsKeys.CaptureAdjustAreaBeforeCapture);
        }
    }

    public QuickAccessSettings QuickAccess
    {
        get { lock (_lock) return _quickAccess; }
        set
        {
            lock (_lock)
            {
                _quickAccess = value;
                Save();
            }
            NotifyChanged(SettingsKeys.QuickAccessSettings);
        }
    }

    public AfterCaptureSettings AfterCapture
    {
        get { lock (_lock) return _afterCapture; }
        set
        {
            lock (_lock)
            {
                if (_afterCapture == value) return;
                _afterCapture = value;
                Save();
            }
            NotifyChanged(SettingsKeys.AfterCaptureSettings);
        }
    }

    public string? GetSetting(string key)
    {
        lock (_lock)
        {
            return _customSettings.TryGetValue(key, out var val) ? val : null;
        }
    }

    public void SetSetting(string key, string? value)
    {
        lock (_lock)
        {
            if (value == null)
            {
                _customSettings.Remove(key);
            }
            else
            {
                _customSettings[key] = value;
            }
            Save();
        }
        NotifyChanged(key);
    }

    private void NotifyChanged(string key)
    {
        try
        {
            SettingChanged?.Invoke(this, key);
            Changed?.Invoke(key);
        }
        catch
        {
            // Subscribers should not break the store
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var root = new JsonObject();

                // 1. save.format and save.jpegQuality
                string formatStr = _defaultFormat is ImageFormat.Jpeg ? "jpeg" : "png";
                root[SettingsKeys.SaveFormat] = formatStr;
                if (_defaultFormat is ImageFormat.Jpeg jp)
                {
                    root[SettingsKeys.SaveJpegQuality] = Math.Clamp(jp.Quality, 0.0, 1.0);
                }
                else
                {
                    root[SettingsKeys.SaveJpegQuality] = SettingsKeys.DefaultJpegQuality;
                }

                // 2. save.location
                root[SettingsKeys.SaveLocation] = _saveLocation;

                // 3. save.filenamePattern
                root[SettingsKeys.SaveFilenamePattern] = _filenamePattern;

                // 4. capture.hotkeys
                var hotkeyMap = new JsonObject();
                foreach (var (action, binding) in _hotkeys.Assignments)
                {
                    var dto = new JsonObject
                    {
                        ["KeyCode"] = binding.KeyCode,
                        ["Modifiers"] = (int)binding.Modifiers,
                        ["KeyLabel"] = binding.KeyLabel
                    };
                    hotkeyMap[action.ToString()] = dto;
                }
                root[SettingsKeys.CaptureHotkeys] = hotkeyMap;

                // 5. capture toggles
                root[SettingsKeys.CaptureOpenInEditor] = _openInEditor;
                root[SettingsKeys.CaptureIncludeCursor] = _includeCursor;
                root[SettingsKeys.CaptureAdjustAreaBeforeCapture] = _adjustAreaBeforeCapture;
                root[SettingsKeys.CaptureDelay] = _captureDelay;

                // 6. history
                root[SettingsKeys.HistoryRetention] = _historyRetention;
                root[SettingsKeys.HistoryMaxAgeDays] = _historyMaxAgeDays;

                // 7. recording
                root[SettingsKeys.RecordingDefaults] = JsonSerializer.SerializeToNode(_recordingDefaults);
                root[SettingsKeys.RecordingRememberLastArea] = _rememberLastRecordingArea;
                if (_lastRecordingRegion != null)
                {
                    root[SettingsKeys.RecordingLastRegion] = JsonSerializer.SerializeToNode(_lastRecordingRegion);
                }

                // 8. appearance
                root[SettingsKeys.AppAppearance] = _appearance switch
                {
                    AppearancePreference.Light => "light",
                    AppearancePreference.Dark => "dark",
                    _ => "system"
                };

                // 9. OCR & Desktop
                root[SettingsKeys.OcrKeepLineBreaks] = _ocrKeepsLineBreaks;
                root[SettingsKeys.AppHideDesktopIcons] = _hideDesktopIcons;

                // 10. Quick access & After capture
                root[SettingsKeys.QuickAccessSettings] = JsonSerializer.SerializeToNode(_quickAccess);
                root[SettingsKeys.AfterCaptureSettings] = JsonSerializer.SerializeToNode(_afterCapture);

                // 11. Custom settings (e.g. editor.lastArrowStyle)
                foreach (var (k, v) in _customSettings)
                {
                    root[k] = v;
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = root.ToJsonString(options);
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
            // Reset to defaults first
            ResetToDefaults();

            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json)) return;

                var root = JsonNode.Parse(json) as JsonObject;
                if (root == null) return;

                // 1. save.format & save.jpegQuality
                string? formatStr = root[SettingsKeys.SaveFormat]?.GetValue<string>()
                    ?? root["DefaultFormat"]?.GetValue<string>();
                double? jpegQuality = root[SettingsKeys.SaveJpegQuality]?.GetValue<double>()
                    ?? root["JpegQuality"]?.GetValue<double>();

                if (string.Equals(formatStr, "jpeg", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(formatStr, "jpg", StringComparison.OrdinalIgnoreCase))
                {
                    _defaultFormat = new ImageFormat.Jpeg(jpegQuality.HasValue ? Math.Clamp(jpegQuality.Value, 0.0, 1.0) : 0.9);
                }
                else
                {
                    _defaultFormat = new ImageFormat.Png();
                }

                // 2. save.location
                string? loc = root[SettingsKeys.SaveLocation]?.GetValue<string>()
                    ?? root["SaveLocation"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(loc))
                {
                    _saveLocation = loc;
                }

                // 3. save.filenamePattern
                string? pattern = root[SettingsKeys.SaveFilenamePattern]?.GetValue<string>()
                    ?? root["FilenamePattern"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    _filenamePattern = pattern;
                }

                // 4. capture.hotkeys
                var hotkeyNode = root[SettingsKeys.CaptureHotkeys] ?? root["Hotkeys"];
                if (hotkeyNode is JsonObject hotkeyObj)
                {
                    var assignments = new Dictionary<CaptureAction, HotkeyBinding>();
                    foreach (var (k, v) in hotkeyObj)
                    {
                        if (Enum.TryParse<CaptureAction>(k, true, out var action) && v is JsonObject itemObj)
                        {
                            ushort keyCode = itemObj["KeyCode"]?.GetValue<ushort>() ?? 0;
                            int modifiers = itemObj["Modifiers"]?.GetValue<int>() ?? 0;
                            string label = itemObj["KeyLabel"]?.GetValue<string>() ?? "";
                            assignments[action] = new HotkeyBinding(keyCode, (HotkeyModifiers)modifiers, label);
                        }
                    }
                    if (assignments.Count > 0)
                    {
                        _hotkeys = new HotkeyBindings(assignments);
                    }
                }

                // 5. capture toggles
                if (TryGetBool(root, SettingsKeys.CaptureOpenInEditor, "OpenInEditor", out bool openInEditor))
                    _openInEditor = openInEditor;

                if (TryGetBool(root, SettingsKeys.CaptureIncludeCursor, "IncludeCursor", out bool includeCursor))
                    _includeCursor = includeCursor;

                if (TryGetBool(root, SettingsKeys.CaptureAdjustAreaBeforeCapture, "AdjustAreaBeforeCapture", out bool adjustArea))
                    _adjustAreaBeforeCapture = adjustArea;

                if (TryGetDouble(root, SettingsKeys.CaptureDelay, "CaptureDelay", out double delay))
                    _captureDelay = Math.Max(0, delay);

                // 6. history
                if (TryGetInt(root, SettingsKeys.HistoryRetention, "HistoryRetention", out int retention))
                    _historyRetention = Math.Max(0, retention);

                if (TryGetInt(root, SettingsKeys.HistoryMaxAgeDays, "HistoryMaxAgeDays", out int maxAgeDays))
                    _historyMaxAgeDays = Math.Clamp(maxAgeDays, 0, 3650);

                // 7. recording
                var recDefaultsNode = root[SettingsKeys.RecordingDefaults] ?? root["RecordingDefaults"];
                if (recDefaultsNode != null)
                {
                    try
                    {
                        var rec = JsonSerializer.Deserialize<RecordingDefaults>(recDefaultsNode.ToJsonString());
                        if (rec != null) _recordingDefaults = rec;
                    }
                    catch { /* keep default */ }
                }

                if (TryGetBool(root, SettingsKeys.RecordingRememberLastArea, "RememberLastRecordingArea", out bool rememberArea))
                    _rememberLastRecordingArea = rememberArea;

                var lastRegNode = root[SettingsKeys.RecordingLastRegion] ?? root["LastRecordingRegion"];
                if (lastRegNode != null)
                {
                    try
                    {
                        _lastRecordingRegion = JsonSerializer.Deserialize<CaptureRegion>(lastRegNode.ToJsonString());
                    }
                    catch { /* keep null */ }
                }

                // 8. appearance
                string? appStr = root[SettingsKeys.AppAppearance]?.GetValue<string>()
                    ?? root["Appearance"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(appStr) && Enum.TryParse<AppearancePreference>(appStr, true, out var appPref))
                {
                    _appearance = appPref;
                }

                // 9. OCR & Desktop
                if (TryGetBool(root, SettingsKeys.OcrKeepLineBreaks, "OcrKeepsLineBreaks", out bool ocrLines))
                    _ocrKeepsLineBreaks = ocrLines;

                if (TryGetBool(root, SettingsKeys.AppHideDesktopIcons, "HideDesktopIcons", out bool hideIcons))
                    _hideDesktopIcons = hideIcons;

                // 10. Quick access & After capture
                var qaNode = root[SettingsKeys.QuickAccessSettings] ?? root["QuickAccess"];
                if (qaNode != null)
                {
                    try
                    {
                        var qa = JsonSerializer.Deserialize<QuickAccessSettings>(qaNode.ToJsonString());
                        if (qa != null) _quickAccess = qa;
                    }
                    catch { /* keep default */ }
                }

                var acNode = root[SettingsKeys.AfterCaptureSettings] ?? root["AfterCapture"];
                if (acNode != null)
                {
                    try
                    {
                        var ac = JsonSerializer.Deserialize<AfterCaptureSettings>(acNode.ToJsonString());
                        if (ac != null) _afterCapture = ac;
                    }
                    catch { /* keep default */ }
                }

                // 11. Custom settings (including editor.lastArrowStyle)
                static string? AsSettingString(JsonNode? v) =>
                    v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v?.ToJsonString();

                _customSettings.Clear();
                if (root["CustomSettings"] is JsonObject customObj)
                {
                    foreach (var (k, v) in customObj)
                    {
                        _customSettings[k] = AsSettingString(v);
                    }
                }

                // Scan all root properties: any non-standard key is stored in custom settings
                foreach (var (k, v) in root)
                {
                    if (!SettingsKeys.AllKeys.Contains(k, StringComparer.OrdinalIgnoreCase) &&
                        !string.Equals(k, "CustomSettings", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "DefaultFormat", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "JpegQuality", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "SaveLocation", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "FilenamePattern", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "Hotkeys", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "OpenInEditor", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "IncludeCursor", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "AdjustAreaBeforeCapture", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "CaptureDelay", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "HistoryRetention", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "HistoryMaxAgeDays", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "RecordingDefaults", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "RememberLastRecordingArea", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "LastRecordingRegion", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "Appearance", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "OcrKeepsLineBreaks", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "HideDesktopIcons", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "QuickAccess", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "AfterCapture", StringComparison.OrdinalIgnoreCase))
                    {
                        _customSettings[k] = AsSettingString(v);
                    }
                }
            }
            catch
            {
                // Fall back to defaults on corrupt config
                ResetToDefaults();
            }
        }
    }

    private void ResetToDefaults()
    {
        _defaultFormat = new ImageFormat.Png();
        _saveLocation = KnownFolders.DefaultSaveLocation;
        _filenamePattern = SettingsKeys.DefaultFilenamePattern;
        _hotkeys = HotkeyBindings.Defaults;
        _openInEditor = SettingsKeys.DefaultOpenInEditor;
        _includeCursor = SettingsKeys.DefaultIncludeCursor;
        _captureDelay = SettingsKeys.DefaultCaptureDelay;
        _historyRetention = SettingsKeys.DefaultHistoryRetention;
        _historyMaxAgeDays = SettingsKeys.DefaultHistoryMaxAgeDays;
        _recordingDefaults = new();
        _rememberLastRecordingArea = SettingsKeys.DefaultRememberLastArea;
        _lastRecordingRegion = null;
        _appearance = AppearancePreference.System;
        _ocrKeepsLineBreaks = SettingsKeys.DefaultOcrKeepLineBreaks;
        _hideDesktopIcons = SettingsKeys.DefaultHideDesktopIcons;
        _adjustAreaBeforeCapture = SettingsKeys.DefaultAdjustAreaBeforeCapture;
        _quickAccess = new();
        _afterCapture = new();
    }

    private static bool TryGetBool(JsonObject obj, string primaryKey, string legacyKey, out bool value)
    {
        if (obj[primaryKey] is JsonValue pv && pv.TryGetValue(out value)) return true;
        if (obj[legacyKey] is JsonValue lv && lv.TryGetValue(out value)) return true;
        value = default;
        return false;
    }

    private static bool TryGetInt(JsonObject obj, string primaryKey, string legacyKey, out int value)
    {
        if (obj[primaryKey] is JsonValue pv && pv.TryGetValue(out value)) return true;
        if (obj[legacyKey] is JsonValue lv && lv.TryGetValue(out value)) return true;
        value = default;
        return false;
    }

    private static bool TryGetDouble(JsonObject obj, string primaryKey, string legacyKey, out double value)
    {
        if (obj[primaryKey] is JsonValue pv && pv.TryGetValue(out value)) return true;
        if (obj[legacyKey] is JsonValue lv && lv.TryGetValue(out value)) return true;
        value = default;
        return false;
    }
}
