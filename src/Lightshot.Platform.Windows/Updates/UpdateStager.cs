using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Platform.Windows.Updates;

public sealed class UpdateStager
{
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
                byte[] buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
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
            }

            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }
            File.Move(tempFilePath, targetFilePath);

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
}
