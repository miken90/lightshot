using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Updates;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class UpdateCheckerTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> CapturedRequests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (string privKeyPem, string pubKeyBase64) GenerateKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKeyPem(), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    [Unit]
    public async Task RequestCarriesNoIdentifyingHeaders()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new HttpClient(handler);
        var verifier = new ManifestVerifier();

        var checker = new UpdateChecker(
            httpClient: client,
            verifier: verifier,
            isDisabled: () => false,
            manifestUrl: "https://example.com/releases.json"
        );

        await checker.CheckForUpdateAsync(currentSequence: 0, ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(handler.CapturedRequests);
        var request = handler.CapturedRequests[0];

        // Verify only standard safe headers are sent
        string[] forbiddenHeaderSubstrings =
        [
            "Machine", "User", "HostName", "DeviceId", "Hardware", "OS", "Windows", "Telemetry", "Tracking", "Token", "Bearer", "Cookie"
        ];

        foreach (var header in request.Headers)
        {
            foreach (string forbidden in forbiddenHeaderSubstrings)
            {
                Assert.DoesNotContain(forbidden, header.Key, StringComparison.OrdinalIgnoreCase);
                foreach (string val in header.Value)
                {
                    Assert.DoesNotContain(forbidden, val, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    [Unit]
    public async Task DisabledSettingSendsNothing()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new HttpClient(handler);
        var verifier = new ManifestVerifier();

        var checker = new UpdateChecker(
            httpClient: client,
            verifier: verifier,
            isDisabled: () => true, // Disabled setting
            manifestUrl: "https://example.com/releases.json"
        );

        var result = await checker.CheckForUpdateAsync(currentSequence: 0, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(handler.CapturedRequests); // Gate 4: Zero requests counted via fake handler
    }

    [Fact]
    [Unit]
    public async Task Http404ReturnsNullSilently()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
        var client = new HttpClient(handler);
        var verifier = new ManifestVerifier();

        var checker = new UpdateChecker(
            httpClient: client,
            verifier: verifier,
            manifestUrl: "https://example.com/releases.json"
        );

        var result = await checker.CheckForUpdateAsync(currentSequence: 0, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
        Assert.Single(handler.CapturedRequests);
    }

    [Fact]
    [Unit]
    public async Task ValidManifestAndSignatureReturnsManifest()
    {
        var (privKey, pubKey) = GenerateKeys();
        var manifest = new UpdateManifest
        {
            SchemaVersion = 1,
            Version = "1.5.0",
            Sequence = 100,
            Package = "Lightshot-win-Setup.exe",
            Sha256 = new string('0', 64),
            Size = 10000000
        };
        string manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privKey);
        byte[] sigBytes = ecdsa.SignData(Encoding.UTF8.GetBytes(manifestJson), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        string sigBase64 = Convert.ToBase64String(sigBytes);

        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = req =>
            {
                if (req.RequestUri!.ToString().EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(sigBase64, Encoding.ASCII, "text/plain")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
                };
            }
        };

        var client = new HttpClient(handler);
        var verifier = new ManifestVerifier(pubKey);
        var checker = new UpdateChecker(
            httpClient: client,
            verifier: verifier,
            manifestUrl: "https://example.com/releases.json"
        );

        var result = await checker.CheckForUpdateAsync(currentSequence: 50, expectedPublicKey: pubKey, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("1.5.0", result.Version);
        Assert.Equal(100, result.Sequence);
        Assert.Equal(2, handler.CapturedRequests.Count); // Manifest request and sig request
    }
}
