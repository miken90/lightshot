using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class UpdateStagerTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(StatusCode);
            if (StatusCode == HttpStatusCode.OK)
            {
                response.Content = new ByteArrayContent(Payload);
            }
            return Task.FromResult(response);
        }
    }

    [Fact]
    [Unit]
    public async Task RejectsHashMismatch()
    {
        byte[] packageData = Encoding.UTF8.GetBytes("Lightshot installer payload bytes for testing");
        string wrongHash = new string('0', 64); // Does not match packageData hash

        var handler = new FakeHttpMessageHandler { Payload = packageData };
        var client = new HttpClient(handler);
        var stager = new UpdateStager(client);

        var manifest = new UpdateManifest
        {
            Version = "1.0.0",
            Sequence = 1,
            Package = "Lightshot-win-Setup.exe",
            Sha256 = wrongHash,
            Size = packageData.Length
        };

        string tempStaging = Path.Combine(Path.GetTempPath(), "lightshot-test-staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = new UpdateState();
            bool result = await stager.StagePackageAsync(manifest, tempStaging, state, "https://example.com/", ct: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.False(state.IsStaged);
            Assert.Contains("SHA-256 mismatch", state.LastError);

            // Staged file must NOT exist on hash mismatch
            string targetPath = Path.Combine(tempStaging, "Lightshot-win-Setup.exe");
            Assert.False(File.Exists(targetPath));
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                Directory.Delete(tempStaging, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public async Task StagesPackageOnHashMatch()
    {
        byte[] packageData = Encoding.UTF8.GetBytes("Valid installer package binary content");
        using var sha256 = SHA256.Create();
        string correctHash = Convert.ToHexString(sha256.ComputeHash(packageData));

        var handler = new FakeHttpMessageHandler { Payload = packageData };
        var client = new HttpClient(handler);
        var stager = new UpdateStager(client);

        var manifest = new UpdateManifest
        {
            Version = "1.1.0",
            Sequence = 2,
            Package = "Lightshot-win-Setup.exe",
            Sha256 = correctHash,
            Size = packageData.Length
        };

        string tempStaging = Path.Combine(Path.GetTempPath(), "lightshot-test-staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = new UpdateState();
            bool result = await stager.StagePackageAsync(manifest, tempStaging, state, "https://example.com/", ct: TestContext.Current.CancellationToken);

            Assert.True(result);
            Assert.True(state.IsStaged);
            Assert.Equal("1.1.0", state.StagedVersion);
            Assert.Equal(2, state.StagedSequence);
            Assert.Equal(correctHash, state.StagedSha256);

            string targetPath = Path.Combine(tempStaging, "Lightshot-win-Setup.exe");
            Assert.True(File.Exists(targetPath));
            byte[] fileBytes = await File.ReadAllBytesAsync(targetPath, TestContext.Current.CancellationToken);
            Assert.Equal(packageData, fileBytes);
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                Directory.Delete(tempStaging, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public async Task HandlesDownloadFailureCleanly()
    {
        var handler = new FakeHttpMessageHandler { StatusCode = HttpStatusCode.InternalServerError };
        var client = new HttpClient(handler);
        var stager = new UpdateStager(client);

        var manifest = new UpdateManifest
        {
            Version = "1.2.0",
            Sequence = 3,
            Package = "Setup.exe",
            Sha256 = new string('a', 64),
            Size = 100
        };

        string tempStaging = Path.Combine(Path.GetTempPath(), "lightshot-test-staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = new UpdateState();
            bool result = await stager.StagePackageAsync(manifest, tempStaging, state, "https://example.com/", ct: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.False(state.IsStaged);
            Assert.Contains("Package download failed", state.LastError);
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                Directory.Delete(tempStaging, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public async Task WritesVelopackFeedForVerifiedPackage()
    {
        byte[] packageBytes = UpdateFeed.MinimalNupkg("LightshotApp", "9.9.9");
        string packageFileName = "LightshotApp-9.9.9-full.nupkg";
        string sha256Hex = Convert.ToHexString(SHA256.HashData(packageBytes));
        string sha1Hex = Convert.ToHexString(SHA1.HashData(packageBytes));

        string tempStaging = Path.Combine(Path.GetTempPath(), "lightshot-test-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempStaging);

        try
        {
            // Place an older nupkg in staging beforehand
            string oldNupkgPath = Path.Combine(tempStaging, "LightshotApp-9.9.8-full.nupkg");
            await File.WriteAllBytesAsync(oldNupkgPath, new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);

            var handler = new UrlMapHandler();
            string packageUrl = "https://example.com/" + packageFileName;
            handler.Map(packageUrl, packageBytes);

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
            bool result = await stager.StagePackageAsync(manifest, tempStaging, state, "https://example.com/", ct: TestContext.Current.CancellationToken);

            Assert.True(result);
            Assert.True(state.IsStaged);

            string feedPath = Path.Combine(tempStaging, UpdateStager.FeedFileName);
            Assert.True(File.Exists(feedPath));

            string feedJson = await File.ReadAllTextAsync(feedPath, TestContext.Current.CancellationToken);
            var feed = Velopack.VelopackAssetFeed.FromJson(feedJson);
            Assert.NotNull(feed);
            var asset = Assert.Single(feed.Assets);
            Assert.Equal("LightshotApp", asset.PackageId);
            Assert.Equal("9.9.9", asset.Version?.ToNormalizedString());
            Assert.Equal(Velopack.VelopackAssetType.Full, asset.Type);
            Assert.Equal(packageFileName, asset.FileName);
            Assert.Equal(sha1Hex, asset.SHA1);
            Assert.Equal(sha256Hex, asset.SHA256);
            Assert.Equal(packageBytes.Length, asset.Size);

            // Verify older package is removed
            Assert.False(File.Exists(oldNupkgPath));
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                Directory.Delete(tempStaging, recursive: true);
            }
        }
    }

    [Fact]
    [Unit]
    public async Task ReusesVerifiedPackageWithoutDownloading()
    {
        byte[] packageBytes = UpdateFeed.MinimalNupkg("LightshotApp", "9.9.9");
        string packageFileName = "LightshotApp-9.9.9-full.nupkg";
        string sha256Hex = Convert.ToHexString(SHA256.HashData(packageBytes));

        string tempStaging = Path.Combine(Path.GetTempPath(), "lightshot-test-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempStaging);

        try
        {
            var handler = new UrlMapHandler();
            string packageUrl = "https://example.com/" + packageFileName;
            handler.Map(packageUrl, packageBytes);

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

            // Stage first time
            var state1 = new UpdateState();
            bool result1 = await stager.StagePackageAsync(manifest, tempStaging, state1, "https://example.com/", ct: TestContext.Current.CancellationToken);
            Assert.True(result1);
            Assert.Single(handler.Requests);

            // Stage second time with recording handler that has no mappings
            var recordingHandler = new UrlMapHandler();
            var recordingClient = new HttpClient(recordingHandler);
            var stager2 = new UpdateStager(recordingClient);

            var state2 = new UpdateState();
            bool result2 = await stager2.StagePackageAsync(manifest, tempStaging, state2, "https://example.com/", ct: TestContext.Current.CancellationToken);
            Assert.True(result2);
            Assert.True(state2.IsStaged);
            Assert.Empty(recordingHandler.Requests);

            // Feed still exists and matches
            string feedPath = Path.Combine(tempStaging, UpdateStager.FeedFileName);
            Assert.True(File.Exists(feedPath));
            string feedJson = await File.ReadAllTextAsync(feedPath, TestContext.Current.CancellationToken);
            var feed = Velopack.VelopackAssetFeed.FromJson(feedJson);
            Assert.NotNull(feed);
            Assert.Single(feed.Assets);
        }
        finally
        {
            if (Directory.Exists(tempStaging))
            {
                Directory.Delete(tempStaging, recursive: true);
            }
        }
    }
}
