// Ported from LightshotKit/Tests/LightshotKitTests/MediaMetadataTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading.Tasks;
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

public class MfMediaMetadataTests : IDisposable
{
    private readonly string _tempDir;

    public MfMediaMetadataTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_metadata_test_" + Guid.NewGuid().ToString("N"));
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
    public async Task ExtractsMetadataAndThumbnail()
    {
        string rawTake = Path.Combine(_tempDir, "metadata_take.frag.mp4");
        string progressiveMp4 = Path.Combine(_tempDir, "metadata_take.mp4");

        int width = 640;
        int height = 360;
        int fps = 30;
        double durationSeconds = 1.0;

        CreateSyntheticVideo(rawTake, width, height, fps, durationSeconds);
        var remuxResult = new Mp4Remuxer().Remux(rawTake, progressiveMp4);
        Assert.True(remuxResult.Success, "Remux to progressive MP4 must succeed.");

        var metadataSource = new MfMediaMetadata();
        var metadata = await metadataSource.VideoMetadataAsync(progressiveMp4);

        Assert.NotNull(metadata);
        Assert.Equal(width, metadata.Value.PixelWidth);
        Assert.Equal(height, metadata.Value.PixelHeight);
        Assert.InRange(metadata.Value.Duration, 0.9, 1.2);

        Assert.False(metadata.Value.ThumbnailPng.IsEmpty, "Thumbnail PNG data must not be empty.");

        // Verify thumbnail is valid PNG and respects thumbnailMaxPixelSize (320)
        using var thumbBitmap = SKBitmap.Decode(metadata.Value.ThumbnailPng.Span);
        Assert.NotNull(thumbBitmap);
        Assert.True(thumbBitmap.Width <= 320, $"Thumbnail width {thumbBitmap.Width} must be <= 320");
        Assert.True(thumbBitmap.Height <= 320, $"Thumbnail height {thumbBitmap.Height} must be <= 320");
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
