using System;
using System.IO;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Velopack.Locators;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class UpdateServiceTests
{
    [Fact]
    [Unit]
    public async Task StagesSignedUpdateFromVersionedReleaseUrl()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-service-test-" + Guid.NewGuid().ToString("N"));
        string settingsFile = Path.Combine(baseTemp, "settings.json");
        string stagingDir = Path.Combine(baseTemp, "staging");
        string packagesDir = Path.Combine(baseTemp, "packages");
        Directory.CreateDirectory(baseTemp);

        try
        {
            var (privKey, pubKey) = UpdateFeed.NewThrowawayKey();
            byte[] packageBytes = UpdateFeed.MinimalNupkg("LightshotApp", "9.9.9");
            string packageFileName = "LightshotApp-9.9.9-full.nupkg";

            var (manifestJson, sigBase64) = UpdateFeed.SignManifest(privKey, "9.9.9", 10, packageFileName, packageBytes);

            var handler = new UrlMapHandler();
            handler.MapText(UpdateChecker.DefaultManifestUrl, manifestJson);
            handler.MapText(UpdateChecker.DefaultManifestUrl + ".sig", sigBase64);
            string packageUrl = UpdateService.ReleaseDownloadBase + "v9.9.9/" + packageFileName;
            handler.Map(packageUrl, packageBytes);

            var store = new JsonSettingsStore(settingsFile);
            var locator = new TestVelopackLocator("LightshotApp", "1.0.0", packagesDir, null);
            var applier = new VelopackApplier(locator: locator);

            using var service = new UpdateService(store, applier, stagingDir, handler, publicKey: pubKey);

            var outcome = await service.CheckAndStageAsync(force: true, ct: TestContext.Current.CancellationToken);

            Assert.Equal(UpdateCheckOutcome.Staged, outcome);
            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal(UpdateChecker.DefaultManifestUrl, handler.Requests[0]);
            Assert.Equal(UpdateChecker.DefaultManifestUrl + ".sig", handler.Requests[1]);
            Assert.Equal(packageUrl, handler.Requests[2]);

            Assert.True(service.State.IsStaged);
            Assert.Equal("9.9.9", service.State.StagedVersion);
            Assert.Equal(10, service.State.StagedSequence);
        }
        finally
        {
            if (Directory.Exists(baseTemp))
            {
                Directory.Delete(baseTemp, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public async Task DisabledSettingMakesNoRequest()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-service-test-" + Guid.NewGuid().ToString("N"));
        string settingsFile = Path.Combine(baseTemp, "settings.json");
        string stagingDir = Path.Combine(baseTemp, "staging");
        string packagesDir = Path.Combine(baseTemp, "packages");
        Directory.CreateDirectory(baseTemp);

        try
        {
            var handler = new UrlMapHandler();
            var store = new JsonSettingsStore(settingsFile);
            store.SetSetting(SettingsKeys.UpdateEnabled, "false");

            var locator = new TestVelopackLocator("LightshotApp", "1.0.0", packagesDir, null);
            var applier = new VelopackApplier(locator: locator);

            using var service = new UpdateService(store, applier, stagingDir, handler);

            var outcome = await service.CheckAndStageAsync(force: true, ct: TestContext.Current.CancellationToken);

            Assert.Equal(UpdateCheckOutcome.Disabled, outcome);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            if (Directory.Exists(baseTemp))
            {
                Directory.Delete(baseTemp, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public void FloorMovesOnlyAfterSuccessfulUpdate()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-service-test-" + Guid.NewGuid().ToString("N"));
        string settingsFile = Path.Combine(baseTemp, "settings.json");
        string stagingDir = Path.Combine(baseTemp, "staging");
        string packagesDir = Path.Combine(baseTemp, "packages");
        Directory.CreateDirectory(baseTemp);

        try
        {
            // Case 1: Locator version matches pendingVersion -> successful update
            var store1 = new JsonSettingsStore(settingsFile);
            store1.SetSetting(SettingsKeys.UpdatePendingVersion, "9.9.9");
            store1.SetSetting(SettingsKeys.UpdatePendingSequence, "12");

            var locatorMatch = new TestVelopackLocator("LightshotApp", "9.9.9", packagesDir, null);
            var applierMatch = new VelopackApplier(locator: locatorMatch);

            using (var service1 = new UpdateService(store1, applierMatch, stagingDir))
            {
                service1.ReconcileAfterUpdate();
                Assert.Equal(12, service1.SequenceFloor);
                Assert.Null(store1.GetSetting(SettingsKeys.UpdatePendingVersion));
                Assert.Null(store1.GetSetting(SettingsKeys.UpdatePendingSequence));
            }

            // Persistence through real store reload
            var storeReloaded = new JsonSettingsStore(settingsFile);
            Assert.Equal("12", storeReloaded.GetSetting(SettingsKeys.UpdateSequence));
            Assert.Null(storeReloaded.GetSetting(SettingsKeys.UpdatePendingVersion));
            Assert.Null(storeReloaded.GetSetting(SettingsKeys.UpdatePendingSequence));

            // Case 2: Locator version does not match pendingVersion -> failed/not applied update
            string settingsFile2 = Path.Combine(baseTemp, "settings2.json");
            var store2 = new JsonSettingsStore(settingsFile2);
            store2.SetSetting(SettingsKeys.UpdatePendingVersion, "9.9.9");
            store2.SetSetting(SettingsKeys.UpdatePendingSequence, "12");

            var locatorMismatch = new TestVelopackLocator("LightshotApp", "1.0.0", packagesDir, null);
            var applierMismatch = new VelopackApplier(locator: locatorMismatch);

            using (var service2 = new UpdateService(store2, applierMismatch, stagingDir))
            {
                service2.ReconcileAfterUpdate();
                Assert.Equal(0, service2.SequenceFloor);
                Assert.Null(store2.GetSetting(SettingsKeys.UpdatePendingVersion));
                Assert.Null(store2.GetSetting(SettingsKeys.UpdatePendingSequence));
            }

            var storeReloaded2 = new JsonSettingsStore(settingsFile2);
            Assert.Equal("0", storeReloaded2.GetSetting(SettingsKeys.UpdateSequence) ?? "0");
            Assert.Null(storeReloaded2.GetSetting(SettingsKeys.UpdatePendingVersion));
            Assert.Null(storeReloaded2.GetSetting(SettingsKeys.UpdatePendingSequence));
        }
        finally
        {
            if (Directory.Exists(baseTemp))
            {
                Directory.Delete(baseTemp, recursive: true);
            }
        }
    }
}
