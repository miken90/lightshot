using System;
using System.Collections.Generic;
using System.Linq;

namespace RecordingProbe;

/// <summary>
/// Per-stage timing: compares what the sink writer was handed (sample time, tapped counter, loopback PCM) with what
/// the decoded file holds, so a lag can be placed in capture, encode/mux or decode instead of guessed.
/// </summary>
public static class StageTiming
{
    private static double Pct(List<double> sorted, double p) =>
        sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

    private static object Dist(IEnumerable<double> values)
    {
        var s = values.OrderBy(x => x).ToList();
        return new
        {
            count = s.Count,
            min = Math.Round(s.DefaultIfEmpty(double.NaN).First(), 2),
            p05 = Math.Round(Pct(s, 0.05), 2),
            median = Math.Round(Pct(s, 0.5), 2),
            p95 = Math.Round(Pct(s, 0.95), 2),
            max = Math.Round(s.DefaultIfEmpty(double.NaN).Last(), 2)
        };
    }

    /// <summary>File presentation times against the input sample times, by order and by counter.</summary>
    public static object Video(VideoAnalysis decoded, List<FrameTrace> trace)
    {
        var frames = decoded.Frames.OrderBy(f => f.PtsSec).ToList();
        int n = Math.Min(frames.Count, trace.Count);
        var byOrderMs = new List<double>(n);
        for (int i = 0; i < n; i++) byOrderMs.Add((frames[i].PtsSec - trace[i].SampleHns / 1e7) * 1000.0);

        // First written frame for each tapped counter; duplicates re-send the previous content and are skipped.
        var firstWritten = new Dictionary<int, int>();
        for (int j = 0; j < trace.Count; j++)
            if (!trace[j].Duplicate && trace[j].Counter >= 0 && !firstWritten.ContainsKey(trace[j].Counter)) firstWritten[trace[j].Counter] = j;
        var seen = new HashSet<int>();
        var byCounterMs = new List<double>();
        var byCounterFrames = new List<double>();
        for (int i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            if (f.Ambiguous || !seen.Add(f.Counter) || !firstWritten.TryGetValue(f.Counter, out int j)) continue;
            byCounterMs.Add((f.PtsSec - trace[j].SampleHns / 1e7) * 1000.0);
            byCounterFrames.Add(i - j);
        }

        var writeMs = trace.Select(t => (t.WriteReturnQpc - t.AcquireQpc) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        return new
        {
            framesHanded = trace.Count,
            framesDecoded = frames.Count,
            filePtsMinusInputSampleTimeByOrderMs = Dist(byOrderMs),
            filePtsMinusInputSampleTimeSameCounterMs = Dist(byCounterMs),
            decodedIndexMinusInputIndexSameCounter = Dist(byCounterFrames),
            acquireToWriteSampleReturnMs = Dist(writeMs)
        };
    }

    /// <summary>Sample time the sink writer was given for the first written frame acquired at or after flashQpc.</summary>
    public static double? FlashInputSec(List<FrameTrace> trace, long flashQpc)
    {
        var t = trace.FirstOrDefault(x => x.AcquireQpc >= flashQpc);
        return t == null ? null : t.SampleHns / 1e7;
    }
}
