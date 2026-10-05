using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace RecordingProbe;

/// <summary>Where the source window's blocks land in the decoded frame (output pixels).</summary>
public sealed record SourceLayout(double Dpi, double ScaleX, double ScaleY);

public sealed record VideoFrameInfo(double PtsSec, int Counter, double FlashLuma, bool Ambiguous);

public sealed class VideoAnalysis
{
    public List<VideoFrameInfo> Frames { get; } = new();
    public string? Error { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class AudioTrack
{
    public const int Rate = 48000;
    public float[] Samples { get; set; } = Array.Empty<float>();
    public int Buffers { get; set; }
    public int Discontinuities { get; set; }
    public double GapSeconds { get; set; }
    public double FirstTsSec { get; set; }
    public double EndSec => Samples.Length / (double)Rate;
    public string? Error { get; set; }
    public bool Usable => Error == null && Samples.Length > 0;
}

public sealed record Onset(int Clap, double ExpectedSec, double? OnsetSec, string Note);

public static class TakeAnalyzer
{
    // Ordinal among the file's audio streams (the mic track is written first, the loopback track second).
    public const int AudioStreamMic = 0;
    public const int AudioStreamLoopback = 1;

    /// <summary>Source-reader stream indices by major type; container order is not guaranteed to match write order.</summary>
    public static (List<int> video, List<int> audio) ClassifyStreams(string path)
    {
        var video = new List<int>();
        var audio = new List<int>();
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
        for (int i = 0; i < 16; i++)
        {
            try
            {
                using var nt = reader.GetNativeMediaType((SourceReaderIndex)i, 0);
                Guid major = nt.GetGUID(MediaTypeAttributeKeys.MajorType);
                if (major == MediaTypeGuids.Video) video.Add(i);
                else if (major == MediaTypeGuids.Audio) audio.Add(i);
            }
            catch { break; }
        }
        return (video, audio);
    }

    public static double Dbfs(double amplitude) => amplitude > 1e-10 ? 20.0 * Math.Log10(amplitude) : -200.0;

    // ---------------------------------------------------------------- video
    public static VideoAnalysis DecodeVideo(string path, SourceLayout layout)
    {
        var result = new VideoAnalysis();
        try
        {
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
            using var nv12 = MediaFactory.MFCreateMediaType();
            nv12.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            nv12.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, nv12);

            using var cur = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            MediaFactory.MFGetAttributeSize(cur, MediaTypeAttributeKeys.FrameSize, out uint w, out uint h);
            result.Width = (int)w;
            result.Height = (int)h;
            int stride = (int)w;
            try { stride = Math.Abs((int)cur.GetUInt32(MediaTypeAttributeKeys.DefaultStride)); } catch { }
            if (stride < w) stride = (int)w;

            while (true)
            {
                var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None,
                    out _, out var flags, out long ts);
                if (flags.HasFlag(SourceReaderFlag.Error)) { result.Error = "source reader error flag"; break; }
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
                if (sample == null) continue;
                using (sample)
                {
                    using var buf = sample.ConvertToContiguousBuffer();
                    buf.Lock(out IntPtr p, out _, out _);
                    try { result.Frames.Add(ReadFrame(p, stride, ts / 10_000_000.0, layout)); }
                    finally { buf.Unlock(); }
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.GetType().Name + ": " + ex.Message;
        }
        return result;
    }

    private static double LumaAt(IntPtr p, int stride, int cx, int cy, int radius)
    {
        long sum = 0;
        int n = 0;
        for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                sum += Marshal.ReadByte(p, y * stride + x);
                n++;
            }
        return sum / (double)n;
    }

    private static VideoFrameInfo ReadFrame(IntPtr p, int stride, double ptsSec, SourceLayout l)
    {
        double sx = l.Dpi * l.ScaleX, sy = l.Dpi * l.ScaleY;
        int gray = 0;
        bool ambiguous = false;
        int cy = (int)((CounterWindow.BitY0 + CounterWindow.BitSize / 2) * sy);
        for (int i = 0; i < CounterWindow.Bits; i++)
        {
            int cx = (int)((CounterWindow.BitX0 + i * CounterWindow.BitPitch + CounterWindow.BitSize / 2) * sx);
            double y = LumaAt(p, stride, cx, cy, 3);
            if (y > 126) gray |= 1 << i;
            if (y > 70 && y < 170) ambiguous = true;
        }

        // Flash panel: mean of an 8x8 grid over its inner area.
        double flash = 0;
        for (int gy = 0; gy < 8; gy++)
            for (int gx = 0; gx < 8; gx++)
            {
                double fx = CounterWindow.FlashX + CounterWindow.FlashW * (0.2 + 0.6 * gx / 7.0);
                double fy = CounterWindow.FlashY + CounterWindow.FlashH * (0.2 + 0.6 * gy / 7.0);
                flash += Marshal.ReadByte(p, (int)(fy * sy) * stride + (int)(fx * sx));
            }
        flash /= 64.0;
        return new VideoFrameInfo(ptsSec, CounterWindow.FromGray(gray), flash, ambiguous);
    }

    public static Onset FlashOnset(VideoAnalysis v, int clap, double expectedSec)
    {
        var win = v.Frames.Where(f => f.PtsSec >= expectedSec - 0.5 && f.PtsSec <= expectedSec + 1.0).OrderBy(f => f.PtsSec).ToList();
        if (win.Count == 0) return new Onset(clap, expectedSec, null, "no video frames near the clap");
        var before = win.Where(f => f.PtsSec < expectedSec - 0.05).Select(f => f.FlashLuma).OrderBy(x => x).ToList();
        double black = before.Count > 0 ? before[before.Count / 2] : double.NaN;
        double white = win.Where(f => f.PtsSec >= expectedSec - 0.05).Select(f => f.FlashLuma).DefaultIfEmpty(0).Max();
        if (double.IsNaN(black) || white - black < 80)
            return new Onset(clap, expectedSec, null, $"no white flash found (black {black:F0}, max {white:F0})");
        double thr = (black + white) / 2.0;
        var first = win.FirstOrDefault(f => f.PtsSec >= expectedSec - 0.05 && f.FlashLuma >= thr);
        return first == null
            ? new Onset(clap, expectedSec, null, "threshold never crossed")
            : new Onset(clap, expectedSec, first.PtsSec, $"luma black {black:F0} white {white:F0} thr {thr:F0}");
    }

    // ---------------------------------------------------------------- audio
    public static AudioTrack DecodeAudio(string path, int audioOrdinal)
    {
        var track = new AudioTrack();
        var streams = ClassifyStreams(path);
        if (audioOrdinal >= streams.audio.Count)
        {
            track.Error = $"file has {streams.audio.Count} audio stream(s), ordinal {audioOrdinal} requested";
            return track;
        }
        int streamIndex = streams.audio[audioOrdinal];
        var pieces = new List<(long start, float[] mono)>();
        try
        {
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            var idx = (SourceReaderIndex)streamIndex;
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(idx, true);
            using var pcm = MediaFactory.MFCreateMediaType();
            pcm.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            pcm.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            pcm.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
            pcm.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, AudioTrack.Rate);
            pcm.Set(MediaTypeAttributeKeys.AudioNumChannels, 2);
            pcm.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4);
            pcm.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioTrack.Rate * 4);
            reader.SetCurrentMediaType(idx, pcm);

