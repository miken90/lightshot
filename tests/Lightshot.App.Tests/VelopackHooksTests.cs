using System;
using System.IO;
using Lightshot.App.Startup;
using Lightshot.Platform.Windows.Startup;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class VelopackHooksTests
{
    [Fact]
    [Unit]
    public void UninstallRemovesRunValueAndKeepsDataByDefault()
    {
        string testName = "LightshotHookTest-" + Guid.NewGuid().ToString("N");
        string dummyExe = @"C:\dummy\lightshot.exe";
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-hook-test-" + Guid.NewGuid().ToString("N"));
        string dir1 = Path.Combine(baseTemp, "roaming");
        string dir2 = Path.Combine(baseTemp, "local");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        string file1 = Path.Combine(dir1, "settings.json");
        string file2 = Path.Combine(dir2, "state.json");
        File.WriteAllText(file1, "settings");
        File.WriteAllText(file2, "state");

        try
        {
            LaunchAtLogin.SetEnabled(true, dummyExe, testName);
            Assert.True(LaunchAtLogin.IsEnabled(testName));

            int askCalls = 0;
            string? capturedValueDuringAsk = "uninitialized";

            VelopackHooks.CleanupOnUninstall(
                askRemoveUserData: () =>
                {
                    askCalls++;
                    capturedValueDuringAsk = LaunchAtLogin.GetRegisteredCommandLine(testName);
                    return false;
                },
                valueName: testName,
                dataDirectories: new[] { dir1, dir2 });

            Assert.Equal(1, askCalls);
            Assert.Null(capturedValueDuringAsk);
            Assert.False(LaunchAtLogin.IsEnabled(testName));
            Assert.True(Directory.Exists(dir1));
            Assert.True(Directory.Exists(dir2));
            Assert.True(File.Exists(file1));
            Assert.True(File.Exists(file2));
        }
        finally
        {
            try { LaunchAtLogin.SetEnabled(false, null, testName); } catch { }
            if (Directory.Exists(baseTemp))
            {
                Directory.Delete(baseTemp, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public void UninstallRemovesDataWhenChosen()
    {
        string testName = "LightshotHookTest-" + Guid.NewGuid().ToString("N");
        string dummyExe = @"C:\dummy\lightshot.exe";
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-hook-test-" + Guid.NewGuid().ToString("N"));
        string dir1 = Path.Combine(baseTemp, "roaming");
        string dir2 = Path.Combine(baseTemp, "local");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir1, "settings.json"), "settings");
        File.WriteAllText(Path.Combine(dir2, "state.json"), "state");

        try
        {
            LaunchAtLogin.SetEnabled(true, dummyExe, testName);
            Assert.True(LaunchAtLogin.IsEnabled(testName));

            VelopackHooks.CleanupOnUninstall(
                askRemoveUserData: () => true,
                valueName: testName,
                dataDirectories: new[] { dir1, dir2 });

            Assert.False(LaunchAtLogin.IsEnabled(testName));
            Assert.False(Directory.Exists(dir1));
            Assert.False(Directory.Exists(dir2));
        }
        finally
        {
            try { LaunchAtLogin.SetEnabled(false, null, testName); } catch { }
            if (Directory.Exists(baseTemp))
            {
                Directory.Delete(baseTemp, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public void UninstallKeepsUnfinishedRecordings()
    {
        string testName = "LightshotHookTest-" + Guid.NewGuid().ToString("N");
        string data = Path.Combine(Path.GetTempPath(), "lightshot-hook-test-" + Guid.NewGuid().ToString("N"));
        string historyDir = Path.Combine(data, "History");
        string recordingsDir = Path.Combine(data, "Lightshot Recordings");
        Directory.CreateDirectory(historyDir);
        Directory.CreateDirectory(recordingsDir);

        string settingsFile = Path.Combine(data, "settings.json");
        string historyFile = Path.Combine(historyDir, "a.png");
        string recordingFile = Path.Combine(recordingsDir, "take.mp4");

        File.WriteAllText(settingsFile, "settings");
        File.WriteAllText(historyFile, "png");
        File.WriteAllText(recordingFile, "mp4");

        try
        {
            VelopackHooks.CleanupOnUninstall(
                askRemoveUserData: () => true,
                valueName: testName,
                dataDirectories: new[] { data },
                keepIfNotEmpty: recordingsDir);

            // settings and History are gone
            Assert.False(File.Exists(settingsFile));
            Assert.False(Directory.Exists(historyDir));

            // Lightshot Recordings\take.mp4 still exists with same content
            Assert.True(File.Exists(recordingFile));
            Assert.Equal("mp4", File.ReadAllText(recordingFile));

            // Then empty the recordings folder and run again: data is deleted entirely
            File.Delete(recordingFile);
            VelopackHooks.CleanupOnUninstall(
                askRemoveUserData: () => true,
                valueName: testName,
                dataDirectories: new[] { data },
                keepIfNotEmpty: recordingsDir);

            Assert.False(Directory.Exists(data));
        }
        finally
        {
            try { LaunchAtLogin.SetEnabled(false, null, testName); } catch { }
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public void AfterUpdateRewritesEnabledRunValue()
    {
        string testName = "LightshotHookTest-" + Guid.NewGuid().ToString("N");
        string stalePath = @"C:\old\Lightshot.App.exe";

        try
        {
            // Set stale path
            LaunchAtLogin.SetEnabled(true, stalePath, testName);
            Assert.Equal($"\"{stalePath}\" --background", LaunchAtLogin.GetRegisteredCommandLine(testName));

            // Refresh rewrites to LaunchAtLogin.BuildCommandLine()
            VelopackHooks.RefreshRunKey(testName);
            string expected = LaunchAtLogin.BuildCommandLine();
            Assert.Equal(expected, LaunchAtLogin.GetRegisteredCommandLine(testName));

            // For a name that is not set, RefreshRunKey leaves it unset
            string unsetTestName = "LightshotHookTest-Unset-" + Guid.NewGuid().ToString("N");
            VelopackHooks.RefreshRunKey(unsetTestName);
            Assert.False(LaunchAtLogin.IsEnabled(unsetTestName));
            Assert.Null(LaunchAtLogin.GetRegisteredCommandLine(unsetTestName));
        }
        finally
        {
            try { LaunchAtLogin.SetEnabled(false, null, testName); } catch { }
        }
    }
}
