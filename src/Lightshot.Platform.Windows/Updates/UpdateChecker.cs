using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Platform.Windows.Updates;

public sealed class UpdateChecker
{
    public const string DefaultManifestUrl = "https://github.com/miken90/lightshot/releases/latest/download/releases.json";

    private readonly HttpClient _httpClient;
    private readonly ManifestVerifier _verifier;
    private readonly Func<bool> _isDisabled;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string>? _logger;
    private readonly string _manifestUrl;

    public UpdateChecker(
        HttpClient httpClient,
        ManifestVerifier verifier,
        Func<bool>? isDisabled = null,
        Func<DateTimeOffset>? clock = null,
        Action<string>? logger = null,
        string manifestUrl = DefaultManifestUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _isDisabled = isDisabled ?? (() => false);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _logger = logger;
        _manifestUrl = manifestUrl;
    }

    public static HttpRequestMessage CreateSafeRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Never attach machine ID, user name, OS build details, or tracking telemetry
        return request;
    }

    public async Task<UpdateManifest?> CheckForUpdateAsync(
        long currentSequence,
        string? expectedPublicKey = null,
        CancellationToken ct = default)
    {
        if (_isDisabled())
        {
            _logger?.Invoke("[UpdateChecker] Update checks are disabled by configuration. Zero requests made.");
            return null;
        }

        try
        {
            // 1. Fetch releases.json
            using var manifestReq = CreateSafeRequest(_manifestUrl);
            using var manifestResp = await _httpClient.SendAsync(manifestReq, ct).ConfigureAwait(false);
            if (!manifestResp.IsSuccessStatusCode)
            {
                _logger?.Invoke($"[UpdateChecker] Failed to fetch manifest from {_manifestUrl}: HTTP {(int)manifestResp.StatusCode}");
                return null;
            }

            string manifestJson = await manifestResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // 2. Fetch releases.json.sig
            string sigUrl = _manifestUrl.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? _manifestUrl + ".sig"
                : _manifestUrl + "/releases.json.sig";

            using var sigReq = CreateSafeRequest(sigUrl);
            using var sigResp = await _httpClient.SendAsync(sigReq, ct).ConfigureAwait(false);
            if (!sigResp.IsSuccessStatusCode)
            {
                _logger?.Invoke($"[UpdateChecker] Failed to fetch manifest signature from {sigUrl}: HTTP {(int)sigResp.StatusCode}");
                return null;
            }

            byte[] sigBytes = await sigResp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

            // 3. Verify manifest
            var verifyResult = _verifier.Verify(manifestJson, sigBytes, currentSequence, expectedPublicKey);
            if (!verifyResult.IsValid || verifyResult.Manifest == null)
            {
                _logger?.Invoke($"[UpdateChecker] Manifest verification failed: {verifyResult.ErrorReason}");
                return null;
            }

            _logger?.Invoke($"[UpdateChecker] Valid update available: v{verifyResult.Manifest.Version} (seq {verifyResult.Manifest.Sequence})");
            return verifyResult.Manifest;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"[UpdateChecker] Exception during update check: {ex.Message}");
            return null;
        }
    }
}
