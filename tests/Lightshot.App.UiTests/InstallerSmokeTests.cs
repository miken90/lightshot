// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Startup;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Velopack;
using Velopack.Locators;
using Xunit;

namespace Lightshot.App.UiTests;

public class DryRunReleaseFixture : IDisposable
{
    public string RepoRoot { get; }
    public string TempKeyDir { get; }
    public string KeyPemPath { get; }
    public string PublicKeyBase64 { get; }
    public string ReleaseDir { get; }
    public string PublishAppExePath { get; }
    public string ProcessOutput { get; }

    public DryRunReleaseFixture()
    {
        RepoRoot = UpdateFeed.FindRepoRoot() ?? throw new InvalidOperationException("Repo root not found.");
        ReleaseDir = Path.Combine(RepoRoot, "artifacts", "release");
        PublishAppExePath = Path.Combine(RepoRoot, "artifacts", "publish", "app", "Lightshot.App.exe");

        var (privKey, pubKey) = UpdateFeed.NewThrowawayKey();
        PublicKeyBase64 = pubKey;

        TempKeyDir = Path.Combine(Path.GetTempPath(), "lightshot-test-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempKeyDir);
        KeyPemPath = Path.Combine(TempKeyDir, "key.pem");
        File.WriteAllText(KeyPemPath, privKey, Encoding.ASCII);

        string releaseScript = Path.Combine(RepoRoot, "scripts", "release.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{releaseScript}\" -DryRun -Version 0.1.0 -Sequence 7 -DryRunKeyPath \"{KeyPemPath}\"",
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputBuilder = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outputBuilder) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (outputBuilder) outputBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool finished = process.WaitForExit((int)TimeSpan.FromMinutes(40).TotalMilliseconds);
        if (!finished)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("release.ps1 -DryRun timed out after 40 minutes.");
        }

        ProcessOutput = outputBuilder.ToString();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempKeyDir))
            {
                Directory.Delete(TempKeyDir, recursive: true);
            }
        }
        catch { }
    }
}

