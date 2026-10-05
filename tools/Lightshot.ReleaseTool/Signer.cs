using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lightshot.ReleaseTool;

public record GeneratedKeyPair(
    string PrivateKeyPem,
    string PublicKeyBase64,
    string FingerprintHex);

public static class Signer
{
    public static GeneratedKeyPair GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privateKeyPem = ecdsa.ExportECPrivateKeyPem();

        byte[] pubBytes = ecdsa.ExportSubjectPublicKeyInfo();
        string publicKeyBase64 = Convert.ToBase64String(pubBytes);

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(pubBytes);
        string fingerprintHex = Convert.ToHexString(hash);

        // Self-test in memory
        bool selfTestOk = VerifySelfTest(ecdsa, pubBytes);
        if (!selfTestOk)
        {
            throw new InvalidOperationException("In-memory self-test failed for generated ECDSA keypair.");
        }

        return new GeneratedKeyPair(privateKeyPem, publicKeyBase64, fingerprintHex);
    }

    public static bool VerifySelfTest(ECDsa ecdsa, byte[] publicKeyBytes)
    {
        byte[] testData = Encoding.UTF8.GetBytes("Lightshot In-Memory Key Self-Test");
        byte[] sig = ecdsa.SignData(testData, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
        return verifier.VerifyData(testData, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    public static ECDsa LoadPrivateKey(string privateKeyContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyContent);

        var ecdsa = ECDsa.Create();
        string trimmed = privateKeyContent.Trim();

        if (trimmed.StartsWith("-----BEGIN", StringComparison.OrdinalIgnoreCase))
        {
            ecdsa.ImportFromPem(trimmed);
            return ecdsa;
        }

        // Try raw Base64 PKCS#8 or EC
        try
        {
            byte[] bytes = Convert.FromBase64String(trimmed);
            ecdsa.ImportPkcs8PrivateKey(bytes, out _);
            return ecdsa;
        }
        catch
        {
            byte[] bytes = Convert.FromBase64String(trimmed);
            ecdsa.ImportECPrivateKey(bytes, out _);
            return ecdsa;
        }
    }

    public static byte[] SignData(byte[] data, string privateKeyContent)
    {
        using var ecdsa = LoadPrivateKey(privateKeyContent);
        return ecdsa.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    public static string SignString(string text, string privateKeyContent)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        byte[] sig = SignData(data, privateKeyContent);
        return Convert.ToBase64String(sig);
    }
}
