// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Tests;

/// <summary>
/// Decodes finalized MP4 files to measure video flash onset, audio 1 kHz beep onset,
/// frame counts, and dropped frame percentages without third-party tools.
/// </summary>
public static class SyncTakeAnalyzer
{
    private const int AudioSampleRate = 48000;
    private const double BeepFrequency = 1000.0;

    public static (double? VideoOnset, double? AudioOnset) MeasureOnsets(string mp4Path, double expectedSec)
    {
        double? videoOnset = DetectFlashOnset(mp4Path, expectedSec);
        double? audioOnset = DetectBeepOnset(mp4Path, expectedSec);
        return (videoOnset, audioOnset);
    }

    public static double? DetectFlashOnset(string mp4Path, double expectedSec)
    {
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(mp4Path, null);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        using var nv12 = MediaFactory.MFCreateMediaType();
        nv12.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        nv12.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, nv12);

        using var currentType = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        MediaFactory.MFGetAttributeSize(currentType, MediaTypeAttributeKeys.FrameSize, out uint w, out uint h);
        int stride = (int)w;
        try { stride = Math.Abs((int)currentType.GetUInt32(MediaTypeAttributeKeys.DefaultStride)); } catch { }
        if (stride < w) stride = (int)w;

        // Sample flash panel center: (200, 100)
        int sampleX = Math.Min((int)w - 1, 200);
        int sampleY = Math.Min((int)h - 1, 100);

        while (true)
        {
            using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None,
                out _, out var flags, out long ts);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream) || flags.HasFlag(SourceReaderFlag.Error)) break;
            if (sample == null) continue;

            double ptsSec = ts / 10_000_000.0;
            if (ptsSec < expectedSec - 0.5) continue;
            if (ptsSec > expectedSec + 1.0) break;

            using var buf = sample.ConvertToContiguousBuffer();
            buf.Lock(out IntPtr pData, out _, out _);
            try
            {
                byte luma = Marshal.ReadByte(pData, sampleY * stride + sampleX);
                if (luma > 128) return ptsSec;
            }
            finally
            {
                buf.Unlock();
            }
        }
        return null;
    }

    public static double? DetectBeepOnset(string mp4Path, double expectedSec)
    {
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(mp4Path, null);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);

        using var pcm = MediaFactory.MFCreateMediaType();
        pcm.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        pcm.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        pcm.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
        pcm.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, AudioSampleRate);
        pcm.Set(MediaTypeAttributeKeys.AudioNumChannels, 2);
        pcm.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4);
        pcm.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioSampleRate * 4);
        reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, pcm);

        var samples = new List<(double TimeSec, float Value)>();
        while (true)
        {
            using var sample = reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None,
                out _, out var flags, out long ts);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream) || flags.HasFlag(SourceReaderFlag.Error)) break;
            if (sample == null) continue;

            double ptsSec = ts / 10_000_000.0;
            if (ptsSec < expectedSec - 0.5) continue;
            if (ptsSec > expectedSec + 1.0) break;

            using var buf = sample.ConvertToContiguousBuffer();
            buf.Lock(out IntPtr pData, out _, out int len);
            short[] s16 = new short[len / 2];
            Marshal.Copy(pData, s16, 0, s16.Length);
            buf.Unlock();

            int frames = s16.Length / 2;
            for (int i = 0; i < frames; i++)
            {
                double t = ptsSec + (i / (double)AudioSampleRate);
                float mono = (s16[2 * i] + s16[2 * i + 1]) / 65536.0f;
                samples.Add((t, mono));
            }
        }

        if (samples.Count < 240) return null;

        float[] values = samples.Select(s => s.Value).ToArray();
        const int win = 240; // 5ms
        const int hop = 48;  // 1ms
        var amplitudes = new List<(int Index, double Amp)>();

        for (int i = 0; i <= values.Length - win; i += hop)
        {
            double amp = ToneAmplitude(values, i, win, BeepFrequency, AudioSampleRate);
            amplitudes.Add((i, amp));
        }

        if (amplitudes.Count == 0) return null;
        double peak = amplitudes.Max(a => a.Amp);
        if (peak < 0.05) return null;

        var onset = amplitudes.FirstOrDefault(a => a.Amp >= 0.5 * peak);
        return samples[onset.Index + win / 2].TimeSec;
    }

    public static (int FrameCount, double DroppedPercent) AnalyzeVideoFrames(string mp4Path, int expectedFps, double durationSec)
    {
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(mp4Path, null);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        int count = 0;
        while (true)
        {
            using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None,
                out _, out var flags, out _);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream) || flags.HasFlag(SourceReaderFlag.Error)) break;
            if (sample != null) count++;
        }

        double expected = expectedFps * durationSec;
        double dropped = Math.Max(0, expected - count);
        double droppedPercent = expected > 0 ? (dropped / expected) * 100.0 : 0.0;
        return (count, droppedPercent);
    }

    private static double ToneAmplitude(float[] x, int start, int n, double freq, int rate)
    {
        double w = 2.0 * Math.PI * freq / rate;
        double re = 0, im = 0;
        for (int i = 0; i < n; i++)
        {
            double v = x[start + i];
            re += v * Math.Cos(w * i);
            im -= v * Math.Sin(w * i);
        }
        return 2.0 * Math.Sqrt(re * re + im * im) / n;
    }
}
