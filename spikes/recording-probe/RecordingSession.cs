using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace RecordingProbe;

public sealed record SessionStats(
    int VideoFramesWritten,
    int DuplicateFrames,
    double ElapsedSeconds,
    double TotalCpuSeconds,
    double CpuPercent,
    int PreStartAudioDiscarded,
    string EncoderTransform,
    List<FrameTrace> VideoTrace,
    AudioTrack LoopbackInput,
    Dictionary<string, (long silenceFrames, long trimmedFrames)> AudioContiguityFix);

/// <summary>One frame handed to the sink writer: acquire QPC, the sample time it was given, and the tapped counter.</summary>
public sealed record FrameTrace(long AcquireQpc, long SampleHns, int Counter, bool Duplicate, long WriteReturnQpc);

/// <summary>
/// The shared record loop: DDA to NV12 to the fragmented MP4 writer plus mic and loopback audio, all stamped on
/// QPC relative to startQpc. durationSec &lt;= 0 records until the process is killed.
/// </summary>
public static class RecordingSession
{
    private sealed record Pending(Vortice.Direct3D11.ID3D11Texture2D Texture, long AcquireQpc, long SampleHns, int Counter, bool Duplicate);

    public static SessionStats Run(
        string outputPath,
        double durationSec,
        long startQpc,
        DdaToNv12 dda,
        WasapiMic mic,
        ProcessLoopback loopback,
        int width,
        int height,
        int fps,
        Action<string>? onLoopStarted = null)
    {
        var micQueue = new ConcurrentQueue<(byte[] data, long ts, long dur)>();
        var loopbackQueue = new ConcurrentQueue<(byte[] data, long ts, long dur)>();
        Action<byte[], long, long> micHandler = (d, t, u) => micQueue.Enqueue((d, t, u));
        Action<byte[], long, long> loopHandler = (d, t, u) => loopbackQueue.Enqueue((d, t, u));
        mic.OnAudioSample += micHandler;
        loopback.OnAudioSample += loopHandler;

        using var writer = new MfFragmentedWriter(outputPath, dda.Device, width, height, fps);
        long startHns = QpcClock.ToHns(startQpc);
        long nominalDurationHns = (long)(10_000_000.0 / fps);
        int written = 0, duplicates = 0, discarded = 0;
        Pending? pending = null;
        // Stage trace: what the sink writer was given, to compare with what the decoded file holds.
        var trace = new List<FrameTrace>(4096);
        var loopPieces = new List<(long start, float[] mono)>();

        // Wait for the shared start instant so every source (window, helper, audio) is on the same timeline.
        while (Stopwatch.GetTimestamp() < startQpc) Thread.Sleep(1);

        var proc = Process.GetCurrentProcess();
        var cpuStart = proc.TotalProcessorTime;
        onLoopStarted?.Invoke(writer.EncoderTransformName);

        void WritePending(Pending f, long durationHns)
        {
            // The ring texture is still intact: the converter has advanced by one slot since it was filled.
            // The file has no per-track start time (no tfdt or edit list): a track's timeline begins at its first
            // sample. The first frame therefore starts at the shared start instant so later frames keep their QPC times.
            long sampleHns = written == 0 ? 0 : f.SampleHns;
            durationHns += f.SampleHns - sampleHns;
            writer.WriteVideoFrame(f.Texture, sampleHns, Math.Max(1, durationHns));
            if (durationSec > 0) trace.Add(new FrameTrace(f.AcquireQpc, f.SampleHns, f.Counter, f.Duplicate, Stopwatch.GetTimestamp()));
            written++;
            if (f.Duplicate) duplicates++;
        }

        // Audio time in the file is the running sample count, so each track is kept contiguous against QPC: a gap
        // (the leading one included) is filled with silence and an overlap is trimmed. The loopback carries the device's
        // QPC position, so 1 ms is the tolerance; the mic is stamped on arrival, whose jitter must not become edits.
        var nextFrame = new Dictionary<int, long>();
        var audioFix = new Dictionary<int, (long silenceFrames, long trimmedFrames)>();
        const int Rate = AudioTrack.Rate, FrameBytes = 4;
        long FrameToHns(long frame) => frame * 10_000_000L / Rate;

        void Drain(ConcurrentQueue<(byte[] data, long ts, long dur)> q, int stream)
        {
            while (q.TryDequeue(out var a))
            {
                long t = a.ts - startHns;
                // Samples captured before the shared start are dropped, never clamped to zero.
                if (t < 0) { discarded++; continue; }
                long expected = nextFrame.GetValueOrDefault(stream);
                long ToleranceFrames = stream == writer.LoopbackStreamIndex ? Rate / 1000 : Rate / 50;
                var fix = audioFix.GetValueOrDefault(stream);
                long start = (long)Math.Round(t * (double)Rate / 10_000_000.0);
                byte[] data = a.data;
                if (start - expected > ToleranceFrames)
                {
                    long gap = start - expected;
                    writer.WriteAudioSample(stream, new byte[gap * FrameBytes], FrameToHns(expected), FrameToHns(gap));
                    fix.silenceFrames += gap;
                    expected = start;
                }
                else if (expected - start > ToleranceFrames)
                {
                    long skip = Math.Min(expected - start, data.Length / FrameBytes);
                    data = data.AsSpan((int)skip * FrameBytes).ToArray();
                    fix.trimmedFrames += skip;
                }
                audioFix[stream] = fix;
                long frames = data.Length / FrameBytes;
                if (frames == 0) continue;
                writer.WriteAudioSample(stream, data, FrameToHns(expected), FrameToHns(frames));
                if (stream == writer.LoopbackStreamIndex && durationSec > 0) loopPieces.Add((FrameToHns(expected), ToMono(data)));
                nextFrame[stream] = expected + frames;
            }
        }

        while (true)
        {
            double elapsed = (Stopwatch.GetTimestamp() - startQpc) / (double)Stopwatch.Frequency;
            if (durationSec > 0 && elapsed >= durationSec) break;

            if (dda.AcquireAndProcessFrame(34, out long frameQpc, out bool dup))
            {
                long t = QpcClock.ToHns(frameQpc) - startHns;
                if (t >= 0)
                {
                    // Every acquired frame is written. It is held until the next one arrives so its duration is the
                    // real gap: the MP4 timeline is built from sample durations, and a fixed 1/fps duration on
                    // irregular frames made the file's presentation times drift from the sample times by hundreds of ms.
                    if (pending is { } p) WritePending(p, t - p.SampleHns);
                    int counter = dda.AcquiredCounters.Count > 0 ? dda.AcquiredCounters[^1] : -1;
                    pending = new Pending(dda.Nv12Texture, frameQpc, t, counter, dup);
                }
            }
            Drain(micQueue, writer.MicStreamIndex);
            Drain(loopbackQueue, writer.LoopbackStreamIndex);
        }

        if (pending is { } last) WritePending(last, nominalDurationHns);

        var cpuEnd = proc.TotalProcessorTime;
        double wall = (Stopwatch.GetTimestamp() - startQpc) / (double)Stopwatch.Frequency;
        double cpuSec = (cpuEnd - cpuStart).TotalSeconds;

        mic.OnAudioSample -= micHandler;
        loopback.OnAudioSample -= loopHandler;
        string encoder = writer.EncoderTransformName;
        writer.FinalizeWriting();

        return new SessionStats(written, duplicates, wall, cpuSec,
            cpuSec / (wall * Environment.ProcessorCount) * 100.0, discarded, encoder, trace, Assemble(loopPieces),
            new Dictionary<string, (long, long)>
            {
                ["mic"] = audioFix.GetValueOrDefault(writer.MicStreamIndex),
                ["loopback"] = audioFix.GetValueOrDefault(writer.LoopbackStreamIndex)
            });
    }

    private static float[] ToMono(byte[] pcm16Stereo)
    {
        var mono = new float[pcm16Stereo.Length / 4];
        for (int i = 0; i < mono.Length; i++)
            mono[i] = (BitConverter.ToInt16(pcm16Stereo, 4 * i) + BitConverter.ToInt16(pcm16Stereo, 4 * i + 2)) / 65536.0f;
        return mono;
    }

    // Places each written PCM packet at its sample time, the same way TakeAnalyzer.DecodeAudio rebuilds a file track.
    private static AudioTrack Assemble(List<(long startHns, float[] mono)> pieces)
    {
        var track = new AudioTrack();
        if (pieces.Count == 0) { track.Error = "no loopback packets written"; return track; }
        var placed = pieces.Select(p => (start: (long)Math.Round(p.startHns / 10_000_000.0 * AudioTrack.Rate), p.mono)).ToList();
        var all = new float[placed.Max(p => p.start + p.mono.Length)];
        foreach (var (start, mono) in placed) Array.Copy(mono, 0, all, start, mono.Length);
        track.Samples = all;
        track.Buffers = pieces.Count;
        track.FirstTsSec = pieces[0].startHns / 10_000_000.0;
        return track;
    }
}
