// Ported for Lightshot Windows Port (Phase 7 R3)
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

namespace Lightshot.Platform.Windows.Tests;

public class AudioTrackRoutingMediaTests : IDisposable
{
    private readonly string _tempDir;

    public AudioTrackRoutingMediaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Lightshot_AudioRouting_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        try { MediaFactory.MFStartup(); } catch { }
    }

    [Fact]
    [Media]
    public async Task TakeWithMicAndLoopbackHasOneAacTrackByDefault()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);
        var primary = displays[0];

        string outputPath = Path.Combine(_tempDir, "take_single_audio_track.mp4");
        var defaults = new RecordingDefaults
        {
            Video = new VideoSettings(Core.VideoCodec.H264, 30, MaxResolution.P720, false),
            RecordMicrophone = true,
            RecordComputerAudio = true,
            SeparateAudioTracks = false,
            CountdownSeconds = 0
        };

        var options = RecordingOptions.Resolve(
            new CaptureRegion.DisplayRegion(primary.DisplayId),
            RecordingOutputKind.Video,
            defaults);

        using var service = new WindowsRecordingService();
        var startError = await service.StartAsync(options, outputPath, ev => { });
        Assert.Null(startError);

        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var (finalPath, stopError) = await service.StopAsync();
        Assert.Null(stopError);
        Assert.NotNull(finalPath);
        Assert.True(File.Exists(finalPath));

        int audioStreams = CountAudioStreams(finalPath);
        Assert.Equal(1, audioStreams);
    }

    [Fact]
    [Media]
    public async Task TakeWithMicAndLoopbackHasTwoAacTracksWhenSeparateTracksEnabled()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);
        var primary = displays[0];

        string outputPath = Path.Combine(_tempDir, "take_separate_audio_tracks.mp4");
        var defaults = new RecordingDefaults
        {
            Video = new VideoSettings(Core.VideoCodec.H264, 30, MaxResolution.P720, false),
            RecordMicrophone = true,
            RecordComputerAudio = true,
            SeparateAudioTracks = true,
            CountdownSeconds = 0
        };

        var options = RecordingOptions.Resolve(
            new CaptureRegion.DisplayRegion(primary.DisplayId),
            RecordingOutputKind.Video,
            defaults);

        using var service = new WindowsRecordingService();
        var startError = await service.StartAsync(options, outputPath, ev => { });
        Assert.Null(startError);

        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var (finalPath, stopError) = await service.StopAsync();
        Assert.Null(stopError);
        Assert.NotNull(finalPath);
        Assert.True(File.Exists(finalPath));

        int audioStreams = CountAudioStreams(finalPath);
        Assert.Equal(2, audioStreams);
    }

    private static int CountAudioStreams(string path)
    {
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
        int audioCount = 0;
        for (int i = 0; i < 8; i++)
        {
            try
            {
                using var nativeType = reader.GetNativeMediaType((SourceReaderIndex)i, 0);
                Guid majorType = nativeType.GetGUID(MediaTypeAttributeKeys.MajorType);
                if (majorType == MediaTypeGuids.Audio) audioCount++;
            }
            catch
            {
                break;
            }
        }
        return audioCount;
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
