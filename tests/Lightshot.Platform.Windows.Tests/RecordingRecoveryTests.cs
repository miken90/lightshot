// Ported from LightshotKit/Tests/LightshotKitTests/RecordingRecoveryTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class RecordingRecoveryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScratchStore _store;

    public RecordingRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_rec_recovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new ScratchStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private class FailingRemuxer : IRemuxer
    {
        public RemuxResult Remux(string inputPath, string outputPath)
        {
            return new RemuxResult(false, 0, 0, "Simulated remux corruption");
        }
    }

    [Fact]
    [Unit]
    public async Task DeletesPartialGifAndTooShortTakes()
    {
        // 1. Partial files from interrupted encodes
        string partialGif = Path.Combine(_tempDir, "anim.gif.partial");
        string partialMp4 = Path.Combine(_tempDir, "rec.mp4.partial");
        File.WriteAllText(partialGif, "stale gif data");
        File.WriteAllText(partialMp4, "stale mp4 data");

        // 2. Already finished undelivered files
        string finishedMp4 = Path.Combine(_tempDir, "finished_recording.mp4");
        string finishedGif = Path.Combine(_tempDir, "finished_clip.gif");
        File.WriteAllText(finishedMp4, "finished mp4");
        File.WriteAllText(finishedGif, "finished gif");

        // 3. Stubs and takes shorter than 0.5 s
        string shortTake = _store.CreateTakePath("short");
        File.WriteAllBytes(shortTake, new byte[] { 0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });

        var result = await RecordingRecovery.RecoverAsync(
            _store,
            durationProvider: path => 0.2);

        // Stale partials must be deleted
        Assert.False(File.Exists(partialGif), "Partial GIF must be deleted.");
        Assert.False(File.Exists(partialMp4), "Partial MP4 must be deleted.");

        // Too short take must be deleted
        Assert.False(File.Exists(shortTake), "Take shorter than 0.5s must be deleted.");

        // Finished files offered in recovered list
        Assert.Contains(finishedMp4, result.RecoveredFiles);
        Assert.Contains(finishedGif, result.RecoveredFiles);
        Assert.Empty(result.UnfinalizedTakes);
    }

    [Fact]
    [Unit]
    public async Task KeepsTakeWhoseRemuxFails()
    {
        string failingTake = _store.CreateTakePath("failing");
        byte[] payload = new byte[] { 10, 20, 30, 40, 50 };
        File.WriteAllBytes(failingTake, payload);

        var finalizer = new TakeFinalizer(new FailingRemuxer(), delay: _ => { });

        var result = await RecordingRecovery.RecoverAsync(
            _store,
            finalizer: finalizer,
            durationProvider: path => 2.0);

        // Take must NOT be deleted
        Assert.True(File.Exists(failingTake), "Take whose remux fails must be preserved on disk.");
        Assert.Equal(payload, File.ReadAllBytes(failingTake));

        // Listed as unfinalized
        Assert.Contains(failingTake, result.UnfinalizedTakes);
    }

    [Fact]
    [Media]
    public async Task RemuxesKilledTake()
    {
        MediaFactory.MFStartup().CheckError();

        // Generate synthetic fragmented take
        string takePath = _store.CreateTakePath("killed_take");
        CreateSyntheticFragmentedTake(takePath, durationSeconds: 1.0);

        Assert.True(File.Exists(takePath), "Synthetic take must exist before recovery.");

        var result = await RecordingRecovery.RecoverAsync(_store);

        // Recovered files must contain the progressive MP4
        string expectedFinalMp4 = _store.GetFinalMp4Path(takePath);
        Assert.Contains(expectedFinalMp4, result.RecoveredFiles);
        Assert.True(File.Exists(expectedFinalMp4), "Delivered MP4 must exist.");
        Assert.True(new FileInfo(expectedFinalMp4).Length > 0, "Delivered MP4 must be non-empty.");
        Assert.Empty(result.UnfinalizedTakes);
    }

    private static void CreateSyntheticFragmentedTake(string path, double durationSeconds)
    {
        var result = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device? device);

        if (result.Failure || device == null)
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                out device).CheckError();
        }

        using (device)
        {
            int width = 320;
            int height = 240;
            int fps = 30;
            int totalFrames = (int)Math.Round(durationSeconds * fps);

            var texDesc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.None
            };

            using var nv12Tex = device!.CreateTexture2D(texDesc);
            using var writer = new MfFragmentedWriter(
                path,
                device,
                width: width,
                height: height,
                fps: fps,
                audioTrackCount: 0,
                useHevc: false,
                bitRate: 1_000_000
            );

            long frameDurationHns = 10_000_000 / fps;
            for (int i = 0; i < totalFrames; i++)
            {
                long sampleTimeHns = i * frameDurationHns;
                writer.WriteVideoFrame(nv12Tex, sampleTimeHns, frameDurationHns);
            }

            writer.FinalizeWriting(stripMfra: false);
        }
    }
}