            long expectedNext = -1;
            bool first = true;
            while (true)
            {
                var sample = reader.ReadSample(idx, SourceReaderControlFlag.None, out _, out var flags, out long ts);
                if (flags.HasFlag(SourceReaderFlag.Error)) { track.Error = "source reader error flag"; break; }
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
                if (sample == null) continue;
                using (sample)
                {
                    using var buf = sample.ConvertToContiguousBuffer();
                    buf.Lock(out IntPtr p, out _, out int len);
                    short[] s16 = new short[len / 2];
                    Marshal.Copy(p, s16, 0, s16.Length);
                    buf.Unlock();

                    float[] mono = new float[s16.Length / 2];
                    for (int i = 0; i < mono.Length; i++) mono[i] = (s16[2 * i] + s16[2 * i + 1]) / 65536.0f;

                    long startSample = (long)Math.Round(ts / 10_000_000.0 * AudioTrack.Rate);
                    if (first) { track.FirstTsSec = ts / 10_000_000.0; first = false; }
                    if (expectedNext >= 0 && Math.Abs(startSample - expectedNext) > 240)
                    {
                        track.Discontinuities++;
                        if (startSample > expectedNext) track.GapSeconds += (startSample - expectedNext) / (double)AudioTrack.Rate;
                    }
                    expectedNext = startSample + mono.Length;
                    pieces.Add((startSample, mono));
                    track.Buffers++;
                }
            }

