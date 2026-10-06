// Ported from LightshotKit/Tests/LightshotKitTests/VideoTrimmerTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Media;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class MfVideoTrimmerTests : IDisposable
{
    private readonly string _tempDir;

    public MfVideoTrimmerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_trimmer_test_" + Guid.NewGuid().ToString("N"));
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
    public async Task PassthroughSnapsToPrecedingKeyFrame()
    {
        string rawTake = Path.Combine(_tempDir, "source_take.frag.mp4");
        string progressiveSource = Path.Combine(_tempDir, "source_take.mp4");
        string trimmedOutput = Path.Combine(_tempDir, "trimmed_output.mp4");

        // 1. Create a 4.0-second video with GOP = 2.0s (keyframes at 0s and 2s)
        CreateMultiGopVideo(rawTake, durationSeconds: 4.0, fps: 30);
        var remuxResult = new Mp4Remuxer().Remux(rawTake, progressiveSource);
        Assert.True(remuxResult.Success, "Remux to progressive source must succeed.");

        var trimmer = new MfVideoTrimmer();

        // 2. Trim from 2.5s to 3.5s. Snapped start must snap back to keyframe at ~2.0s
        var trimRange = new TrimRange(2.5, 3.5, 4.0);
        double reportedProgress = 0;

        var result = await trimmer.TrimAsync(
            progressiveSource,
            trimmedOutput,
            trimRange,
            p => reportedProgress = p,
            TestContext.Current.CancellationToken);

        // 3. Verify trim result
        Assert.True(result.Success, $"Trim failed: {result.ErrorMessage}");
        Assert.True(File.Exists(trimmedOutput), "Trimmed output file must exist.");
        Assert.True(new FileInfo(trimmedOutput).Length > 0, "Trimmed output must be non-empty.");

        // Snapped start should snap to keyframe at ~2.0s (within 0.1s tolerance)
        Assert.InRange(result.SnappedStartTime, 1.9, 2.1);

        // Actual duration should be approximately 3.5 - 2.0 = 1.5s
        Assert.InRange(result.Duration, 1.3, 1.7);
        Assert.Equal(1.0, reportedProgress, 0.01);
    }

    private static void CreateMultiGopVideo(string outputPath, double durationSeconds, int fps)
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
            int width = 1280;
            int height = 720;
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
