// Ported from LightshotKit/Tests/LightshotKitTests/AudioMixerTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class AudioMixerTests
{
    [Fact]
    [Unit]
    public void SumsTwoSourcesFrameByFrameAndAdvancesTheCursor()
    {
        var m = new AudioMixer(1, [AudioMixer.Source.Microphone, AudioMixer.Source.Computer]);
        m.Push(AudioMixer.Source.Microphone, [0.1f, 0.2f, 0.3f, 0.4f], 100);
        m.Push(AudioMixer.Source.Computer, [0.5f, 0.5f, 0.5f, 0.5f], 100);
        var @out = m.Drain();
        Assert.NotNull(@out);
        Assert.Equal(100, @out.Value.Start);
        float[] expected = [0.6f, 0.7f, 0.8f, 0.9f];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(@out.Value.Frames[i] - expected[i]) < 0.001f);
        }
        Assert.Null(m.Drain());

        m.Push(AudioMixer.Source.Microphone, [0.1f], 104);
        m.Push(AudioMixer.Source.Computer, [0.1f], 104);
        Assert.Equal(104, m.Drain()?.Start);
    }

    [Fact]
    [Unit]
    public void WaitsForTheLaggingSourceAndPadsItsGapWithSilence()
    {
        var m = new AudioMixer(1, [AudioMixer.Source.Microphone, AudioMixer.Source.Computer]);
        m.Push(AudioMixer.Source.Computer, [0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f], 0);
        Assert.Null(m.Drain());

        m.Push(AudioMixer.Source.Microphone, [0.5f, 0.5f], 2);
        var @out = m.Drain();
        Assert.NotNull(@out);
        Assert.Equal(0, @out.Value.Start);
        float[] expected = [0.4f, 0.4f, 0.9f, 0.9f];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(@out.Value.Frames[i] - expected[i]) < 0.001f);
        }

        m.Push(AudioMixer.Source.Microphone, [0.5f, 0.5f], 6);
        var out2 = m.Drain();
        Assert.NotNull(out2);
        float[] expected2 = [0.4f, 0.4f];
        for (int i = 0; i < expected2.Length; i++)
        {
            Assert.True(Math.Abs(out2.Value.Frames[i] - expected2[i]) < 0.001f);
        }
    }

    [Fact]
    [Unit]
    public void AnInactiveSourceNoLongerHoldsTheMixBack()
    {
        var m = new AudioMixer(1, [AudioMixer.Source.Microphone, AudioMixer.Source.Computer]);
        m.Push(AudioMixer.Source.Computer, [0.2f, 0.2f, 0.2f], 10);
        Assert.Null(m.Drain());

        m.SetInactive(AudioMixer.Source.Microphone);
        var @out = m.Drain();
        Assert.NotNull(@out);
        Assert.Equal([0.2f, 0.2f, 0.2f], @out.Value.Frames);

        m.Push(AudioMixer.Source.Microphone, [9f, 9f], 13);
        m.Push(AudioMixer.Source.Computer, [0.1f], 13);
        var drained = m.Drain();
        Assert.NotNull(drained);
        Assert.Equal([0.1f], drained.Value.Frames);
    }

    [Fact]
    [Unit]
    public void ClipsToUnityAndHandlesStereoInterleaving()
    {
        var m = new AudioMixer(2, [AudioMixer.Source.Microphone, AudioMixer.Source.Computer]);
        m.Push(AudioMixer.Source.Microphone, [0.8f, -0.8f, 0.1f, 0.1f], 0);
        m.Push(AudioMixer.Source.Computer, [0.5f, -0.5f, 0.1f, 0.1f], 0);
        var @out = m.Drain();
        Assert.NotNull(@out);
        float[] expected = [1.0f, -1.0f, 0.2f, 0.2f];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(@out.Value.Frames[i] - expected[i]) < 0.001f);
        }
    }

    [Fact]
    [Unit]
    public void AStalledSourceStopsHoldingTheMixBackAfterMaxLag()
    {
        var m = new AudioMixer(1, [AudioMixer.Source.Microphone, AudioMixer.Source.Computer], maxLag: 3);
        m.Push(AudioMixer.Source.Computer, [0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f], 0);
        var @out = m.Drain();
        Assert.NotNull(@out);
        Assert.Equal(0, @out.Value.Start);
        float[] expected = [0.2f, 0.2f, 0.2f, 0.2f, 0.2f];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(@out.Value.Frames[i] - expected[i]) < 0.001f);
        }

        m.Push(AudioMixer.Source.Microphone, [0.5f, 0.5f, 0.5f], 5);
        var out2 = m.Drain();
        Assert.NotNull(out2);
        float[] expected2 = [0.7f, 0.7f, 0.7f];
        for (int i = 0; i < expected2.Length; i++)
        {
            Assert.True(Math.Abs(out2.Value.Frames[i] - expected2[i]) < 0.001f);
        }
    }

    [Fact]
    [Unit]
    public void GainScalesAndClips()
    {
        float[] samples = [0.25f, -0.25f, 0.75f];
        AudioMixer.ApplyGain(2.0f, samples);
        Assert.Equal([0.5f, -0.5f, 1.0f], samples);

        AudioMixer.ApplyGain(1.0f, samples);
        Assert.Equal([0.5f, -0.5f, 1.0f], samples);

        AudioMixer.ApplyGain(0.0f, samples);
        Assert.Equal([0.0f, 0.0f, 0.0f], samples);
    }

    [Fact]
    [Unit]
    public void OverlappingPushesKeepOnlyTheNewTailAndOldFramesAreDropped()
    {
        var m = new AudioMixer(1, [AudioMixer.Source.Computer]);
        m.Push(AudioMixer.Source.Computer, [0.1f, 0.2f, 0.3f], 0);
        m.Push(AudioMixer.Source.Computer, [0.3f, 0.4f], 2);
        var @out = m.Drain();
        Assert.NotNull(@out);
        float[] expected = [0.1f, 0.2f, 0.3f, 0.4f];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(@out.Value.Frames[i] - expected[i]) < 0.001f);
        }

        m.Push(AudioMixer.Source.Computer, [0.7f], 1);
        Assert.Null(m.Drain());
    }
}
