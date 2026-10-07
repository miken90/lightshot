// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class JsonSettingsStoreTests
{
    [Fact]
    [Unit]
    public void DefaultsMissingKeysAndIgnoresUnknown()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"lightshot_settings_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string settingsFile = Path.Combine(tempDir, "settings.json");

        try
        {
            // Prepare a JSON file with only a subset of keys plus unknown keys
            string partialJson = """
            {
                "unknown.future.key": "some_value",
                "save.format": "jpeg",
                "save.jpegQuality": 0.42
            }
            """;
            File.WriteAllText(settingsFile, partialJson);

            var store = new JsonSettingsStore(settingsFile);

            // 1. Assert missing keys fall back to defaults
            Assert.Equal(SettingsKeys.DefaultFilenamePattern, store.FilenamePattern);
            Assert.Equal(SettingsKeys.DefaultHistoryRetention, store.HistoryRetention);
            Assert.Equal(SettingsKeys.DefaultOpenInEditor, store.OpenInEditor);
            Assert.Equal(SettingsKeys.DefaultIncludeCursor, store.IncludeCursor);
            Assert.Equal(SettingsKeys.DefaultAdjustAreaBeforeCapture, store.AdjustAreaBeforeCapture);
            Assert.Equal(SettingsKeys.DefaultCaptureDelay, store.CaptureDelay);
            Assert.Equal(AppearancePreference.System, store.Appearance);
            Assert.Equal(SettingsKeys.DefaultOcrKeepLineBreaks, store.OcrKeepsLineBreaks);
            Assert.Equal(SettingsKeys.DefaultHideDesktopIcons, store.HideDesktopIcons);
            Assert.NotNull(store.Hotkeys[CaptureAction.Area]);
            Assert.NotNull(store.Hotkeys[CaptureAction.Fullscreen]);

            // 2. Assert known present keys are loaded properly
            Assert.IsType<ImageFormat.Jpeg>(store.DefaultFormat);
            var jp = (ImageFormat.Jpeg)store.DefaultFormat;
            Assert.Equal(0.42, jp.Quality, 2);

            // 3. Assert unknown key is safely ignored and accessible via GetSetting
            Assert.Equal("some_value", store.GetSetting("unknown.future.key"));

            // 4. Assert change notifications fire on update
            string? changedKey = null;
            store.SettingChanged += (sender, key) => changedKey = key;
            store.OpenInEditor = false;
            Assert.Equal(SettingsKeys.CaptureOpenInEditor, changedKey);
            Assert.False(store.OpenInEditor);

            // Phase 4 editor.lastArrowStyle custom key persistence
            store.SetSetting(SettingsKeys.EditorLastArrowStyle, "2");
            Assert.Equal("2", store.GetSetting(SettingsKeys.EditorLastArrowStyle));

            // 5. Reload into a new store and verify persistence of both known and unknown keys
            var storeReloaded = new JsonSettingsStore(settingsFile);
            Assert.False(storeReloaded.OpenInEditor);
            Assert.Equal("some_value", storeReloaded.GetSetting("unknown.future.key"));
            Assert.Equal("2", storeReloaded.GetSetting(SettingsKeys.EditorLastArrowStyle));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    [Unit]
    public void AfterCaptureDefaultsToCopyAndQuickAccess()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ls_ac_def_{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSettingsStore(Path.Combine(tempDir, "settings.json"));
            Assert.True(store.AfterCapture.ShowQuickAccess);
            Assert.True(store.AfterCapture.CopyToClipboard);
            Assert.False(store.AfterCapture.SaveToFile);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    [Unit]
    public void AfterCaptureRoundTripsThroughSettingsFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ls_ac_roundtrip_{Guid.NewGuid():N}");
        string file = Path.Combine(tempDir, "settings.json");
        try
        {
            var store = new JsonSettingsStore(file);
            store.AfterCapture = new AfterCaptureSettings(ShowQuickAccess: false, CopyToClipboard: false, SaveToFile: true);

            var reloaded = new JsonSettingsStore(file);
            Assert.False(reloaded.AfterCapture.ShowQuickAccess);
            Assert.False(reloaded.AfterCapture.CopyToClipboard);
            Assert.True(reloaded.AfterCapture.SaveToFile);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    [Unit]
    public void BoolCustomValueDoesNotResetSettings()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ls_bool_custom_{Guid.NewGuid():N}");
        string file = Path.Combine(tempDir, "settings.json");
        try
        {
            Directory.CreateDirectory(tempDir);
            string json = """
            {
                "custom.flag": true,
                "save.format": "jpeg",
                "save.jpegQuality": 0.55
            }
            """;
            File.WriteAllText(file, json);

            var store = new JsonSettingsStore(file);
            Assert.IsType<ImageFormat.Jpeg>(store.DefaultFormat);
            Assert.Equal("true", store.GetSetting("custom.flag"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    [Unit]
    public void HistoryMaxAgeDaysDefaultsToSevenAndClamps()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"ls_max_age_{Guid.NewGuid():N}");
        string file = Path.Combine(tempDir, "settings.json");
        try
        {
            Directory.CreateDirectory(tempDir);
            var store = new JsonSettingsStore(file);

            // Default is 7
            Assert.Equal(7, store.HistoryMaxAgeDays);

            // Round trip
            store.HistoryMaxAgeDays = 14;
            Assert.Equal(14, store.HistoryMaxAgeDays);

            var reloaded = new JsonSettingsStore(file);
            Assert.Equal(14, reloaded.HistoryMaxAgeDays);

            // Clamp below 0 -> 0
            store.HistoryMaxAgeDays = -10;
            Assert.Equal(0, store.HistoryMaxAgeDays);

            var reloadedZero = new JsonSettingsStore(file);
            Assert.Equal(0, reloadedZero.HistoryMaxAgeDays);

            // Clamp above 3650 -> 3650
            store.HistoryMaxAgeDays = 99999;
            Assert.Equal(3650, store.HistoryMaxAgeDays);

            var reloadedMax = new JsonSettingsStore(file);
            Assert.Equal(3650, reloadedMax.HistoryMaxAgeDays);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
