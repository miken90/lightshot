// Ported from App/Sources/UserDefaultsSettingsStore.swift and LightshotKit
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.Platform.Windows.Files;

namespace Lightshot.Platform.Windows.Settings;

/// <summary>
/// Defines all persistent configuration keys specified in APP §5 and their default values.
/// </summary>
public static class SettingsKeys
{
    public const string SaveFormat = "save.format";
    public const string SaveJpegQuality = "save.jpegQuality";
    public const string SaveLocation = "save.location";
    public const string SaveFilenamePattern = "save.filenamePattern";
    public const string CaptureHotkeys = "capture.hotkeys";
    public const string CaptureOpenInEditor = "capture.openInEditor";
    public const string CaptureIncludeCursor = "capture.includeCursor";
    public const string CaptureAdjustAreaBeforeCapture = "capture.adjustAreaBeforeCapture";
    public const string CaptureDelay = "capture.delay";
    public const string HistoryRetention = "history.retention";
    public const string RecordingDefaults = "recording.defaults";
    public const string RecordingRememberLastArea = "recording.rememberLastArea";
    public const string RecordingLastRegion = "recording.lastRegion";
    public const string AppAppearance = "app.appearance";
    public const string OcrKeepLineBreaks = "ocr.keepLineBreaks";
    public const string AppHideDesktopIcons = "app.hideDesktopIcons";
    public const string QuickAccessSettings = "quickAccess.settings";
    public const string AfterCaptureSettings = "capture.afterCapture";

    // Custom string key: must stay out of AllKeys (Load rebuilds custom settings only for keys not in AllKeys)
    public const string AppOnboarded = "app.onboarded";

    // Phase 4 custom setting preserved for editor state
    public const string EditorLastArrowStyle = "editor.lastArrowStyle";

    // Custom string keys read through ISettingsStore.GetSetting/SetSetting. They must stay out of AllKeys:
    // JsonSettingsStore reloads root keys only when they are not in AllKeys (JsonSettingsStore.cs:612-635).
    public const string UpdateEnabled = "update.enabled";            // "true" (default when absent) or "false"
    public const string UpdateSequence = "update.sequence";          // sequence floor of the applied release
    public const string UpdatePendingVersion = "update.pendingVersion";
    public const string UpdatePendingSequence = "update.pendingSequence";
    public const string AppLaunchCount = "app.launchCount";          // installed launches, for second-launch consent

    // Default values
    public const string DefaultFormat = "png";
    public const double DefaultJpegQuality = 0.9;
    public static string DefaultSaveLocation => KnownFolders.DefaultSaveLocation;
    public const string DefaultFilenamePattern = "Screenshot %Y-%m-%d at %H.%M.%S";
    public const bool DefaultOpenInEditor = true;
    public const bool DefaultIncludeCursor = false;
    public const bool DefaultAdjustAreaBeforeCapture = false;
    public const double DefaultCaptureDelay = 0.0;
    public const int DefaultHistoryRetention = 50;
    public const bool DefaultRememberLastArea = false;
    public const string DefaultAppearance = "system";
    public const bool DefaultOcrKeepLineBreaks = true;
    public const bool DefaultHideDesktopIcons = false;
    public const bool DefaultAppOnboarded = false;

    /// <summary>
    /// Enumeration of all primary keys from APP §5.
    /// </summary>
    public static readonly IReadOnlyList<string> AllKeys =
    [
        SaveFormat,
        SaveJpegQuality,
        SaveLocation,
        SaveFilenamePattern,
        CaptureHotkeys,
        CaptureOpenInEditor,
        CaptureIncludeCursor,
        CaptureAdjustAreaBeforeCapture,
        CaptureDelay,
        HistoryRetention,
        RecordingDefaults,
        RecordingRememberLastArea,
        RecordingLastRegion,
        AppAppearance,
        OcrKeepLineBreaks,
        AppHideDesktopIcons,
        QuickAccessSettings,
        AfterCaptureSettings
    ];
}
