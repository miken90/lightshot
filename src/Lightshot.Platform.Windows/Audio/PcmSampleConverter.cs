// Lightshot Windows Port
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Buffers.Binary;
using NAudio.Wave;

namespace Lightshot.Platform.Windows.Audio;

/// <summary>
/// Converts and resamples raw audio byte buffers into 32-bit floating point PCM sample arrays.
/// </summary>
public static class PcmSampleConverter
{
    /// <summary>
    /// Converts raw byte buffers of any WaveFormat into 32-bit float samples at target channels and sample rate.
    /// </summary>
    public static float[] ConvertToFloatSamples(ReadOnlySpan<byte> buffer, WaveFormat format, int targetChannels = 2, double targetRate = 48000.0)
    {
        if (buffer.IsEmpty) return [];

        int inputChannels = Math.Max(1, format.Channels);
        int inputRate = format.SampleRate > 0 ? format.SampleRate : 48000;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (format is WaveFormatExtensible ext && ext.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);

        int bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0) bytesPerSample = isFloat ? 4 : 2;

        int frameBytes = inputChannels * bytesPerSample;
        int inputFrames = buffer.Length / frameBytes;
        if (inputFrames <= 0) return [];

        // Extract input frames as float channels
        float[] intermediate = new float[inputFrames * targetChannels];

        for (int f = 0; f < inputFrames; f++)
        {
            int frameOffset = f * frameBytes;
            float left = 0f;
            float right = 0f;

            if (isFloat && bytesPerSample == 4)
            {
                left = BitConverter.ToSingle(buffer.Slice(frameOffset, 4));
                right = inputChannels > 1 ? BitConverter.ToSingle(buffer.Slice(frameOffset + 4, 4)) : left;
            }
            else if (!isFloat && bytesPerSample == 2)
            {
                short sL = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(frameOffset, 2));
                left = sL / 32768.0f;
                if (inputChannels > 1)
                {
                    short sR = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(frameOffset + 2, 2));
                    right = sR / 32768.0f;
                }
                else
                {
                    right = left;
                }
            }
            else if (!isFloat && bytesPerSample == 3) // 24-bit PCM
            {
                int sampleL = (buffer[frameOffset] | (buffer[frameOffset + 1] << 8) | ((sbyte)buffer[frameOffset + 2] << 16));
                left = sampleL / 8388608.0f;
                if (inputChannels > 1)
                {
                    int sampleR = (buffer[frameOffset + 3] | (buffer[frameOffset + 4] << 8) | ((sbyte)buffer[frameOffset + 5] << 16));
                    right = sampleR / 8388608.0f;
                }
                else
                {
                    right = left;
                }
            }

            if (targetChannels == 1)
            {
                intermediate[f] = (left + right) * 0.5f;
            }
            else
            {
                intermediate[f * 2] = left;
                intermediate[f * 2 + 1] = right;
            }
        }

        // Resample if rates differ
        if (inputRate == (int)targetRate)
        {
            return intermediate;
        }

        int outputFrames = (int)((long)inputFrames * targetRate / inputRate);
        float[] resampled = new float[outputFrames * targetChannels];

        for (int i = 0; i < outputFrames; i++)
        {
            double srcIdx = (double)i * inputRate / targetRate;
            int idx0 = (int)srcIdx;
            int idx1 = Math.Min(idx0 + 1, inputFrames - 1);
            float frac = (float)(srcIdx - idx0);

            for (int c = 0; c < targetChannels; c++)
            {
                float v0 = intermediate[idx0 * targetChannels + c];
                float v1 = intermediate[idx1 * targetChannels + c];
                resampled[i * targetChannels + c] = v0 * (1.0f - frac) + v1 * frac;
            }
        }

        return resampled;
    }
}
