// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

// Proof that a value written through SetSetting under a key listed in SettingsKeys.AllKeys is dropped
// on reload: Load skips AllKeys when it rebuilds the custom settings and no typed field reads it.
public class SettingsReloadProofTests
{
    [Fact]
    [Unit]
    public void OnboardedFlagSurvivesARestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ls-reload-proof-{Guid.NewGuid():N}");
        var file = Path.Combine(dir, "settings.json");
        try
        {
            new JsonSettingsStore(file).SetSetting(SettingsKeys.AppOnboarded, "true");

            var restarted = new JsonSettingsStore(file);

            Assert.Equal("true", restarted.GetSetting(SettingsKeys.AppOnboarded));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
