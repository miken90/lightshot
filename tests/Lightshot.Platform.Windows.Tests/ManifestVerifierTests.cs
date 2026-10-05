using System;
using System.Security.Cryptography;
using System.Text;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class ManifestVerifierTests
{
    private static (string privateKeyPem, string publicKeyBase64) GenerateTestKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKeyPem(), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    private static (string manifestJson, byte[] signatureBytes) CreateAndSignManifest(
        string privateKeyPem,
        long sequence = 10,
        string version = "1.2.0",
        string? customSha256 = null)
    {
        string sha256 = customSha256 ?? new string('f', 64);
        var manifest = new UpdateManifest
        {
            SchemaVersion = 1,
            Version = version,
            Sequence = sequence,
            Package = "Lightshot-win-Setup.exe",
            Sha256 = sha256,
            Size = 52428800,
            NotesUrl = "https://github.com/miken90/lightshot/releases"
        };
        string json = System.Text.Json.JsonSerializer.Serialize(manifest);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        byte[] sig = ecdsa.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return (json, sig);
    }

    [Fact]
    [Unit]
    public void AcceptsValidSignatureAndManifest()
    {
        var (privKey, pubKey) = GenerateTestKeys();
        var (json, sig) = CreateAndSignManifest(privKey, sequence: 5, version: "2.0.0");

        var verifier = new ManifestVerifier(pubKey);
        var result = verifier.Verify(json, sig, currentSequence: 4);

        Assert.True(result.IsValid, result.ErrorReason);
        Assert.NotNull(result.Manifest);
        Assert.Equal("2.0.0", result.Manifest.Version);
        Assert.Equal(5, result.Manifest.Sequence);
    }

    [Fact]
    [Unit]
    public void RejectsTamperedManifest()
    {
        var (privKey, pubKey) = GenerateTestKeys();
        var (json, sig) = CreateAndSignManifest(privKey, sequence: 5, version: "2.0.0");

        string tamperedJson = json.Replace("2.0.0", "2.0.1");

        var verifier = new ManifestVerifier(pubKey);
        var result = verifier.Verify(tamperedJson, sig, currentSequence: 4);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
        Assert.Contains("tampered or signature invalid", result.ErrorReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Unit]
    public void RejectsWrongKey()
    {
        var (privKey1, _) = GenerateTestKeys();
        var (_, pubKey2) = GenerateTestKeys();
        var (json, sig) = CreateAndSignManifest(privKey1, sequence: 5, version: "2.0.0");

        var verifier = new ManifestVerifier(pubKey2);
        var result = verifier.Verify(json, sig, currentSequence: 4);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
    }

    [Fact]
    [Unit]
    public void RejectsTruncatedSignature()
    {
        var (privKey, pubKey) = GenerateTestKeys();
        var (json, sig) = CreateAndSignManifest(privKey, sequence: 5, version: "2.0.0");

        byte[] truncatedSig = sig[..8];

        var verifier = new ManifestVerifier(pubKey);
        var result = verifier.Verify(json, truncatedSig, currentSequence: 4);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
    }

    [Fact]
    [Unit]
    public void RejectsDowngradeAndReplay()
    {
        var (privKey, pubKey) = GenerateTestKeys();
        var verifier = new ManifestVerifier(pubKey);

        // Case 1: Equal sequence (replay attempt)
        var (jsonEqual, sigEqual) = CreateAndSignManifest(privKey, sequence: 10, version: "1.0.0");
        var resultEqual = verifier.Verify(jsonEqual, sigEqual, currentSequence: 10);
        Assert.False(resultEqual.IsValid);
        Assert.Contains("downgrade or replay rejected", resultEqual.ErrorReason, StringComparison.OrdinalIgnoreCase);

        // Case 2: Lower sequence (downgrade attempt)
        var (jsonLower, sigLower) = CreateAndSignManifest(privKey, sequence: 8, version: "0.9.0");
        var resultLower = verifier.Verify(jsonLower, sigLower, currentSequence: 10);
        Assert.False(resultLower.IsValid);
        Assert.Contains("downgrade or replay rejected", resultLower.ErrorReason, StringComparison.OrdinalIgnoreCase);

        // Case 3: Strictly higher sequence (valid forward update)
        var (jsonHigher, sigHigher) = CreateAndSignManifest(privKey, sequence: 11, version: "1.1.0");
        var resultHigher = verifier.Verify(jsonHigher, sigHigher, currentSequence: 10);
        Assert.True(resultHigher.IsValid);
        Assert.Equal(11, resultHigher.Manifest!.Sequence);
    }

    [Fact]
    [Unit]
    public void RejectsInvalidSha256OrSize()
    {
        var (privKey, pubKey) = GenerateTestKeys();
        var verifier = new ManifestVerifier(pubKey);

        // Invalid sha256 length
        var (jsonBadHash, sigBadHash) = CreateAndSignManifest(privKey, sequence: 20, version: "1.0.0", customSha256: "tooshort");
        var resultBadHash = verifier.Verify(jsonBadHash, sigBadHash, currentSequence: 10);
        Assert.False(resultBadHash.IsValid);

        // Non-positive size
        var badSizeManifest = new UpdateManifest
        {
            SchemaVersion = 1,
            Version = "1.0.0",
            Sequence = 20,
            Package = "Setup.exe",
            Sha256 = new string('a', 64),
            Size = 0
        };
        string badSizeJson = System.Text.Json.JsonSerializer.Serialize(badSizeManifest);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privKey);
        byte[] badSizeSig = ecdsa.SignData(Encoding.UTF8.GetBytes(badSizeJson), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        var resultBadSize = verifier.Verify(badSizeJson, badSizeSig, currentSequence: 10);
        Assert.False(resultBadSize.IsValid);
    }
}
