// MIT License, Copyright (c) 2026 Viet Le

using System;
using Microsoft.Win32;
using Lightshot.Platform.Windows.Startup;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class LaunchAtLoginTests
{
    [Fact]
    [Desktop]
    public void WritesAndRemovesRunValue()
    {
        string testValueName = $"LightshotTest_{Guid.NewGuid():N}";
        string dummyExePath = @"C:\TestApp\Lightshot.exe";
        string expectedCommandLine = $"\"{dummyExePath}\" --background";

        try
        {
            // 1. Initial state: value must not exist
            Assert.False(LaunchAtLogin.IsEnabled(testValueName));
            Assert.Null(LaunchAtLogin.GetRegisteredCommandLine(testValueName));

            // 2. Enable: writes to HKCU Run
            LaunchAtLogin.SetEnabled(true, dummyExePath, testValueName);
            Assert.True(LaunchAtLogin.IsEnabled(testValueName));
            Assert.Equal(expectedCommandLine, LaunchAtLogin.GetRegisteredCommandLine(testValueName));

            // Verify directly against registry
            using (var runKey = Registry.CurrentUser.OpenSubKey(LaunchAtLogin.RunKeyPath, false))
            {
                Assert.NotNull(runKey);
                object? val = runKey.GetValue(testValueName);
                Assert.Equal(expectedCommandLine, val);
            }

            // 3. Test StartupApproved\Run detection
            using (var approvedKey = Registry.CurrentUser.CreateSubKey(LaunchAtLogin.StartupApprovedKeyPath, true))
            {
                byte[] disabledBytes = [0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
                approvedKey.SetValue(testValueName, disabledBytes, RegistryValueKind.Binary);
            }

            Assert.True(LaunchAtLogin.IsDisabledByTaskManager(testValueName));
            Assert.False(LaunchAtLogin.IsEnabled(testValueName));
            Assert.Equal(StartupStatus.DisabledByTaskManager, LaunchAtLogin.GetStatus(testValueName));

            // 4. Disable: removes from HKCU Run
            LaunchAtLogin.SetEnabled(false, null, testValueName);
            Assert.False(LaunchAtLogin.IsEnabled(testValueName));
            Assert.Null(LaunchAtLogin.GetRegisteredCommandLine(testValueName));

            using (var runKey = Registry.CurrentUser.OpenSubKey(LaunchAtLogin.RunKeyPath, false))
            {
                if (runKey != null)
                {
                    Assert.Null(runKey.GetValue(testValueName));
                }
            }
        }
        finally
        {
            // Guaranteed test-scoped cleanup
            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(LaunchAtLogin.RunKeyPath, true);
                runKey?.DeleteValue(testValueName, false);
            }
            catch { }

            try
            {
                using var approvedKey = Registry.CurrentUser.OpenSubKey(LaunchAtLogin.StartupApprovedKeyPath, true);
                approvedKey?.DeleteValue(testValueName, false);
            }
            catch { }
        }
    }
}
