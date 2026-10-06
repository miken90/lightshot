// Ported for Lightshot Windows Port (Phase 7 R3)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.Platform.Windows.Audio;
using Lightshot.TestSupport;
using Xunit;
using PlatformAudioMixer = Lightshot.Platform.Windows.Audio.AudioMixer;

namespace Lightshot.Platform.Windows.Tests;

public class AudioMixerTests
{
    [Fact]
    [Unit]
    public void ApplyGainScalesAndClampsSamples()
    {
        float[] samples = [-0.6f, 0.25f, 0.8f, -0.9f];
        PlatformAudioMixer.ApplyGain(2.0f, samples.AsSpan());

        Assert.Equal(-1.0f, samples[0]);
        Assert.Equal(0.5f, samples[1], 3);
        Assert.Equal(1.0f, samples[2]);
        Assert.Equal(-1.0f, samples[3]);

        PlatformAudioMixer.ApplyGain(0.0f, samples.AsSpan());
        Assert.All(samples, s => Assert.Equal(0.0f, s));
    }

    [Fact]
    [Unit]
    public void MixFramesCombinesSourcesWithVolumesAndClamps()
    {
        float[] mic = [0.4f, -0.5f];
        float[] loopback = [0.3f, -0.6f];

        float[] mixed = PlatformAudioMixer.MixFrames(mic, 1.0f, loopback, 1.0f);
        Assert.Equal(2, mixed.Length);
        Assert.Equal(0.7f, mixed[0], 3);
        Assert.Equal(-1.0f, mixed[1]);

        float[] halfVol = PlatformAudioMixer.MixFrames(mic, 0.5f, loopback, 0.5f);
        Assert.Equal(0.35f, halfVol[0], 3);
        Assert.Equal(-0.55f, halfVol[1], 3);
    }

    [Fact]
    [Unit]
    public void FoldDownToMonoAveragesStereoChannels()
    {
        float[] stereo = [0.8f, 0.2f, -0.6f, -0.4f];
        float[] mono = PlatformAudioMixer.FoldDownToMono(stereo);

        Assert.Equal(2, mono.Length);
        Assert.Equal(0.5f, mono[0], 3);
        Assert.Equal(-0.5f, mono[1], 3);
    }

    [Fact]
    [Unit]
    public void UpmixToStereoDuplicatesMonoChannels()
    {
        float[] mono = [0.75f, -0.25f];
        float[] stereo = PlatformAudioMixer.UpmixToStereo(mono);

        Assert.Equal(4, stereo.Length);
        Assert.Equal(0.75f, stereo[0]);
        Assert.Equal(0.75f, stereo[1]);
        Assert.Equal(-0.25f, stereo[2]);
        Assert.Equal(-0.25f, stereo[3]);
    }

    [Fact]
    [Unit]
    public void AdjustChannelsConvertsBetweenMonoAndStereo()
    {
        float[] stereo = [0.4f, 0.6f];
        float[] toMono = PlatformAudioMixer.AdjustChannels(stereo, 2, 1);
        Assert.Single(toMono);
        Assert.Equal(0.5f, toMono[0], 3);

        float[] mono = [0.3f];
        float[] toStereo = PlatformAudioMixer.AdjustChannels(mono, 1, 2);
        Assert.Equal(2, toStereo.Length);
        Assert.Equal(0.3f, toStereo[0]);
        Assert.Equal(0.3f, toStereo[1]);

        float[] unchanged = PlatformAudioMixer.AdjustChannels(stereo, 2, 2);
        Assert.Equal(stereo, unchanged);
    }

