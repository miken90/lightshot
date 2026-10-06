using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Platform.Windows.Updates;

public sealed class UpdateStager
{
    // Must equal the vpk --packId in scripts/package.ps1 (InstallerSmokeTests.DryRunSucceeds compares it with vpk's own feed).
    public const string VelopackPackageId = "LightshotApp";
    // Velopack's SimpleFileSource reads this feed name on Windows (default channel "win").
    public const string FeedFileName = "releases.win.json";

    private readonly HttpClient _httpClient;
    private readonly Action<string>? _logger;

    public UpdateStager(HttpClient httpClient, Action<string>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    public async Task<bool> StagePackageAsync(
        UpdateManifest manifest,
        string stagingDirectory,
        UpdateState state,
        string? packageBaseUrl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            string packageUrl = manifest.Package;
            if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out _))
            {
                string baseUrl = packageBaseUrl ?? "https://github.com/miken90/lightshot/releases/latest/download/";
                if (!baseUrl.EndsWith('/'))
                {
                    baseUrl += "/";
                }
                packageUrl = baseUrl + manifest.Package;
            }

            string fileName = Path.GetFileName(new Uri(packageUrl, UriKind.RelativeOrAbsolute).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "Setup.exe";
            }

            string tempFilePath = Path.Combine(stagingDirectory, $"{fileName}.{Guid.NewGuid():N}.tmp");
            string targetFilePath = Path.Combine(stagingDirectory, fileName);

            string sha1Hex, sha256Hex;
            long size;

            // Reuse: a package already staged with the manifest's SHA-256 is not downloaded again
            // (about 107 MB per daily check while the user has not restarted).
            bool reused = false;
            if (File.Exists(targetFilePath))
            {
                (sha1Hex, sha256Hex) = HashFile(targetFilePath);
                reused = string.Equals(sha256Hex, manifest.Sha256, StringComparison.OrdinalIgnoreCase);
                size = new FileInfo(targetFilePath).Length;
            }
            else { sha1Hex = sha256Hex = string.Empty; size = 0; }

            if (!reused)
            {
                _logger?.Invoke($"[UpdateStager] Downloading package from {packageUrl} to {tempFilePath}...");

                using (var request = UpdateChecker.CreateSafeRequest(packageUrl))
                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        state.LastError = $"Package download failed: HTTP {(int)response.StatusCode}";
                        _logger?.Invoke($"[UpdateStager] {state.LastError}");
                        return false;
                    }

                    await using var sourceStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                    using var sha256 = SHA256.Create();
                    using var sha1 = SHA1.Create();
                    byte[] buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                        sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
                    }

                    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    byte[] computedHashBytes = sha256.Hash ?? Array.Empty<byte>();
                    string computedHashHex = Convert.ToHexString(computedHashBytes);

                    await fileStream.FlushAsync(ct).ConfigureAwait(false);
                    fileStream.Close();

                    if (!string.Equals(computedHashHex, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        state.LastError = $"SHA-256 mismatch for {fileName}: expected {manifest.Sha256}, got {computedHashHex}";
                        _logger?.Invoke($"[UpdateStager] REJECTED: {state.LastError}");
                        if (File.Exists(tempFilePath))
                        {
                            File.Delete(tempFilePath);
                        }
                        return false;
                    }

                    sha256Hex = computedHashHex;
                    sha1Hex = Convert.ToHexString(sha1.Hash ?? Array.Empty<byte>());
                    size = new FileInfo(tempFilePath).Length;
                }

                if (File.Exists(targetFilePath))
                {
                    File.Delete(targetFilePath);
                }
                File.Move(tempFilePath, targetFilePath);
            }
            else
            {
                _logger?.Invoke($"[UpdateStager] Reusing verified package already staged at {targetFilePath}.");
            }

            WriteVelopackFeed(stagingDirectory, manifest, fileName, sha1Hex, sha256Hex, size);

            state.IsStaged = true;
            state.StagedVersion = manifest.Version;
            state.StagedSequence = manifest.Sequence;
            state.StagedPackagePath = targetFilePath;
            state.StagedSha256 = manifest.Sha256;
            state.LastError = null;

            _logger?.Invoke($"[UpdateStager] Package verified and staged at {targetFilePath} (SHA-256: {manifest.Sha256}).");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.LastError = $"Staging exception: {ex.Message}";
            _logger?.Invoke($"[UpdateStager] Error: {state.LastError}");
            return false;
        }
    }

    private static void WriteVelopackFeed(string stagingDirectory, UpdateManifest manifest, string fileName, string sha1Hex, string sha256Hex, long size)
    {
        var feed = new
        {
            Assets = new[]
            {
                new
                {
                    PackageId = VelopackPackageId,
                    Version = manifest.Version,
                    Type = "Full",
                    FileName = fileName,
                    SHA1 = sha1Hex,
                    SHA256 = sha256Hex,
                    Size = size
                }
            }
        };
        File.WriteAllText(Path.Combine(stagingDirectory, FeedFileName), JsonSerializer.Serialize(feed));
        foreach (var old in Directory.EnumerateFiles(stagingDirectory, "*.nupkg"))
        {
            if (!string.Equals(Path.GetFileName(old), fileName, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(old);
            }
        }
    }

    private static (string Sha1, string Sha256) HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string sha1 = Convert.ToHexString(SHA1.HashData(stream));
        stream.Position = 0;
        string sha256 = Convert.ToHexString(SHA256.HashData(stream));
        return (sha1, sha256);
    }
}
