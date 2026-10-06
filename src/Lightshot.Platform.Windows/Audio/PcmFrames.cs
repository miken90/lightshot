// Ported from App/Sources/PCMBuffer.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Interleaved 32-bit floating point audio frames with presentation timestamp and duration.
/// </summary>
public sealed class PcmFrames
{
    public float[] Samples { get; set; }
    public int Channels { get; }
    public double SampleRate { get; }
    public long TimestampHns { get; set; }
    public long DurationHns { get; set; }

    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;

    public PcmFrames(float[] samples, int channels, double sampleRate, long timestampHns)
    {
        Samples = samples ?? [];
        Channels = Math.Max(1, channels);
        SampleRate = sampleRate > 0 ? sampleRate : 48000.0;
        TimestampHns = timestampHns;
        DurationHns = (long)((FrameCount / SampleRate) * 10_000_000.0);
    }

    /// <summary>
    /// RMS level mapped over a 50 dB range to [0, 1].
    /// </summary>
    public float Level
    {
        get
        {
            if (Samples.Length == 0) return 0f;
            double sumOfSquares = 0.0;
            for (int i = 0; i < Samples.Length; i++)
            {
                float s = Samples[i];
                sumOfSquares += (double)s * s;
            }
            float rms = (float)Math.Sqrt(sumOfSquares / Samples.Length);
            if (rms <= 0f) return 0f;
            float db = 20f * MathF.Log10(rms);
            return Math.Clamp((db + 50f) / 50f, 0f, 1f);
        }
    }

    /// <summary>
    /// Applies gain scaling and clamps samples to [-1.0, 1.0].
    /// </summary>
    public void ApplyGain(float gain)
    {
        AudioMixer.ApplyGain(gain, Samples.AsSpan());
    }

    /// <summary>
    /// Converts 32-bit float samples to 16-bit signed PCM little-endian byte array.
    /// </summary>
    public byte[] ToPcm16Bytes()
    {
        byte[] bytes = new byte[Samples.Length * 2];
        for (int i = 0; i < Samples.Length; i++)
        {
            float f = Samples[i];
            short s;
            if (f <= -1.0f) s = -32768;
            else if (f >= 1.0f) s = 32767;
            else s = (short)Math.Clamp((int)Math.Round(f * (f < 0 ? 32768.0f : 32767.0f)), -32768, 32767);

            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    /// <summary>
    /// Creates a silent frame buffer of the given count.
    /// </summary>
    public static PcmFrames CreateSilence(int frameCount, int channels, double sampleRate, long timestampHns)
    {
        int totalSamples = Math.Max(0, frameCount) * Math.Max(1, channels);
        return new PcmFrames(new float[totalSamples], channels, sampleRate, timestampHns);
    }
}
