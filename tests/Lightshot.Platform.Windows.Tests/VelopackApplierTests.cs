using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Velopack.Locators;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class VelopackApplierTests
{
    [Fact]
    [Unit]
    public void ReportsInstalledVersionFromLocator()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "lightshot-applier-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testLocator = new TestVelopackLocator("LightshotApp", "1.0.0", tempDir, null);
            var applierWithLocator = new VelopackApplier(locator: testLocator);

            Assert.True(applierWithLocator.IsInstalled());
            Assert.Equal("1.0.0", applierWithLocator.InstalledVersion());

            // Without locator in test host (no VelopackApp.Build().Run())
            var applierDefault = new VelopackApplier();
            Assert.False(applierDefault.IsInstalled());
            Assert.Null(applierDefault.InstalledVersion());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public async Task PreparesNewerStagedPackage()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-applier-test-" + Guid.NewGuid().ToString("N"));
        string stagingDir = Path.Combine(baseTemp, "staging");
        string packagesDir = Path.Combine(baseTemp, "packages");
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(packagesDir);

        try
        {
            byte[] packageBytes = UpdateFeed.MinimalNupkg("LightshotApp", "9.9.9");
            string packageFileName = "LightshotApp-9.9.9-full.nupkg";
            string sha256Hex = Convert.ToHexString(SHA256.HashData(packageBytes));

            var handler = new UrlMapHandler();
            handler.Map("https://example.com/" + packageFileName, packageBytes);
            var client = new HttpClient(handler);
            var stager = new UpdateStager(client);
            var manifest = new UpdateManifest
            {
                Version = "9.9.9",
                Sequence = 10,
                Package = packageFileName,
                Sha256 = sha256Hex,
                Size = packageBytes.Length
            };

            var state = new UpdateState();
            bool staged = await stager.StagePackageAsync(manifest, stagingDir, state, "https://example.com/", ct: TestContext.Current.CancellationToken);
            Assert.True(staged);

            var locator = new TestVelopackLocator("LightshotApp", "1.0.0", packagesDir, null);
            var applier = new VelopackApplier(locator: locator);

            var asset = await applier.PrepareStagedUpdateAsync(stagingDir, TestContext.Current.CancellationToken);
            Assert.NotNull(asset);
            Assert.Equal("9.9.9", asset.Version?.ToNormalizedString());

            // packagesDir holds a file whose SHA-256 equals the staged one
            string preparedFile = Path.Combine(packagesDir, packageFileName);
            Assert.True(File.Exists(preparedFile));
            byte[] preparedBytes = await File.ReadAllBytesAsync(preparedFile);
            string preparedSha256 = Convert.ToHexString(SHA256.HashData(preparedBytes));
            Assert.Equal(sha256Hex, preparedSha256);
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
    public async Task IgnoresStagedPackageNotNewerThanInstalled()
    {
        string baseTemp = Path.Combine(Path.GetTempPath(), "lightshot-applier-test-" + Guid.NewGuid().ToString("N"));
        string stagingDir = Path.Combine(baseTemp, "staging");
        string packagesDir = Path.Combine(baseTemp, "packages");
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(packagesDir);

        try
        {
            byte[] packageBytes = UpdateFeed.MinimalNupkg("LightshotApp", "9.9.9");
            string packageFileName = "LightshotApp-9.9.9-full.nupkg";
            string sha256Hex = Convert.ToHexString(SHA256.HashData(packageBytes));

            var handler = new UrlMapHandler();
            handler.Map("https://example.com/" + packageFileName, packageBytes);
            var client = new HttpClient(handler);
            var stager = new UpdateStager(client);
            var manifest = new UpdateManifest
            {
                Version = "9.9.9",
                Sequence = 10,
                Package = packageFileName,
                Sha256 = sha256Hex,
                Size = packageBytes.Length
            };

            var state = new UpdateState();
            bool staged = await stager.StagePackageAsync(manifest, stagingDir, state, "https://example.com/", ct: TestContext.Current.CancellationToken);
            Assert.True(staged);

            // Locator at same version 9.9.9 (not newer)
            var locator = new TestVelopackLocator("LightshotApp", "9.9.9", packagesDir, null);
            var applier = new VelopackApplier(locator: locator);

            var asset = await applier.PrepareStagedUpdateAsync(stagingDir, TestContext.Current.CancellationToken);
            Assert.Null(asset);

            // packagesDir stays empty
            Assert.Empty(Directory.GetFiles(packagesDir));
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
