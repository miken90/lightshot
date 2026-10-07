// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeSettingsStore : ISettingsStore
{
    public ImageFormat DefaultFormat { get; set; } = new ImageFormat.Png();
    public string SaveLocation { get; set; } = "/tmp/shots";
    public string FilenamePattern { get; set; } = "shot-%Y";
    public HotkeyBindings Hotkeys { get; set; } = HotkeyBindings.Defaults;
    public bool OpenInEditor { get; set; } = true;
    public bool IncludeCursor { get; set; } = false;
    public double CaptureDelay { get; set; } = 0;
    public int HistoryRetention { get; set; } = 50;
    public bool LaunchAtLogin { get; set; } = false;
    public RecordingDefaults RecordingDefaults { get; set; } = new() { CountdownEnabled = false };
    public bool RememberLastRecordingArea { get; set; } = false;
    public CaptureRegion? LastRecordingRegion { get; set; }
    public AppearancePreference Appearance { get; set; } = AppearancePreference.System;
    public bool OcrKeepsLineBreaks { get; set; } = true;
    public bool HideDesktopIcons { get; set; } = false;
    public bool AdjustAreaBeforeCapture { get; set; } = false;
    public QuickAccessSettings QuickAccess { get; set; } = new();
    public AfterCaptureSettings AfterCapture { get; set; } = new();

    private readonly System.Collections.Generic.Dictionary<string, string?> _customSettings = new();
    public string? GetSetting(string key) => _customSettings.TryGetValue(key, out var val) ? val : null;
    public void SetSetting(string key, string? value) => _customSettings[key] = value;
}
