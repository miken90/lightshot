// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lightshot.Core;
using Lightshot.Platform.Windows.Files;

namespace Lightshot.Platform.Windows.Settings;

/// <summary>
/// Provides additive-only migrations for Lightshot settings.
/// Guarantees that existing user configurations are never overwritten or erased,
/// while all missing keys from APP §5 are populated with their defaults.
/// </summary>
public static class SettingsMigrations
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Migrates a JSON string by adding missing keys without modifying existing ones.
    /// </summary>
    public static string MigrateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            var freshObj = new JsonObject();
            PopulateMissingKeys(freshObj);
            return freshObj.ToJsonString(JsonOptions);
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch
        {
            // If corrupt, fall back to defaults
            var freshObj = new JsonObject();
            PopulateMissingKeys(freshObj);
            return freshObj.ToJsonString(JsonOptions);
        }

        if (rootNode is not JsonObject jsonObject)
        {
            var freshObj = new JsonObject();
            PopulateMissingKeys(freshObj);
            return freshObj.ToJsonString(JsonOptions);
        }

        PopulateMissingKeys(jsonObject);
        return jsonObject.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// Migrates a settings file on disk atomically.
    /// </summary>
    public static void MigrateFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            var freshObj = new JsonObject();
            PopulateMissingKeys(freshObj);
            AtomicFile.WriteAllText(filePath, freshObj.ToJsonString(JsonOptions));
            return;
        }

        string content = File.ReadAllText(filePath);
        string migrated = MigrateJson(content);
        AtomicFile.WriteAllText(filePath, migrated);
    }

    /// <summary>
    /// Ensures all keys from APP §5 are present in the JsonObject.
    /// Never mutates or removes any existing key.
    /// </summary>
    public static void PopulateMissingKeys(JsonObject obj)
    {
        // 1. save.format
        if (!obj.ContainsKey(SettingsKeys.SaveFormat))
        {
            // Check legacy Phase 4 key if present
            if (obj.TryGetPropertyValue("DefaultFormat", out var legacyFormat) && legacyFormat != null)
            {
                obj[SettingsKeys.SaveFormat] = legacyFormat.DeepClone();
            }
            else
            {
                obj[SettingsKeys.SaveFormat] = SettingsKeys.DefaultFormat;
            }
        }

        // 2. save.jpegQuality
        if (!obj.ContainsKey(SettingsKeys.SaveJpegQuality))
        {
            if (obj.TryGetPropertyValue("JpegQuality", out var legacyQuality) && legacyQuality != null)
            {
                obj[SettingsKeys.SaveJpegQuality] = legacyQuality.DeepClone();
            }
            else
            {
                obj[SettingsKeys.SaveJpegQuality] = SettingsKeys.DefaultJpegQuality;
            }
        }

        // 3. save.location
        if (!obj.ContainsKey(SettingsKeys.SaveLocation))
        {
            if (obj.TryGetPropertyValue("SaveLocation", out var legacyLoc) && legacyLoc != null)
            {
                obj[SettingsKeys.SaveLocation] = legacyLoc.DeepClone();
            }
            else
            {
                obj[SettingsKeys.SaveLocation] = SettingsKeys.DefaultSaveLocation;
            }
        }

        // 4. save.filenamePattern
        if (!obj.ContainsKey(SettingsKeys.SaveFilenamePattern))
        {
            if (obj.TryGetPropertyValue("FilenamePattern", out var legacyPat) && legacyPat != null)
            {
                obj[SettingsKeys.SaveFilenamePattern] = legacyPat.DeepClone();
            }
            else
            {
                obj[SettingsKeys.SaveFilenamePattern] = SettingsKeys.DefaultFilenamePattern;
            }
        }

        // 5. capture.hotkeys
        if (!obj.ContainsKey(SettingsKeys.CaptureHotkeys))
        {
            if (obj.TryGetPropertyValue("Hotkeys", out var legacyHotkeys) && legacyHotkeys != null)
            {
                obj[SettingsKeys.CaptureHotkeys] = legacyHotkeys.DeepClone();
            }
            else
            {
                var hotkeyMap = new JsonObject();
                foreach (var (action, binding) in HotkeyBindings.Defaults.Assignments)
                {
                    var dto = new JsonObject
                    {
                        ["KeyCode"] = binding.KeyCode,
                        ["Modifiers"] = (int)binding.Modifiers,
                        ["KeyLabel"] = binding.KeyLabel
                    };
                    hotkeyMap[action.ToString()] = dto;
                }
                obj[SettingsKeys.CaptureHotkeys] = hotkeyMap;
            }
        }

        // 6. capture.openInEditor
        if (!obj.ContainsKey(SettingsKeys.CaptureOpenInEditor))
        {
            if (obj.TryGetPropertyValue("OpenInEditor", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.CaptureOpenInEditor] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.CaptureOpenInEditor] = SettingsKeys.DefaultOpenInEditor;
        }

        // 7. capture.includeCursor
        if (!obj.ContainsKey(SettingsKeys.CaptureIncludeCursor))
        {
            if (obj.TryGetPropertyValue("IncludeCursor", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.CaptureIncludeCursor] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.CaptureIncludeCursor] = SettingsKeys.DefaultIncludeCursor;
        }

        // 8. capture.adjustAreaBeforeCapture
        if (!obj.ContainsKey(SettingsKeys.CaptureAdjustAreaBeforeCapture))
        {
            if (obj.TryGetPropertyValue("AdjustAreaBeforeCapture", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.CaptureAdjustAreaBeforeCapture] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.CaptureAdjustAreaBeforeCapture] = SettingsKeys.DefaultAdjustAreaBeforeCapture;
        }

        // 9. capture.delay
        if (!obj.ContainsKey(SettingsKeys.CaptureDelay))
        {
            if (obj.TryGetPropertyValue("CaptureDelay", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.CaptureDelay] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.CaptureDelay] = SettingsKeys.DefaultCaptureDelay;
        }

        // 10. history.retention
        if (!obj.ContainsKey(SettingsKeys.HistoryRetention))
        {
            if (obj.TryGetPropertyValue("HistoryRetention", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.HistoryRetention] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.HistoryRetention] = SettingsKeys.DefaultHistoryRetention;
        }

        // 11. recording.defaults
        if (!obj.ContainsKey(SettingsKeys.RecordingDefaults))
        {
            if (obj.TryGetPropertyValue("RecordingDefaults", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.RecordingDefaults] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.RecordingDefaults] = JsonSerializer.SerializeToNode(new RecordingDefaults());
        }

        // 12. recording.rememberLastArea
        if (!obj.ContainsKey(SettingsKeys.RecordingRememberLastArea))
        {
            if (obj.TryGetPropertyValue("RememberLastRecordingArea", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.RecordingRememberLastArea] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.RecordingRememberLastArea] = SettingsKeys.DefaultRememberLastArea;
        }

        // 13. recording.lastRegion (absent by default, do nothing if not present)

        // 14. app.appearance
        if (!obj.ContainsKey(SettingsKeys.AppAppearance))
        {
            if (obj.TryGetPropertyValue("Appearance", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.AppAppearance] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.AppAppearance] = SettingsKeys.DefaultAppearance;
        }

        // 15. ocr.keepLineBreaks
        if (!obj.ContainsKey(SettingsKeys.OcrKeepLineBreaks))
        {
            if (obj.TryGetPropertyValue("OcrKeepsLineBreaks", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.OcrKeepLineBreaks] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.OcrKeepLineBreaks] = SettingsKeys.DefaultOcrKeepLineBreaks;
        }

        // 16. app.hideDesktopIcons
        if (!obj.ContainsKey(SettingsKeys.AppHideDesktopIcons))
        {
            if (obj.TryGetPropertyValue("HideDesktopIcons", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.AppHideDesktopIcons] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.AppHideDesktopIcons] = SettingsKeys.DefaultHideDesktopIcons;
        }

        // 17. quickAccess.settings
        if (!obj.ContainsKey(SettingsKeys.QuickAccessSettings))
        {
            if (obj.TryGetPropertyValue("QuickAccess", out var legacyVal) && legacyVal != null)
                obj[SettingsKeys.QuickAccessSettings] = legacyVal.DeepClone();
            else
                obj[SettingsKeys.QuickAccessSettings] = JsonSerializer.SerializeToNode(new QuickAccessSettings());
        }
    }
}
