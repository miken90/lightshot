// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.App.Updates;
using Lightshot.App.Views.Notices;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Velopack.Locators;
using Xunit;

namespace Lightshot.App.Tests;

public class UpdateNoticeControllerTests
{
    private static void RunInSta(Func<Task> action)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        captured?.Throw();
    }

    [Fact]
    [Unit]
    public void FirstLaunchNeverChecks() => RunInSta(async () =>
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-notice-test-" + Guid.NewGuid().ToString("N"));
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

            var store = new JsonSettingsStore(Path.Combine(baseTemp, "settings.json"));
            var locator = new TestVelopackLocator("LightshotApp", "1.0.0", Path.Combine(baseTemp, "packages"), null);
            var applier = new VelopackApplier(locator: locator);
            using var service = new UpdateService(store, applier, Path.Combine(baseTemp, "staging"), handler, publicKey: pubKey);

            string? trayText = null;
            string? noticeTitle = null;
            using var controller = new UpdateNoticeController(
                service,
                hasWorkInProgress: () => false,
                setTrayUpdate: t => trayText = t,
                showNotice: (t, _) => noticeTitle = t,
                shutdown: () => { },
                testMode: false);

            await controller.StartAsync();

            Assert.Equal(1, controller.LaunchCount);
            Assert.Empty(handler.Requests);
            Assert.Null(trayText);
            Assert.Null(noticeTitle);
        }
        finally
        {
            if (Directory.Exists(baseTemp)) Directory.Delete(baseTemp, true);
        }
    });

    [Fact]
    [Unit]
    public void SecondLaunchStagesAndShowsDotMenuAndNotice() => RunInSta(async () =>
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-notice-test-" + Guid.NewGuid().ToString("N"));
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

            var store = new JsonSettingsStore(Path.Combine(baseTemp, "settings.json"));
            store.SetSetting(SettingsKeys.AppLaunchCount, "1");

            var locator = new TestVelopackLocator("LightshotApp", "1.0.0", Path.Combine(baseTemp, "packages"), null);
            var applier = new VelopackApplier(locator: locator);
            using var service = new UpdateService(store, applier, Path.Combine(baseTemp, "staging"), handler, publicKey: pubKey);

            string? trayText = null;
            string? noticeTitle = null;
            string? stagedVersion = null;
            using var controller = new UpdateNoticeController(
                service,
                hasWorkInProgress: () => false,
                setTrayUpdate: t => trayText = t,
                showNotice: (t, _) => noticeTitle = t,
                shutdown: () => { },
                testMode: false);

            controller.Staged += v => stagedVersion = v;

            await controller.StartAsync();

            Assert.Equal("Restart to Update (9.9.9)", trayText);
            Assert.Equal("Lightshot update ready", noticeTitle);
            Assert.Equal("9.9.9", stagedVersion);
            Assert.Equal(3, handler.Requests.Count);
        }
        finally
        {
            if (Directory.Exists(baseTemp)) Directory.Delete(baseTemp, true);
        }
    });

    [Fact]
    [Unit]
    public void RestartIsHeldWhileWorkInProgress() => RunInSta(async () =>
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-notice-test-" + Guid.NewGuid().ToString("N"));
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

            var store = new JsonSettingsStore(Path.Combine(baseTemp, "settings.json"));
            store.SetSetting(SettingsKeys.AppLaunchCount, "1");

            var locator = new TestVelopackLocator("LightshotApp", "1.0.0", Path.Combine(baseTemp, "packages"), null);
            var applier = new VelopackApplier(locator: locator);
            using var service = new UpdateService(store, applier, Path.Combine(baseTemp, "staging"), handler, publicKey: pubKey);

            string? lastNoticeTitle = null;
            bool applyCalled = false;
            bool shutdownCalled = false;

            using var controller = new UpdateNoticeController(
                service,
                hasWorkInProgress: () => true,
                setTrayUpdate: _ => { },
                showNotice: (t, _) => lastNoticeTitle = t,
                shutdown: () => shutdownCalled = true,
                testMode: false,
                apply: _ =>
                {
                    applyCalled = true;
                    return Task.FromResult(true);
                });

            await controller.StartAsync();

            var decision = await controller.RestartToUpdateAsync();

            Assert.Equal(UpdatePolicyDecision.HeldWorkInProgress, decision);
            Assert.False(applyCalled);
            Assert.False(shutdownCalled);
            Assert.Equal("Update waiting", lastNoticeTitle);
        }
        finally
        {
            if (Directory.Exists(baseTemp)) Directory.Delete(baseTemp, true);
        }
    });

    [Fact]
    [Unit]
    public void RestartAppliesThenShutsDown() => RunInSta(async () =>
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-notice-test-" + Guid.NewGuid().ToString("N"));
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

            var store = new JsonSettingsStore(Path.Combine(baseTemp, "settings.json"));
            store.SetSetting(SettingsKeys.AppLaunchCount, "1");

            var locator = new TestVelopackLocator("LightshotApp", "1.0.0", Path.Combine(baseTemp, "packages"), null);
            var applier = new VelopackApplier(locator: locator);
            using var service = new UpdateService(store, applier, Path.Combine(baseTemp, "staging"), handler, publicKey: pubKey);

            var callOrder = new List<string>();
            bool applyParam = false;

            using var controller = new UpdateNoticeController(
                service,
                hasWorkInProgress: () => false,
                setTrayUpdate: _ => { },
                showNotice: (_, _) => { },
                shutdown: () => callOrder.Add("shutdown"),
                testMode: false,
                apply: restart =>
                {
                    applyParam = restart;
                    callOrder.Add("apply");
                    return Task.FromResult(true);
                });

            await controller.StartAsync();

            var decision = await controller.RestartToUpdateAsync();

            Assert.Equal(UpdatePolicyDecision.ReadyToApply, decision);
            Assert.True(applyParam);
            Assert.Equal(new[] { "apply", "shutdown" }, callOrder);

            bool exitApplied = controller.ApplyOnExit();
            Assert.False(exitApplied);
        }
        finally
        {
            if (Directory.Exists(baseTemp)) Directory.Delete(baseTemp, true);
        }
    });
}