            if (pieces.Count > 0)
            {
                long end = pieces.Max(x => x.start + x.mono.Length);
                var all = new float[end];
                foreach (var (start, mono) in pieces) Array.Copy(mono, 0, all, start, mono.Length);
                track.Samples = all;
            }
        }
        catch (Exception ex)
        {
            track.Error = ex.GetType().Name + ": " + ex.Message;
        }
        return track;
    }

    /// <summary>Amplitude (peak, full scale = 1.0) of a tone at freq in x[start..start+n), optionally Hann-windowed.</summary>
    public static double ToneAmplitude(float[] x, int start, int n, double freq, int rate, bool hann)
    {
        double w = 2.0 * Math.PI * freq / rate;
        double re = 0, im = 0, wsum = 0;
        for (int i = 0; i < n; i++)
        {
            double win = hann ? 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n - 1)) : 1.0;
            double v = x[start + i] * win;
            re += v * Math.Cos(w * i);
            im -= v * Math.Sin(w * i);
            wsum += win;
        }
        return 2.0 * Math.Sqrt(re * re + im * im) / wsum;
    }

    /// <summary>Per-1 s window (hop 0.5 s) tone amplitudes across the whole track.</summary>
    public static List<double> ToneAmplitudes(float[] x, int rate, double freq)
    {
        var res = new List<double>();
        for (int s = 0; s + rate <= x.Length; s += rate / 2) res.Add(ToneAmplitude(x, s, rate, freq, rate, true));
        return res;
    }

    public static double Median(List<double> v)
    {
        var s = v.OrderBy(a => a).ToList();
        return s.Count == 0 ? double.NaN : s[s.Count / 2];
    }

    public static Onset BeepOnset(AudioTrack a, int clap, double expectedSec)
    {
        const int win = 240, hop = 48;
        int lo = (int)Math.Max(0, (expectedSec - 0.5) * AudioTrack.Rate);
        int hi = (int)Math.Min(a.Samples.Length - win, (expectedSec + 1.0) * AudioTrack.Rate);
        if (hi <= lo) return new Onset(clap, expectedSec, null, "audio track does not cover the clap");
        var amps = new List<(int start, double amp)>();
        for (int s = lo; s <= hi; s += hop) amps.Add((s, ToneAmplitude(a.Samples, s, win, ClapHelper.BeepHz, AudioTrack.Rate, false)));
        double peak = amps.Max(t => t.amp);
        if (peak < 0.1)
            return new Onset(clap, expectedSec, null, $"no 1 kHz beep found (peak amplitude {peak:F3})");
        var hit = amps.First(t => t.amp >= 0.5 * peak);
        // The half-amplitude crossing of a 5 ms sliding window sits at the window centre when half of it holds tone.
        return new Onset(clap, expectedSec, (hit.start + win / 2) / (double)AudioTrack.Rate, $"peak amplitude {peak:F3}");
    }

    // Development aid: prints native stream types and the first buffers of each stream.
    public static void Dump(string path)
    {
        for (int si = 0; si < 4; si++)
        {
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            try
            {
                using var nt = reader.GetNativeMediaType((SourceReaderIndex)si, 0);
                string sub = "?"; try { sub = nt.GetGUID(MediaTypeAttributeKeys.Subtype).ToString(); } catch { }
                string ch = "?", rate = "?", ud = "none";
                try { ch = nt.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels).ToString(); } catch { }
                try { rate = nt.GetUInt32(MediaTypeAttributeKeys.AudioSamplesPerSecond).ToString(); } catch { }
                try { ud = nt.GetBlobSize(MediaTypeAttributeKeys.UserData).ToString(); } catch { }
                Console.WriteLine($"stream {si}: subtype={sub} ch={ch} rate={rate} userdataBytes={ud}");
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection((SourceReaderIndex)si, true);
                for (int n = 0; n < 12; n++)
                {
                    var smp = reader.ReadSample((SourceReaderIndex)si, SourceReaderControlFlag.None, out _, out var fl, out long ts);
                    if (smp == null) { Console.WriteLine($"  null flags={fl}"); if (fl.HasFlag(SourceReaderFlag.EndOfStream)) break; continue; }
                    Console.WriteLine($"  ts={ts / 10000.0:F2}ms dur={smp.SampleDuration / 10000.0:F2}ms bytes={smp.TotalLength}");
                    smp.Dispose();
                }
            }
            catch (Exception ex) { Console.WriteLine($"stream {si}: {ex.GetType().Name} {ex.Message}"); }
        }
    }
}
