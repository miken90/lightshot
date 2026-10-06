// Ported from LightshotKit/Tests/LightshotKitTests/GIFEncoderTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Media;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using SkiaSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class MfGifEncoderTests : IDisposable
{
    private readonly string _tempDir;

    public MfGifEncoderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_gif_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        MediaFactory.MFStartup().CheckError();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    [Media]
    public async Task CancelRemovesPartial()
    {
        string rawTake = Path.Combine(_tempDir, "cancel_take.frag.mp4");
        string progressiveMp4 = Path.Combine(_tempDir, "cancel_take.mp4");
        string outputGif = Path.Combine(_tempDir, "canceled.gif");
        string partialPath = outputGif + ".partial";

        CreateSyntheticVideo(rawTake, 640, 360, 30, 2.0);
        new Mp4Remuxer().Remux(rawTake, progressiveMp4);

        var encoder = new MfGifEncoder();
        using var cts = new CancellationTokenSource();

        var settings = new GIFSettings { Fps = 10, MaxWidth = 320, Optimize = true, Quality = 0.8 };

        // Cancel on the first progress update
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await encoder.EncodeAsync(
                progressiveMp4,
                outputGif,
                settings,
                progress: p =>
                {
                    if (p > 0)
                    {
                        cts.Cancel();
                    }
                },
                cancellationToken: cts.Token);
        });

        // Assert partial and final output files do not exist after cancel
        Assert.False(File.Exists(partialPath), "Partial file must be removed on cancellation.");
        Assert.False(File.Exists(outputGif), "Final output file must not exist on cancellation.");
    }

    [Fact]
    [Media]
    public async Task RoundTripsFramesDelaysAndTransparency()
    {
        string rawTake = Path.Combine(_tempDir, "roundtrip_take.frag.mp4");
        string progressiveMp4 = Path.Combine(_tempDir, "roundtrip_take.mp4");
        string outputGif = Path.Combine(_tempDir, "roundtrip.gif");

        int width = 640;
        int height = 360;
        int fps = 30;
        double durationSeconds = 1.0;

        CreateSyntheticVideo(rawTake, width, height, fps, durationSeconds);
        new Mp4Remuxer().Remux(rawTake, progressiveMp4);

        var encoder = new MfGifEncoder();
        var settings = new GIFSettings { Fps = 10, MaxWidth = 160, Optimize = true, Quality = 0.8 };
        var plan = new GIFFramePlan(durationSeconds, new Size(width, height), settings);

        await encoder.EncodeAsync(
            progressiveMp4,
            outputGif,
            settings,
            progress: _ => { },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputGif), "Encoded GIF must exist.");
        Assert.True(new FileInfo(outputGif).Length > 0, "Encoded GIF must be non-empty.");

        // Read GIF using SkiaSharp SKCodec
        using var codec = SKCodec.Create(outputGif);
        Assert.NotNull(codec);

        // 1. Frame count verification against GIFFramePlan
        Assert.InRange(codec.FrameCount, plan.FrameCount - 1, plan.FrameCount + 1);

        // 2. Delay verification against GIFFramePlan
        int expectedDelayMs = (int)Math.Round(plan.FrameDelay * 1000.0);
        var frameInfos = codec.FrameInfo;
        Assert.NotEmpty(frameInfos);
        foreach (var frame in frameInfos)
        {
            Assert.InRange(frame.Duration, expectedDelayMs - 30, expectedDelayMs + 30);
        }

        // 3. Transparency verification: optimized GIF keeps existing backdrop
        if (frameInfos.Length > 1)
        {
            Assert.Equal(SKCodecAnimationDisposalMethod.Keep, frameInfos[1].DisposalMethod);
        }
    }

    private static void CreateSyntheticVideo(string outputPath, int width, int height, int fps, double durationSeconds)
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
                outputPath,
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

            writer.FinalizeWriting(stripMfra: true);
        }
    }
}
