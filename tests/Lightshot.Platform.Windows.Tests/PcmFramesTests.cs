using System;
using Lightshot.Platform.Windows.Audio;
using Lightshot.TestSupport;
using NAudio.Wave;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class PcmFramesTests
{
    [Fact]
    [Unit]
    public void CalculatesRmsLevelAccurately()
    {
        // 1. Digital silence
        var silentFrames = PcmFrames.CreateSilence(480, 2, 48000, 0);
        Assert.Equal(0f, silentFrames.Level);

        // 2. Full-scale square wave (+1, -1) -> RMS = 1.0 -> 0 dBFS -> Level = 1.0
        float[] fullScale = new float[960];
        for (int i = 0; i < fullScale.Length; i++) fullScale[i] = (i % 2 == 0) ? 1.0f : -1.0f;
        var fullScaleFrames = new PcmFrames(fullScale, 2, 48000, 0);
        Assert.Equal(1.0f, fullScaleFrames.Level, 2);

        // 3. -20 dBFS signal: amplitude 0.1, RMS 0.1 -> 20*log10(0.1) = -20 dB -> level = (-20 + 50)/50 = 0.60
        float[] minus20Db = new float[960];
        for (int i = 0; i < minus20Db.Length; i++) minus20Db[i] = (i % 2 == 0) ? 0.1f : -0.1f;
        var minus20Frames = new PcmFrames(minus20Db, 2, 48000, 0);
        Assert.Equal(0.60f, minus20Frames.Level, 2);

        // 4. -50 dBFS signal: amplitude 0.003162 -> 20*log10(rms) = -50 dB -> level = 0.0
        float[] minus50Db = new float[960];
        for (int i = 0; i < minus50Db.Length; i++) minus50Db[i] = 0.003162f;
        var minus50Frames = new PcmFrames(minus50Db, 2, 48000, 0);
        Assert.Equal(0.0f, minus50Frames.Level, 2);
    }

    [Fact]
    [Unit]
    public void AppliesGainWithClamping()
    {
        float[] samples = [-0.5f, 0.2f, 0.8f, -0.9f];
        var frames = new PcmFrames(samples, 2, 48000, 0);

        frames.ApplyGain(2.0f);

        Assert.Equal(-1.0f, frames.Samples[0]);
        Assert.Equal(0.4f, frames.Samples[1], 4);
        Assert.Equal(1.0f, frames.Samples[2]);
        Assert.Equal(-1.0f, frames.Samples[3]);
    }

    [Fact]
    [Unit]
    public void ConvertsToPcm16Bytes()
    {
        float[] samples = [-1.0f, 0.0f, 1.0f];
        var frames = new PcmFrames(samples, 1, 48000, 0);

        byte[] pcm16 = frames.ToPcm16Bytes();

        Assert.Equal(6, pcm16.Length);

        // -1.0f -> -32768 (0x8000, little endian: 0x00, 0x80)
        short s0 = (short)(pcm16[0] | (pcm16[1] << 8));
        Assert.Equal(-32768, s0);

        // 0.0f -> 0
        short s1 = (short)(pcm16[2] | (pcm16[3] << 8));
        Assert.Equal(0, s1);

        // 1.0f -> 32767 (0x7FFF, little endian: 0xFF, 0x7F)
        short s2 = (short)(pcm16[4] | (pcm16[5] << 8));
        Assert.Equal(32767, s2);
    }

    [Fact]
    [Unit]
    public void StampingFromQpcAndPauseOffset()
    {
        long startQpcHns = 10_000_000;
        long currentQpcHns = 15_000_000;
        long pauseOffsetHns = 2_000_000;

        long stampedHns = PcmContiguityTracker.ComputeTimestampHns(currentQpcHns, pauseOffsetHns, startQpcHns);

        // 15s - 2s - 10s = 3s (30,000,000 HNS)
        Assert.Equal(3_000_000, stampedHns);
    }

    [Fact]
    [Unit]
    public void ContiguityTrackerPadsInitialGapWithSilence()
    {
        var tracker = new PcmContiguityTracker(2, 48000);

        // Target timestamp is 10 ms into recording (100,000 HNS = 480 frames)
        long targetHns = 100_000;
        float[] incoming = new float[480 * 2]; // 10 ms of incoming audio
        Array.Fill(incoming, 0.5f);

        var aligned = tracker.Align(incoming, targetHns);

        // Aligned should now have 480 frames of silence + 480 frames of audio = 960 frames
        Assert.Equal(960, aligned.FrameCount);
        Assert.Equal(0, aligned.TimestampHns);

        // Initial 480 frames are silence (0)
        for (int i = 0; i < 480 * 2; i++)
        {
            Assert.Equal(0f, aligned.Samples[i]);
        }
        // Remaining 480 frames are incoming data (0.5)
        for (int i = 480 * 2; i < 960 * 2; i++)
        {
            Assert.Equal(0.5f, aligned.Samples[i]);
        }

        Assert.Equal(960, tracker.NextExpectedFrameIndex);
    }

    [Fact]
    [Unit]
    public void ContiguityTrackerMaintainsContiguityAcrossGaps()
    {
        var tracker = new PcmContiguityTracker(2, 48000);

        // Packet 1: 480 frames at 0
        float[] p1 = new float[480 * 2];
        Array.Fill(p1, 0.2f);
        var aligned1 = tracker.Align(p1, 0);
        Assert.Equal(480, aligned1.FrameCount);

        // Packet 2 arrives at 20 ms instead of 10 ms (gap of 10 ms = 480 frames)
        long targetHns = 200_000;
        float[] p2 = new float[480 * 2];
        Array.Fill(p2, 0.4f);
        var aligned2 = tracker.Align(p2, targetHns);

        // Should pad 480 frames of silence + 480 frames of p2 = 960 frames
        Assert.Equal(960, aligned2.FrameCount);
        Assert.Equal(1440, tracker.NextExpectedFrameIndex);
    }

    [Fact]
    [Unit]
    public void ContiguityTrackerTrimsOverlappingFrames()
    {
        var tracker = new PcmContiguityTracker(2, 48000);

        // Packet 1: 480 frames at 0
        float[] p1 = new float[480 * 2];
        tracker.Align(p1, 0);

        // Packet 2 arrives at 5 ms (240 frames) instead of 10 ms (480 frames)
        // Overlap of 240 frames
        long targetHns = 50_000;
        float[] p2 = new float[480 * 2];
        for (int i = 0; i < p2.Length; i++) p2[i] = 0.9f;

        var aligned2 = tracker.Align(p2, targetHns);

        // Overlap of 240 frames trimmed: 480 - 240 = 240 frames remain
        Assert.Equal(240, aligned2.FrameCount);
        Assert.Equal(720, tracker.NextExpectedFrameIndex);
    }

    [Fact]
    [Unit]
    public void ConvertToFloatSamplesHandlesMultipleWaveFormats()
    {
        // 1. 16-bit PCM stereo 48k
        short[] pcm16 = [16384, -16384]; // 0.5f, -0.5f
        byte[] bytes16 = new byte[4];
        Buffer.BlockCopy(pcm16, 0, bytes16, 0, 4);

        var format16 = new WaveFormat(48000, 16, 2);
        float[] converted16 = PcmContiguityTracker.ConvertToFloatSamples(bytes16, format16, 2, 48000);

        Assert.Equal(2, converted16.Length);
        Assert.Equal(0.5f, converted16[0], 2);
        Assert.Equal(-0.5f, converted16[1], 2);

        // 2. 32-bit Float mono 48k converted to stereo 48k
        float[] floatMono = [0.75f];
        byte[] bytesFloat = new byte[4];
        Buffer.BlockCopy(floatMono, 0, bytesFloat, 0, 4);

        var formatFloat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        float[] convertedFloat = PcmContiguityTracker.ConvertToFloatSamples(bytesFloat, formatFloat, 2, 48000);

        Assert.Equal(2, convertedFloat.Length);
        Assert.Equal(0.75f, convertedFloat[0], 2);
        Assert.Equal(0.75f, convertedFloat[1], 2);
    }
}
