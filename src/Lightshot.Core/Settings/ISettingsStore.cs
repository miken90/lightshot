// Ported from LightshotKit/Sources/LightshotKit/SettingsStore.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;

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
    QuickAccessSettings QuickAccess { get; set; }

    string? GetSetting(string key) => null;
    void SetSetting(string key, string? value) { }
}

public static class SettingsStoreExtensions
{
    public static string DefaultDestination(this ISettingsStore settings, DateTime? date = null)
    {
        var d = date ?? DateTime.Now;
        return new FilenameFormatter(settings.FilenamePattern)
            .DestinationPath(settings.SaveLocation, settings.DefaultFormat, d);
    }

    public static string RecordingDestination(this ISettingsStore settings, RecordingOutputKind kind, DateTime? date = null)
    {
        return settings.RecordingDestination(kind == RecordingOutputKind.Video ? "mp4" : "gif", date);
    }

    public static string RecordingDestination(this ISettingsStore settings, string pathExtension, DateTime? date = null)
    {
        var d = date ?? DateTime.Now;
        var name = new FilenameFormatter(settings.FilenamePattern).Filename(d);
        return settings.RecordingDestination(name, pathExtension, d);
    }

    public static string RecordingDestination(this ISettingsStore settings, string name, string pathExtension, DateTime? date = null)
    {
        var safe = FilenameFormatter.Sanitized(name) ?? new FilenameFormatter(settings.FilenamePattern).Filename(date ?? DateTime.Now);
        var ext = pathExtension.TrimStart('.');
        var filename = string.IsNullOrEmpty(ext) ? safe : $"{safe}.{ext}";
        return Path.Combine(settings.SaveLocation, filename).Replace('\\', '/');
    }
}
