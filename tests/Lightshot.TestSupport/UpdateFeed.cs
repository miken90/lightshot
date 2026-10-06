using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.TestSupport;

public static class UpdateFeed
{
    public static (string PrivateKeyPem, string PublicKeyBase64) NewThrowawayKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKeyPem(), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    public static (string ManifestJson, string SignatureBase64) SignManifest(
        string privateKeyPem,
        string version,
        long sequence,
        string package,
        byte[] packageBytes)
    {
        string sha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
        var manifest = new
        {
            schemaVersion = 1,
            version = version,
            sequence = sequence,
            package = package,
            sha256 = sha256,
            size = (long)packageBytes.Length,
            notesUrl = $"https://github.com/miken90/lightshot/releases/tag/v{version}"
        };

        string json = JsonSerializer.Serialize(manifest);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        byte[] sigBytes = ecdsa.SignData(
            Encoding.UTF8.GetBytes(json),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        return (json, Convert.ToBase64String(sigBytes));
    }

    public static byte[] MinimalNupkg(string id, string version)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"{id}.nuspec", CompressionLevel.Fastest);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                  <metadata>
                    <id>{id}</id>
                    <version>{version}</version>
                    <title>Lightshot</title>
                    <authors>Lightshot</authors>
                    <description>test</description>
                  </metadata>
                </package>
                """);
        }
        return ms.ToArray();
    }

    public static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lightshot.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }
}

public sealed class UrlMapHandler : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _requests = new();
    private readonly object _lock = new();

    public UrlMapHandler(IDictionary<string, byte[]>? initialMap = null)
    {
        if (initialMap != null)
        {
            foreach (var kvp in initialMap)
            {
                _map[kvp.Key] = kvp.Value;
            }
        }
    }

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_lock) { return _requests.ToArray(); }
        }
    }

    public void Map(string url, byte[] data)
    {
        lock (_lock) { _map[url] = data; }
    }

    public void MapText(string url, string text) =>
        Map(url, Encoding.UTF8.GetBytes(text));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        lock (_lock)
        {
            _requests.Add(url);
            if (_map.TryGetValue(url, out var data))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(data)
                };
                return Task.FromResult(response);
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            ReasonPhrase = $"URL not mapped: {url}"
        });
    }
}