public class InstallerSmokeTests : IClassFixture<DryRunReleaseFixture>
{
    private readonly DryRunReleaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InstallerSmokeTests(DryRunReleaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Desktop]
    public void DryRunSucceeds()
    {
        // (1) releases.json and releases.json.sig verify
        string releasesJsonPath = Path.Combine(_fixture.ReleaseDir, "releases.json");
        string releasesSigPath = Path.Combine(_fixture.ReleaseDir, "releases.json.sig");
        Assert.True(File.Exists(releasesJsonPath), $"releases.json missing at {releasesJsonPath}");
        Assert.True(File.Exists(releasesSigPath), $"releases.json.sig missing at {releasesSigPath}");

        string json = File.ReadAllText(releasesJsonPath);
        byte[] sigBytes = Encoding.ASCII.GetBytes(File.ReadAllText(releasesSigPath).Trim());
        var verifier = new ManifestVerifier(_fixture.PublicKeyBase64, null, runningVersion: "0.0.1");
        var verified = verifier.Verify(json, sigBytes, 0);
        Assert.NotNull(verified);
        Assert.True(verified.IsValid);
        Assert.NotNull(verified.Manifest);
        Assert.Equal("0.1.0", verified.Manifest.Version);
        Assert.Equal(7, verified.Manifest.Sequence);

        var nupkgFiles = Directory.GetFiles(_fixture.ReleaseDir, "*-0.1.0-full.nupkg");
        Assert.Single(nupkgFiles);
        string nupkgPath = nupkgFiles[0];
        string nupkgFileName = Path.GetFileName(nupkgPath);
        Assert.Equal(nupkgFileName, verified.Manifest.Package);

        byte[] nupkgBytes = File.ReadAllBytes(nupkgPath);
        string nupkgSha256 = Convert.ToHexString(SHA256.HashData(nupkgBytes));
        Assert.Equal(verified.Manifest.Sha256, nupkgSha256, ignoreCase: true);
        Assert.Equal((long)nupkgBytes.Length, verified.Manifest.Size);

        // (2) Every line of SHA256SUMS.txt matches recomputed hash
        string sumsPath = Path.Combine(_fixture.ReleaseDir, "SHA256SUMS.txt");
        Assert.True(File.Exists(sumsPath), $"SHA256SUMS.txt missing at {sumsPath}");
        Assert.DoesNotContain((byte)'\r', File.ReadAllBytes(sumsPath));
        string[] sumLines = File.ReadAllLines(sumsPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        var sumFileNames = new System.Collections.Generic.List<string>();
        foreach (string line in sumLines)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(parts.Length >= 2, $"Malformed line in SHA256SUMS.txt: {line}");
            string expectedHash = parts[0];
            string filename = parts[1].TrimStart('*');
            sumFileNames.Add(filename);

            string targetFile = Path.Combine(_fixture.ReleaseDir, filename);
            Assert.True(File.Exists(targetFile), $"File listed in SHA256SUMS.txt not found: {targetFile}");
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(targetFile)));
            Assert.Equal(expectedHash, actualHash, ignoreCase: true);
        }

        Assert.Contains(sumFileNames, f => f.EndsWith("-win-Setup.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sumFileNames, f => f.EndsWith("-win-Portable.zip", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sumFileNames, f => f.EndsWith("-0.1.0-full.nupkg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sumFileNames, f => string.Equals(f, "releases.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sumFileNames, f => string.Equals(f, "releases.json.sig", StringComparison.OrdinalIgnoreCase));

        // (3) *-win-Setup.exe length / 1 MiB <= 113
        var setupFiles = Directory.GetFiles(_fixture.ReleaseDir, "*-win-Setup.exe");
        Assert.Single(setupFiles);
        long setupLen = new FileInfo(setupFiles[0]).Length;
        double setupMiB = (double)setupLen / (1024 * 1024);
        _output.WriteLine($"Setup.exe size: {setupMiB:F2} MiB ({setupLen} bytes)");
        Assert.True(setupMiB <= 113.0, $"Setup.exe size {setupMiB:F2} MiB exceeds 113 MiB limit");

        // (4) FileVersionInfo of publish app exe has ProductVersion starting with 0.1.0
        Assert.True(File.Exists(_fixture.PublishAppExePath), $"Publish exe not found at {_fixture.PublishAppExePath}");
        var fvi = FileVersionInfo.GetVersionInfo(_fixture.PublishAppExePath);
        _output.WriteLine($"Publish exe ProductVersion: {fvi.ProductVersion}");
        Assert.NotNull(fvi.ProductVersion);
        Assert.StartsWith("0.1.0", fvi.ProductVersion);

        // (5) Portable zip contains an Update.exe entry
        var portableFiles = Directory.GetFiles(_fixture.ReleaseDir, "*-win-Portable.zip");
        Assert.Single(portableFiles);
        using var zip = ZipFile.OpenRead(portableFiles[0]);
        var updateEntry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, "Update.exe", StringComparison.OrdinalIgnoreCase));
        if (updateEntry == null)
        {
            _output.WriteLine("Zip entries: " + string.Join(", ", zip.Entries.Select(e => e.FullName)));
        }
        Assert.NotNull(updateEntry);

        // (6) The official key path was not passed
        Assert.DoesNotContain(".lightshot-release", _fixture.ProcessOutput);

        // (7) vpk's releases.win.json has exactly one Full asset with PackageId == LightshotApp
        string releasesWinJson = Path.Combine(_fixture.ReleaseDir, "releases.win.json");
        Assert.True(File.Exists(releasesWinJson), $"releases.win.json missing at {releasesWinJson}");
        var feed = VelopackAssetFeed.FromJson(File.ReadAllText(releasesWinJson));
        Assert.NotNull(feed);
        var fullAssets = feed.Assets.Where(a => a.Type == VelopackAssetType.Full).ToList();
        Assert.Single(fullAssets);
        var asset = fullAssets[0];
        Assert.Equal(UpdateStager.VelopackPackageId, asset.PackageId);
        Assert.Equal(verified.Manifest.Package, asset.FileName);
    }

    [Fact]
    [Desktop]
    public async Task StagedReleaseIsAcceptedByVelopack()
    {
        string releasesJsonPath = Path.Combine(_fixture.ReleaseDir, "releases.json");
        string releasesSigPath = Path.Combine(_fixture.ReleaseDir, "releases.json.sig");
        var nupkgFiles = Directory.GetFiles(_fixture.ReleaseDir, "*-0.1.0-full.nupkg");
        Assert.Single(nupkgFiles);

        string manifestJson = File.ReadAllText(releasesJsonPath);
        string sigBase64 = File.ReadAllText(releasesSigPath).Trim();
        byte[] nupkgBytes = File.ReadAllBytes(nupkgFiles[0]);
        string nupkgFileName = Path.GetFileName(nupkgFiles[0]);

        string tempBase = Path.Combine(Path.GetTempPath(), "lightshot-staged-release-test-" + Guid.NewGuid().ToString("N"));
        string stagingDir = Path.Combine(tempBase, "staging");
        string packagesDir = Path.Combine(tempBase, "packages");
        string settingsFile = Path.Combine(tempBase, "settings.json");
        Directory.CreateDirectory(tempBase);

        try
        {
            var handler = new UrlMapHandler();
            handler.MapText(UpdateChecker.DefaultManifestUrl, manifestJson);
            handler.MapText(UpdateChecker.DefaultManifestUrl + ".sig", sigBase64);
            string packageUrl = UpdateService.ReleaseDownloadBase + "v0.1.0/" + nupkgFileName;
            handler.Map(packageUrl, nupkgBytes);

            var store = new JsonSettingsStore(settingsFile);
            var locator = new TestVelopackLocator("LightshotApp", "0.0.1", packagesDir, null);
            var applier = new VelopackApplier(locator: locator);
            using var service = new UpdateService(store, applier, stagingDir, handler, publicKey: _fixture.PublicKeyBase64);

            var outcome = await service.CheckAndStageAsync(force: true, ct: TestContext.Current.CancellationToken);
            Assert.Equal(UpdateCheckOutcome.Staged, outcome);

            var preparedAsset = await applier.PrepareStagedUpdateAsync(stagingDir, ct: TestContext.Current.CancellationToken);
            Assert.NotNull(preparedAsset);
            Assert.Equal("0.1.0", preparedAsset.Version.ToString());

            string expectedHash = Convert.ToHexString(SHA256.HashData(nupkgBytes));
            var copiedFiles = Directory.GetFiles(packagesDir, "*-0.1.0-full.nupkg");
            Assert.Single(copiedFiles);
            byte[] copiedBytes = File.ReadAllBytes(copiedFiles[0]);
            string copiedHash = Convert.ToHexString(SHA256.HashData(copiedBytes));
            Assert.Equal(expectedHash, copiedHash, ignoreCase: true);
        }
        finally
        {
            if (Directory.Exists(tempBase)) Directory.Delete(tempBase, recursive: true);
        }
    }

    [Fact(Explicit = true)]
    [Desktop]
    public async Task InstallLaunchUninstall()
    {
        // This test installs and uninstalls the real per-user Velopack app. Refuse to touch a
        // machine that already has Lightshot installed, and require an explicit opt-in.
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string installRoot = Path.Combine(localAppData, "LightshotApp");
        if (Environment.GetEnvironmentVariable("LIGHTSHOT_ALLOW_REAL_INSTALL") != "1")
            Assert.Skip("Set LIGHTSHOT_ALLOW_REAL_INSTALL=1 on a disposable machine (e.g. Windows Sandbox) to run this test.");
        if (Directory.Exists(installRoot))
            Assert.Skip($"A real Lightshot install exists at {installRoot}; run this test on a machine without one.");

        string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lightshot", "settings.json");
        byte[]? settingsBefore = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        bool runAtLoginBefore = LaunchAtLogin.IsEnabled();
        try
        {
            var setupFiles = Directory.GetFiles(_fixture.ReleaseDir, "*-win-Setup.exe");
            Assert.Single(setupFiles);
            string setupExe = setupFiles[0];

            // 1. Run Setup.exe --silent
            var setupPsi = new ProcessStartInfo
            {
                FileName = setupExe,
                Arguments = "--silent",
                UseShellExecute = false
            };
            using (var setupProc = Process.Start(setupPsi))
            {
                Assert.NotNull(setupProc);
                await setupProc.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.Equal(0, setupProc.ExitCode);
            }

            string installedExe = Path.Combine(installRoot, "current", "Lightshot.App.exe");
            string updateExe = Path.Combine(installRoot, "Update.exe");

            // Wait up to 30s for installed exe to exist
            var sw = Stopwatch.StartNew();
            while (!File.Exists(installedExe) && sw.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }
            Assert.True(File.Exists(installedExe), $"Installed executable not found at {installedExe}");

            // 2. Launch with --background
            var appPsi = new ProcessStartInfo
            {
                FileName = installedExe,
                Arguments = "--background",
                UseShellExecute = false
            };
            using var appProc = Process.Start(appPsi);
            Assert.NotNull(appProc);

            await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(appProc.HasExited, "Installed app exited prematurely");

            // 3. Set Run value
            LaunchAtLogin.SetEnabled(true, installedExe);
            Assert.True(LaunchAtLogin.IsEnabled());

            // 4. Signal quit
            try
            {
                using var quitHandle = EventWaitHandle.OpenExisting(@"Local\Lightshot.Quit");
                quitHandle.Set();
            }
            catch { }

            await appProc.WaitForExitAsync(TestContext.Current.CancellationToken);

            // 5. Run Update.exe --uninstall --silent with LIGHTSHOT_TEST_MODE=1
            var uninstallPsi = new ProcessStartInfo
            {
                FileName = updateExe,
                Arguments = "--uninstall --silent",
                UseShellExecute = false
            };
            uninstallPsi.EnvironmentVariables["LIGHTSHOT_TEST_MODE"] = "1";
            using (var uninstallProc = Process.Start(uninstallPsi))
            {
                Assert.NotNull(uninstallProc);
                await uninstallProc.WaitForExitAsync(TestContext.Current.CancellationToken);
            }

            // Wait up to 30s for installRoot to be gone
            sw.Restart();
            while (Directory.Exists(installRoot) && sw.Elapsed < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(500, TestContext.Current.CancellationToken);
            }

            // 6. Assertions
            Assert.False(LaunchAtLogin.IsEnabled());
            Assert.False(Directory.Exists(installRoot));
            // settings.json is preserved
            Assert.True(File.Exists(settingsFile));
        }
        finally
        {
            if (settingsBefore != null) File.WriteAllBytes(settingsFile, settingsBefore);
            else if (File.Exists(settingsFile)) File.Delete(settingsFile);
            // A Run value that pointed elsewhere before the test cannot be rebuilt; only the
            // value this test created is removed.
            if (!runAtLoginBefore && LaunchAtLogin.IsEnabled()) LaunchAtLogin.SetEnabled(false);
        }
    }
}
