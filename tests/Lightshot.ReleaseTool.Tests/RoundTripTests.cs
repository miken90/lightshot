using System;
using System.Text;
using Lightshot.Platform.Windows.Updates;
using Lightshot.ReleaseTool;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.ReleaseTool.Tests;

public class RoundTripTests
{
    [Fact]
    [Unit]
    public void SignedManifestVerifiesWithAppVerifier()
    {
        // 1. Generate keypair using release tool signer
        var keyPair = Signer.GenerateKeyPair();
        Assert.NotNull(keyPair.PrivateKeyPem);
        Assert.NotNull(keyPair.PublicKeyBase64);
        Assert.Equal(64, keyPair.FingerprintHex.Length);

        // 2. Create manifest model
        string dummyHash = new string('a', 64);
        var manifest = ManifestWriter.CreateManifest(
            version: "1.0.0",
            sequence: 42,
            packagePathOrName: "Lightshot-win-Setup.exe",
            sha256Hex: dummyHash,
            sizeBytes: 10485760,
            notesUrl: "https://github.com/miken90/lightshot/releases/tag/v1.0.0"
        );
        string manifestJson = ManifestWriter.SerializeManifest(manifest);

        // 3. Sign manifest with private key
        string sigBase64 = Signer.SignString(manifestJson, keyPair.PrivateKeyPem);
        byte[] sigBytes = Encoding.ASCII.GetBytes(sigBase64);

        // 4. Verify with the App's real ManifestVerifier
        var verifier = new ManifestVerifier(keyPair.PublicKeyBase64);
        var result = verifier.Verify(manifestJson, sigBytes, currentSequence: 41);

        Assert.True(result.IsValid, $"Expected valid verification but got error: {result.ErrorReason}");
        Assert.NotNull(result.Manifest);
        Assert.Equal("1.0.0", result.Manifest.Version);
        Assert.Equal(42, result.Manifest.Sequence);
        Assert.Equal("Lightshot-win-Setup.exe", result.Manifest.Package);
        Assert.Equal(dummyHash, result.Manifest.Sha256);
        Assert.Equal(10485760, result.Manifest.Size);
    }

    [Fact]
    [Unit]
    public void TamperedManifestIsRejectedByAppVerifier()
    {
        var keyPair = Signer.GenerateKeyPair();
        var manifest = ManifestWriter.CreateManifest("1.0.0", 10, "Setup.exe", new string('b', 64), 1000);
        string manifestJson = ManifestWriter.SerializeManifest(manifest);
        string sigBase64 = Signer.SignString(manifestJson, keyPair.PrivateKeyPem);

        // Tamper with content
        string tamperedJson = manifestJson.Replace("1.0.0", "2.0.0");

        var verifier = new ManifestVerifier(keyPair.PublicKeyBase64);
        var result = verifier.Verify(tamperedJson, Encoding.ASCII.GetBytes(sigBase64), currentSequence: 9);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
        Assert.Contains("tampered or signature invalid", result.ErrorReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Unit]
    public void WrongKeyIsRejectedByAppVerifier()
    {
        var keyPair1 = Signer.GenerateKeyPair();
        var keyPair2 = Signer.GenerateKeyPair();

        var manifest = ManifestWriter.CreateManifest("1.0.0", 10, "Setup.exe", new string('c', 64), 1000);
        string manifestJson = ManifestWriter.SerializeManifest(manifest);
        string sigBase64 = Signer.SignString(manifestJson, keyPair1.PrivateKeyPem);

        // Verifier configured with keyPair2
        var verifier = new ManifestVerifier(keyPair2.PublicKeyBase64);
        var result = verifier.Verify(manifestJson, Encoding.ASCII.GetBytes(sigBase64), currentSequence: 9);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
    }

    [Fact]
    [Unit]
    public void TruncatedSignatureIsRejectedByAppVerifier()
    {
        var keyPair = Signer.GenerateKeyPair();
        var manifest = ManifestWriter.CreateManifest("1.0.0", 10, "Setup.exe", new string('d', 64), 1000);
        string manifestJson = ManifestWriter.SerializeManifest(manifest);
        string sigBase64 = Signer.SignString(manifestJson, keyPair.PrivateKeyPem);

        byte[] rawSig = Convert.FromBase64String(sigBase64);
        byte[] truncated = rawSig[..10]; // only first 10 bytes

        var verifier = new ManifestVerifier(keyPair.PublicKeyBase64);
        var result = verifier.Verify(manifestJson, truncated, currentSequence: 9);

        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
    }
}
