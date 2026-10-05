using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace RecordingProbe;

public sealed record SessionStats(
    int VideoFramesWritten,
    int DuplicateFrames,
    int ThrottledFrames,
    double ElapsedSeconds,
    double TotalCpuSeconds,
    double CpuPercent,
    int PreStartAudioDiscarded,
    string EncoderTransform);

/// <summary>
/// The shared record loop: DDA to NV12 to the fragmented MP4 writer plus mic and loopback audio, all stamped on
/// QPC relative to startQpc. durationSec &lt;= 0 records until the process is killed.
/// </summary>
public static class RecordingSession
{
    private const double FrameIntervalSec = 1.0 / 60.0;

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
        long durationHns = (long)(10_000_000.0 / fps);
        int written = 0, duplicates = 0, throttled = 0, discarded = 0;
        long lastWrittenQpc = 0;
        long minGap = QpcClock.FromSeconds(FrameIntervalSec * 0.9);

        // Wait for the shared start instant so every source (window, helper, audio) is on the same timeline.
        while (Stopwatch.GetTimestamp() < startQpc) Thread.Sleep(1);

        var proc = Process.GetCurrentProcess();
        var cpuStart = proc.TotalProcessorTime;
        onLoopStarted?.Invoke(writer.EncoderTransformName);

        void Drain(ConcurrentQueue<(byte[] data, long ts, long dur)> q, int stream)
        {
            while (q.TryDequeue(out var a))
            {
                long t = a.ts - startHns;
                // Samples captured before the shared start are dropped, never clamped to zero.
                if (t < 0) { discarded++; continue; }
                writer.WriteAudioSample(stream, a.data, t, a.dur);
            }
        }

        while (true)
        {
            double elapsed = (Stopwatch.GetTimestamp() - startQpc) / (double)Stopwatch.Frequency;
            if (durationSec > 0 && elapsed >= durationSec) break;

            if (dda.AcquireAndProcessFrame(34, out long frameQpc, out bool dup))
            {
                if (!dup && lastWrittenQpc != 0 && frameQpc - lastWrittenQpc < minGap)
                {
                    throttled++; // display faster than the target fps: surplus frame not encoded
                }
                else
                {
                    long t = QpcClock.ToHns(frameQpc) - startHns;
                    if (t >= 0)
                    {
                        writer.WriteVideoFrame(dda.Nv12Texture, t, durationHns);
                        lastWrittenQpc = frameQpc;
                        written++;
                        if (dup) duplicates++;
                    }
                }
            }
            Drain(micQueue, writer.MicStreamIndex);
            Drain(loopbackQueue, writer.LoopbackStreamIndex);
        }

        var cpuEnd = proc.TotalProcessorTime;
        double wall = (Stopwatch.GetTimestamp() - startQpc) / (double)Stopwatch.Frequency;
        double cpuSec = (cpuEnd - cpuStart).TotalSeconds;

        mic.OnAudioSample -= micHandler;
        loopback.OnAudioSample -= loopHandler;
        string encoder = writer.EncoderTransformName;
        writer.FinalizeWriting();

        return new SessionStats(written, duplicates, throttled, wall, cpuSec,
            cpuSec / (wall * Environment.ProcessorCount) * 100.0, discarded, encoder);
    }
}