    [Fact]
    [Unit]
    public void ResolveTrackConfigsHandlesOneVsTwoTracks()
    {
        var defaults = new RecordingDefaults { SeparateAudioTracks = false, MonoAudio = false };
        var optionsOneTrack = RecordingOptions.Resolve(new CaptureRegion.DisplayRegion(1u), RecordingOutputKind.Video, defaults);

        var configsOne = PlatformAudioMixer.ResolveTrackConfigs(optionsOneTrack, hasMic: true, hasLoopback: true);
        Assert.Single(configsOne);
        Assert.Equal(2, configsOne[0].Channels);
        Assert.Equal(160_000, configsOne[0].Bitrate);

        defaults.SeparateAudioTracks = true;
        var optionsTwoTracks = RecordingOptions.Resolve(new CaptureRegion.DisplayRegion(1u), RecordingOutputKind.Video, defaults);

        var configsTwo = PlatformAudioMixer.ResolveTrackConfigs(optionsTwoTracks, hasMic: true, hasLoopback: true);
        Assert.Equal(2, configsTwo.Count);
        Assert.Equal(2, configsTwo[0].Channels);
        Assert.Equal(160_000, configsTwo[0].Bitrate);
        Assert.Equal(2, configsTwo[1].Channels);
        Assert.Equal(160_000, configsTwo[1].Bitrate);

        defaults.MonoAudio = true;
        var optionsMono = RecordingOptions.Resolve(new CaptureRegion.DisplayRegion(1u), RecordingOutputKind.Video, defaults);

        var configsMono = PlatformAudioMixer.ResolveTrackConfigs(optionsMono, hasMic: true, hasLoopback: true);
        Assert.Equal(2, configsMono.Count);
        Assert.Equal(1, configsMono[0].Channels);
        Assert.Equal(96_000, configsMono[0].Bitrate);

        var configsMicOnly = PlatformAudioMixer.ResolveTrackConfigs(optionsTwoTracks, hasMic: true, hasLoopback: false);
        Assert.Single(configsMicOnly);

        var configsLoopbackOnly = PlatformAudioMixer.ResolveTrackConfigs(optionsTwoTracks, hasMic: false, hasLoopback: true);
        Assert.Single(configsLoopbackOnly);

        var configsNone = PlatformAudioMixer.ResolveTrackConfigs(optionsTwoTracks, hasMic: false, hasLoopback: false);
        Assert.Empty(configsNone);
    }

    [Fact]
    [Unit]
    public void RoutesSeparateTracksToDedicatedStreams()
    {
        var defaults = new RecordingDefaults { SeparateAudioTracks = true };
        var options = RecordingOptions.Resolve(new CaptureRegion.DisplayRegion(1u), RecordingOutputKind.Video, defaults);

        var writtenSamples = new List<(int Stream, long TimeHns, int ByteLength)>();
        using var mixer = new PlatformAudioMixer(options, hasMic: true, hasLoopback: true, (stream, data, time, dur) =>
        {
            writtenSamples.Add((stream, time, data.Length));
        });

        var micFrames = new PcmFrames([0.1f, 0.1f], 2, 48000, 100_000);
        var loopbackFrames = new PcmFrames([0.2f, 0.2f], 2, 48000, 200_000);

        mixer.OnMicFrames(micFrames);
        mixer.OnLoopbackFrames(loopbackFrames);

        Assert.Equal(2, writtenSamples.Count);
        Assert.Equal(0, writtenSamples[0].Stream);
        Assert.Equal(100_000, writtenSamples[0].TimeHns);
        Assert.Equal(1, writtenSamples[1].Stream);
        Assert.Equal(200_000, writtenSamples[1].TimeHns);
    }

    [Fact]
    [Unit]
    public void PausingDropsAudioFrames()
    {
        var defaults = new RecordingDefaults { SeparateAudioTracks = true };
        var options = RecordingOptions.Resolve(new CaptureRegion.DisplayRegion(1u), RecordingOutputKind.Video, defaults);

        int writeCount = 0;
        using var mixer = new PlatformAudioMixer(options, hasMic: true, hasLoopback: true, (s, d, t, dur) => writeCount++);

        mixer.Pause();
        mixer.OnMicFrames(new PcmFrames([0.1f, 0.1f], 2, 48000, 100_000));
        mixer.OnLoopbackFrames(new PcmFrames([0.2f, 0.2f], 2, 48000, 200_000));

        Assert.Equal(0, writeCount);

        mixer.Resume();
        mixer.OnMicFrames(new PcmFrames([0.1f, 0.1f], 2, 48000, 300_000));
        Assert.Equal(1, writeCount);
    }

    [Fact]
    [Unit]
    public void MicLostSetsMicInactiveGracefully()
    {
        var defaults = new RecordingDefaults { SeparateAudioTracks = false };
        var options = RecordingOptions.Resolve(new CaptureRegion.DisplayRegion(1u), RecordingOutputKind.Video, defaults);

        using var mixer = new PlatformAudioMixer(options, hasMic: true, hasLoopback: true, (s, d, t, dur) => { });

        Assert.True(mixer.HasMic);
        mixer.OnMicLost();
        Assert.False(mixer.HasMic);

        mixer.OnMicFrames(new PcmFrames([0.1f, 0.1f], 2, 48000, 100_000));
    }
}
