// Ported for Lightshot Windows Port (Phase 7 R2)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Vortice.MediaFoundation;
using Xunit;
using CoreVideoCodec = Lightshot.Core.VideoCodec;

namespace Lightshot.Platform.Windows.Tests;

public class WindowsRecordingServiceTests : IDisposable
{
    private readonly string _tempDir;

    public WindowsRecordingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Lightshot_EngineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        try { MediaFactory.MFStartup(); } catch { }
    }

    [Fact]
    [Media]
    public async Task RecordsPrimaryDisplayAndProducesPlayableFaststartMp4()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);
        var primary = DisplayTopology.GetDisplays()[0];

        int fps = 30;
        string outputPath = Path.Combine(_tempDir, "real_take_3s.mp4");

        var defaults = new RecordingDefaults
        {
            Video = new VideoSettings(CoreVideoCodec.H264, fps, MaxResolution.P720, false),
            RecordMicrophone = true,
            RecordComputerAudio = false,
            HighlightClicks = false,
            ShowKeystrokes = false,
            CountdownSeconds = 0
        };

        var options = RecordingOptions.Resolve(
            new CaptureRegion.DisplayRegion(primary.DisplayId),
            RecordingOutputKind.Video,
            defaults);

        using var service = new WindowsRecordingService();

        // 1. Start recording
        var startError = await service.StartAsync(options, outputPath, ev => { });
        Assert.Null(startError);

        // 2. Record for ~3 seconds of live primary display with fake audio source
        await Task.Delay(3100, TestContext.Current.CancellationToken);

        // 3. Stop recording (stop timestamp taken before stopping capture per spec)
        var (finalPath, stopError) = await service.StopAsync();
        Assert.Null(stopError);
        Assert.NotNull(finalPath);
        Assert.True(File.Exists(finalPath), $"Finalized MP4 file should exist at: {finalPath}");

        // 4. Validate Faststart: 'moov' box must precede 'mdat' box
        bool hasMoovBeforeMdat = Mp4Remuxer.HasMoovBeforeMdat(finalPath);
        Assert.True(hasMoovBeforeMdat, "Finished MP4 must have 'moov' box placed before 'mdat' box (faststart).");

        // 5. Validate Playability and streams using Media Foundation SourceReader
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(finalPath, null);
        Assert.NotNull(reader);

        int videoStreams = 0;
        int audioStreams = 0;
        for (int i = 0; i < 8; i++)
        {
            try
            {
                using var nativeType = reader.GetNativeMediaType((SourceReaderIndex)i, 0);
                Guid majorType = nativeType.GetGUID(MediaTypeAttributeKeys.MajorType);
                if (majorType == MediaTypeGuids.Video) videoStreams++;
                else if (majorType == MediaTypeGuids.Audio) audioStreams++;
            }
            catch
            {
                break;
            }
        }

        Assert.Equal(1, videoStreams);
        Assert.True(audioStreams >= 1, "At least 1 audio stream should be present from audio source.");

        // 6. Validate Frame count within 2% of (fps * duration)
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        int frameCount = 0;
        long firstTs = -1;
        long lastTs = -1;

        while (true)
        {
            using var sample = reader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlag.None,
                out _,
                out var flags,
                out long ts);

            if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            if (sample != null)
            {
                if (firstTs < 0) firstTs = ts;
                lastTs = ts;
                frameCount++;
            }
        }

        Assert.True(frameCount > 0, "At least one video frame must be decoded.");
        double durationSeconds = (double)(lastTs - firstTs) / 10_000_000.0;
        Assert.True(durationSeconds >= 2.0, $"Recording duration should be >= 2.0s, was {durationSeconds:F2}s");

        double expectedFrames = fps * durationSeconds;
        double tolerance = Math.Max(2.0, expectedFrames * 0.02);

        Assert.InRange(
            frameCount,
            (int)Math.Floor(expectedFrames - tolerance),
            (int)Math.Ceiling(expectedFrames + tolerance));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
