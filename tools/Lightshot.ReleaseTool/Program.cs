using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lightshot.Platform.Windows.Updates;

namespace Lightshot.ReleaseTool;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "keygen" => HandleKeyGen(args[1..]),
                "sign-manifest" => HandleSignManifest(args[1..]),
                "verify-manifest" => HandleVerifyManifest(args[1..]),
                _ => PrintUsage()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ReleaseTool Error] {ex.Message}");
            return 1;
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Lightshot.ReleaseTool CLI");
        Console.WriteLine("Commands:");
        Console.WriteLine("  keygen --out <privateKeyPath> [--force]");
        Console.WriteLine("  sign-manifest --version <ver> --sequence <seq> --package <pkgFile> --key <privKeyFile> --out-manifest <jsonPath> --out-sig <sigPath> [--notes-url <url>]");
        Console.WriteLine("  verify-manifest --manifest <jsonPath> --sig <sigPath> --pubkey <pubKeyBase64> [--current-sequence <seq>]");
        return 1;
    }

    private static int HandleKeyGen(string[] args)
    {
        string? outPath = null;
        bool force = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length)
            {
                outPath = args[++i];
            }
            else if (args[i] == "--force")
            {
                force = true;
            }
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("Error: --out <privateKeyPath> is required.");
            return 1;
        }

        string fullOutPath = Path.GetFullPath(outPath);
        if (File.Exists(fullOutPath) && !force)
        {
            Console.Error.WriteLine($"Error: Key file already exists at '{fullOutPath}'. Refusing to overwrite without --force.");
            return 2;
        }

        var keyPair = Signer.GenerateKeyPair();

        string? dir = Path.GetDirectoryName(fullOutPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullOutPath, keyPair.PrivateKeyPem, Encoding.ASCII);

        Console.WriteLine($"PublicKey: {keyPair.PublicKeyBase64}");
        Console.WriteLine($"Fingerprint: {keyPair.FingerprintHex}");
        Console.WriteLine($"PrivateKeyPath: {fullOutPath}");
        return 0;
    }

    private static int HandleSignManifest(string[] args)
    {
        string? version = null;
        long sequence = 0;
        string? packagePath = null;
        string? keyPath = null;
        string? outManifest = null;
        string? outSig = null;
        string? notesUrl = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--version" when i + 1 < args.Length:
                    version = args[++i];
                    break;
                case "--sequence" when i + 1 < args.Length:
                    sequence = long.Parse(args[++i]);
                    break;
                case "--package" when i + 1 < args.Length:
                    packagePath = args[++i];
                    break;
                case "--key" when i + 1 < args.Length:
                    keyPath = args[++i];
                    break;
                case "--out-manifest" when i + 1 < args.Length:
                    outManifest = args[++i];
                    break;
                case "--out-sig" when i + 1 < args.Length:
                    outSig = args[++i];
                    break;
                case "--notes-url" when i + 1 < args.Length:
                    notesUrl = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(version) || sequence <= 0 ||
            string.IsNullOrWhiteSpace(packagePath) || string.IsNullOrWhiteSpace(keyPath) ||
            string.IsNullOrWhiteSpace(outManifest) || string.IsNullOrWhiteSpace(outSig))
        {
            Console.Error.WriteLine("Error: Missing required arguments for sign-manifest.");
            return 1;
        }

        var (sha256Hex, sizeBytes) = ManifestWriter.HashAndMeasureFile(packagePath);
        var manifest = ManifestWriter.CreateManifest(version, sequence, packagePath, sha256Hex, sizeBytes, notesUrl);
        string manifestJson = ManifestWriter.SerializeManifest(manifest);

        string privateKeyContent = File.ReadAllText(keyPath).Trim();
        string sigBase64 = Signer.SignString(manifestJson, privateKeyContent);

        string? manifestDir = Path.GetDirectoryName(outManifest);
        if (!string.IsNullOrEmpty(manifestDir))
        {
            Directory.CreateDirectory(manifestDir);
        }

        string? sigDir = Path.GetDirectoryName(outSig);
        if (!string.IsNullOrEmpty(sigDir))
        {
            Directory.CreateDirectory(sigDir);
        }

        File.WriteAllText(outManifest, manifestJson, Encoding.UTF8);
        File.WriteAllText(outSig, sigBase64, Encoding.ASCII);

        // Derive public key from private key to verify generated signature
        using var ecdsa = Signer.LoadPrivateKey(privateKeyContent);
        string pubKeyBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

        var verifier = new ManifestVerifier(pubKeyBase64);
        var verifyResult = verifier.Verify(manifestJson, Encoding.ASCII.GetBytes(sigBase64), sequence - 1);
        if (!verifyResult.IsValid)
        {
            Console.Error.WriteLine($"Verification failed after signing: {verifyResult.ErrorReason}");
            return 1;
        }

        Console.WriteLine($"[ReleaseTool] Manifest successfully written to {outManifest}");
        Console.WriteLine($"[ReleaseTool] Signature successfully written to {outSig}");
        Console.WriteLine($"[ReleaseTool] Package: {manifest.Package} ({sizeBytes} bytes, SHA256: {sha256Hex})");
        return 0;
    }

    private static int HandleVerifyManifest(string[] args)
    {
        string? manifestPath = null;
        string? sigPath = null;
        string? pubKey = null;
        long currentSequence = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manifest" when i + 1 < args.Length:
                    manifestPath = args[++i];
                    break;
                case "--sig" when i + 1 < args.Length:
                    sigPath = args[++i];
                    break;
                case "--pubkey" when i + 1 < args.Length:
                    pubKey = args[++i];
                    break;
                case "--current-sequence" when i + 1 < args.Length:
                    currentSequence = long.Parse(args[++i]);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(sigPath) || string.IsNullOrWhiteSpace(pubKey))
        {
            Console.Error.WriteLine("Error: --manifest, --sig, and --pubkey are required.");
            return 1;
        }

        string manifestJson = File.ReadAllText(manifestPath);
        byte[] sigBytes = File.ReadAllBytes(sigPath);

        var verifier = new ManifestVerifier(pubKey);
        var result = verifier.Verify(manifestJson, sigBytes, currentSequence);

        if (!result.IsValid || result.Manifest == null)
        {
            Console.Error.WriteLine($"Verification failed: {result.ErrorReason}");
            return 1;
        }

        Console.WriteLine($"Valid manifest: v{result.Manifest.Version} (sequence {result.Manifest.Sequence})");
        return 0;
    }
}
