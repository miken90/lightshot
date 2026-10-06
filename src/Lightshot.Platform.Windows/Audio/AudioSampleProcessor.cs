// Ported for Lightshot Windows Port (Phase 7 R3)
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Digital signal processing utilities for audio channel conversion, gain scaling, and mixing.
/// </summary>
public static class AudioSampleProcessor
{
    public static void ApplyGain(float gain, Span<float> samples)
    {
        if (gain == 1.0f) return;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Math.Clamp(samples[i] * gain, -1.0f, 1.0f);
        }
    }

    public static float[] FoldDownToMono(ReadOnlySpan<float> stereo)
    {
        int monoCount = stereo.Length / 2;
        float[] mono = new float[monoCount];
        for (int i = 0; i < monoCount; i++)
        {
            mono[i] = Math.Clamp((stereo[2 * i] + stereo[2 * i + 1]) * 0.5f, -1.0f, 1.0f);
        }
        return mono;
    }

    public static float[] UpmixToStereo(ReadOnlySpan<float> mono)
    {
        float[] stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++)
        {
            stereo[2 * i] = mono[i];
            stereo[2 * i + 1] = mono[i];
        }
        return stereo;
    }

    public static float[] AdjustChannels(ReadOnlySpan<float> input, int inputChannels, int targetChannels)
    {
        if (inputChannels == targetChannels) return input.ToArray();
        if (inputChannels == 2 && targetChannels == 1) return FoldDownToMono(input);
        if (inputChannels == 1 && targetChannels == 2) return UpmixToStereo(input);
        return input.ToArray();
    }

    public static float[] MixFrames(ReadOnlySpan<float> mic, float micVolume, ReadOnlySpan<float> loopback, float loopbackVolume)
    {
        int len = Math.Max(mic.Length, loopback.Length);
        float[] result = new float[len];
        for (int i = 0; i < len; i++)
        {
            float m = i < mic.Length ? mic[i] * micVolume : 0f;
            float l = i < loopback.Length ? loopback[i] * loopbackVolume : 0f;
            result[i] = Math.Clamp(m + l, -1.0f, 1.0f);
        }
        return result;
    }

    public static byte[] ToPcm16(ReadOnlySpan<float> samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float f = samples[i];
            short s = f <= -1.0f ? (short)-32768
                : f >= 1.0f ? (short)32767
                : (short)Math.Clamp((int)Math.Round(f * (f < 0 ? 32768.0f : 32767.0f)), -32768, 32767);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }
}
