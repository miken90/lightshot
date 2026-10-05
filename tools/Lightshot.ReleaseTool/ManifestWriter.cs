using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Lightshot.Platform.Windows.Updates;

namespace Lightshot.ReleaseTool;

public static class ManifestWriter
{
    public static (string sha256Hex, long sizeBytes) HashAndMeasureFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Package file not found: {filePath}", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return (Convert.ToHexString(hash), fileInfo.Length);
    }

    public static UpdateManifest CreateManifest(
        string version,
        long sequence,
        string packagePathOrName,
        string sha256Hex,
        long sizeBytes,
        string? notesUrl = null)
    {
        return new UpdateManifest
        {
            SchemaVersion = 1,
            Version = version,
            Sequence = sequence,
            Package = Path.GetFileName(packagePathOrName),
            Sha256 = sha256Hex,
            Size = sizeBytes,
            NotesUrl = notesUrl
        };
    }

    public static string SerializeManifest(UpdateManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
