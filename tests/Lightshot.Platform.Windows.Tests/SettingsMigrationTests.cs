// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Text.Json.Nodes;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class SettingsMigrationTests
{
    [Fact]
    [Unit]
    public void AddsKeysWithoutRewritingExisting()
    {
        string existingJson = """
        {
            "save.format": "jpeg",
            "save.jpegQuality": 0.75,
            "editor.lastArrowStyle": "2",
            "custom.arbitrary.key": "preserve_this"
        }
        """;

        string migratedJson = SettingsMigrations.MigrateJson(existingJson);
        var node = JsonNode.Parse(migratedJson) as JsonObject;
        Assert.NotNull(node);

        // 1. Existing values must not be modified or overwritten
        Assert.Equal("jpeg", node[SettingsKeys.SaveFormat]?.GetValue<string>());
        Assert.Equal(0.75, node[SettingsKeys.SaveJpegQuality]?.GetValue<double>());
        Assert.Equal("2", node[SettingsKeys.EditorLastArrowStyle]?.GetValue<string>());
        Assert.Equal("preserve_this", node["custom.arbitrary.key"]?.GetValue<string>());

        // 2. All missing keys from APP §5 must now be present with default values
        Assert.Equal(SettingsKeys.DefaultFilenamePattern, node[SettingsKeys.SaveFilenamePattern]?.GetValue<string>());
        Assert.Equal(SettingsKeys.DefaultHistoryRetention, node[SettingsKeys.HistoryRetention]?.GetValue<int>());
        Assert.Equal(SettingsKeys.DefaultOpenInEditor, node[SettingsKeys.CaptureOpenInEditor]?.GetValue<bool>());
        Assert.Equal(SettingsKeys.DefaultIncludeCursor, node[SettingsKeys.CaptureIncludeCursor]?.GetValue<bool>());
        Assert.Equal(SettingsKeys.DefaultAdjustAreaBeforeCapture, node[SettingsKeys.CaptureAdjustAreaBeforeCapture]?.GetValue<bool>());
        Assert.Equal(SettingsKeys.DefaultCaptureDelay, node[SettingsKeys.CaptureDelay]?.GetValue<double>());
        Assert.Equal(SettingsKeys.DefaultAppearance, node[SettingsKeys.AppAppearance]?.GetValue<string>());
        Assert.Equal(SettingsKeys.DefaultOcrKeepLineBreaks, node[SettingsKeys.OcrKeepLineBreaks]?.GetValue<bool>());
        Assert.Equal(SettingsKeys.DefaultHideDesktopIcons, node[SettingsKeys.AppHideDesktopIcons]?.GetValue<bool>());

        // 3. Hotkeys dictionary structure is populated
        var hotkeys = node[SettingsKeys.CaptureHotkeys] as JsonObject;
        Assert.NotNull(hotkeys);
        Assert.True(hotkeys.ContainsKey("Area"));
        Assert.True(hotkeys.ContainsKey("Fullscreen"));

        // 4. File migration test
        string tempDir = Path.Combine(Path.GetTempPath(), $"lightshot_mig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, "settings.json");
        try
        {
            File.WriteAllText(tempFile, existingJson);
            SettingsMigrations.MigrateFile(tempFile);
            string fileContent = File.ReadAllText(tempFile);
            var fileNode = JsonNode.Parse(fileContent) as JsonObject;
            Assert.NotNull(fileNode);
            Assert.Equal("jpeg", fileNode[SettingsKeys.SaveFormat]?.GetValue<string>());
            Assert.Equal(SettingsKeys.DefaultFilenamePattern, fileNode[SettingsKeys.SaveFilenamePattern]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
