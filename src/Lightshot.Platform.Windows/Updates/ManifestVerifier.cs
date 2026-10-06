using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lightshot.Platform.Windows.Updates;

public sealed record ManifestVerificationResult(
    bool IsValid,
    UpdateManifest? Manifest,
    string? ErrorReason)
{
    public static ManifestVerificationResult Success(UpdateManifest manifest) =>
        new(true, manifest, null);

    public static ManifestVerificationResult Failure(string reason) =>
        new(false, null, reason);
}

public sealed class ManifestVerifier
{
    private readonly string? _defaultPublicKey;
    private readonly Action<string>? _logger;
    private readonly string? _runningVersion;

    public ManifestVerifier(string? defaultPublicKey = null, Action<string>? logger = null, string? runningVersion = null)
    {
        _defaultPublicKey = defaultPublicKey ?? PinnedKey.PublicKey;
        _logger = logger;
        _runningVersion = runningVersion;
    }

    public ManifestVerificationResult Verify(
        string manifestJson,
        byte[] signatureBytes,
        long currentSequence,
        string? expectedPublicKey = null)
    {
        string publicKey = expectedPublicKey ?? _defaultPublicKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "PLACEHOLDER_KEY")
        {
            return LogAndFail("Public key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return LogAndFail("Manifest payload is empty.");
        }

        if (signatureBytes == null || signatureBytes.Length == 0)
        {
            return LogAndFail("Signature bytes are empty.");
        }

        byte[] sigToVerify = signatureBytes;
        try
        {
            string sigText = Encoding.UTF8.GetString(signatureBytes).Trim();
            if (sigText.Length > 0 && sigText.Length % 4 == 0 && !sigText.Contains(' ') && !sigText.Contains('\n') && !sigText.Contains('\r'))
            {
                sigToVerify = Convert.FromBase64String(sigText);
            }
        }
        catch
        {
            sigToVerify = signatureBytes;
        }

        // 1. Verify cryptographic ECDSA SHA-256 signature
        try
        {
            using var ecdsa = ECDsa.Create();
            byte[] pubKeyBytes = Convert.FromBase64String(publicKey);
            ecdsa.ImportSubjectPublicKeyInfo(pubKeyBytes, out _);

            byte[] dataBytes = Encoding.UTF8.GetBytes(manifestJson);
            bool verified = ecdsa.VerifyData(
                dataBytes,
                sigToVerify,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);

            if (!verified)
            {
                verified = ecdsa.VerifyData(
                    dataBytes,
                    sigToVerify,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }

            if (!verified)
            {
                return LogAndFail("Signature verification failed: manifest tampered or signature invalid.");
            }
        }
        catch (CryptographicException ex)
        {
            return LogAndFail($"Cryptographic error verifying signature: {ex.Message}");
        }
        catch (Exception ex)
        {
            return LogAndFail($"Error decoding public key or signature: {ex.Message}");
        }

        // 2. Parse and validate manifest schema
        UpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            return LogAndFail($"Failed to deserialize manifest JSON: {ex.Message}");
        }

        if (manifest == null)
        {
            return LogAndFail("Deserialized manifest is null.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.Package) ||
            string.IsNullOrWhiteSpace(manifest.Sha256) ||
            manifest.Size <= 0)
        {
            return LogAndFail("Manifest is missing required fields (version, package, sha256, or positive size).");
        }

        if (manifest.Sha256.Length != 64)
        {
            return LogAndFail("Manifest sha256 hash must be 64 hexadecimal characters.");
        }

        // 3. Reject downgrade and replay attacks
        if (manifest.Sequence <= currentSequence)
        {
            return LogAndFail($"Manifest sequence {manifest.Sequence} is not greater than current sequence {currentSequence} (downgrade or replay rejected).");
        }

        // 4. Reject a version that is not newer than the running app (spec: version must be greater than running).
        if (_runningVersion != null)
        {
            if (!Velopack.SemanticVersion.TryParse(manifest.Version, out var offered) ||
                !Velopack.SemanticVersion.TryParse(_runningVersion, out var running))
            {
                return LogAndFail($"Manifest version '{manifest.Version}' or running version '{_runningVersion}' is not a semantic version.");
            }
            if (offered.CompareTo(running) <= 0)
            {
                return LogAndFail($"Manifest version {manifest.Version} is not newer than running version {_runningVersion} (downgrade rejected).");
            }
        }

        return ManifestVerificationResult.Success(manifest);
    }

    private ManifestVerificationResult LogAndFail(string reason)
    {
        _logger?.Invoke($"[ManifestVerifier] Rejected: {reason}");
        return ManifestVerificationResult.Failure(reason);
    }
}
