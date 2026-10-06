// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.App.Views.Settings;
using Lightshot.Platform.Windows.Settings;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class UpdateSettingsViewModelTests
{
    [Fact]
    [Unit]
    public void AutoCheckPersistsAcrossStoreReload()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "lightshot-updates-vm-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store1 = new JsonSettingsStore(tempFile);
            var vm1 = new UpdateSettingsViewModel(store1, "1.0.0", isInstalled: true);
            Assert.True(vm1.AutoCheck);
            Assert.True(vm1.CanCheckNow);

            vm1.AutoCheck = false;
            Assert.False(vm1.AutoCheck);
            Assert.False(vm1.CanCheckNow);

            var store2 = new JsonSettingsStore(tempFile);
            Assert.Equal("false", store2.GetSetting(SettingsKeys.UpdateEnabled));

            var vm2 = new UpdateSettingsViewModel(store2, "1.0.0", isInstalled: true);
            Assert.False(vm2.AutoCheck);
            Assert.False(vm2.CanCheckNow);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
