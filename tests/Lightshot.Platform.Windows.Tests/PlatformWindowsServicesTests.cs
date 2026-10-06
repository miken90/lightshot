// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Platform.Windows.Windows;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class PlatformWindowsServicesTests
{
    [Fact]
    [Unit]
    public void AppPathsResolveStandardLocations()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.RoamingData));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.SettingsFile));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.LocalData));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.History));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.Temp));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.Logs));
        Assert.False(string.IsNullOrWhiteSpace(AppPaths.Updates));

        Assert.EndsWith("settings.json", AppPaths.SettingsFile, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("History", AppPaths.History, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Unit]
    public void ExplorerBuildsExpectedCommandLinesWithoutLaunching()
    {
        string samplePath = @"C:\Users\Test\Pictures\Screenshot.png";
        string args = Explorer.BuildRevealArguments(samplePath);
        Assert.Equal($"/select,\"{samplePath}\"", args);

        var revealPsi = Explorer.BuildRevealStartInfo(samplePath);
        Assert.Equal("explorer.exe", revealPsi.FileName);
        Assert.Equal($"/select,\"{samplePath}\"", revealPsi.Arguments);
        Assert.False(revealPsi.UseShellExecute);

        var settingsPsi = Explorer.BuildSettingsUriStartInfo(Explorer.StartupAppsUri);
        Assert.Equal(Explorer.StartupAppsUri, settingsPsi.FileName);
        Assert.True(settingsPsi.UseShellExecute);

        var keyboardPsi = Explorer.BuildSettingsUriStartInfo(Explorer.EaseOfAccessKeyboardUri);
        Assert.Equal(Explorer.EaseOfAccessKeyboardUri, keyboardPsi.FileName);
        Assert.True(keyboardPsi.UseShellExecute);
    }
}
