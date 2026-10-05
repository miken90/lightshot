// Ported from LightshotKit/Sources/LightshotKit/SettingsStore.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// User settings backing the application and capture/recording defaults.
/// </summary>
public interface ISettingsStore
{
    ImageFormat DefaultFormat { get; set; }
    string SaveLocation { get; set; }
    string FilenamePattern { get; set; }
    HotkeyBindings Hotkeys { get; set; }
    bool OpenInEditor { get; set; }
    bool IncludeCursor { get; set; }
    double CaptureDelay { get; set; }
    int HistoryRetention { get; set; }
    bool LaunchAtLogin { get; set; }
    RecordingDefaults RecordingDefaults { get; set; }
    bool RememberLastRecordingArea { get; set; }
    CaptureRegion? LastRecordingRegion { get; set; }
    AppearancePreference Appearance { get; set; }
    bool OcrKeepsLineBreaks { get; set; }
    bool HideDesktopIcons { get; set; }
    bool AdjustAreaBeforeCapture { get; set; }
}
