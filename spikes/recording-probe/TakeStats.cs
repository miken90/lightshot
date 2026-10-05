using System;
using System.Collections.Generic;
using System.Linq;

namespace RecordingProbe;

public sealed record VideoStats(
    int Frames,
    double FirstPtsSec,
    double LastPtsSec,
    double AvgFps,
    double MaxPtsGapMs,
    int CounterMin,
    int CounterMax,
    int CounterRange,
    int UniqueCounters,
    int MissingCounters,
    double MissingPercent,
    int MissingRenderedCounters,
    int RenderedCountersInRange,
    int DuplicateCounterFrames,
    int CounterRegressions,
    int AmbiguousFrames,
    double EarlyCounterMinusPtsMs,
    double LateCounterMinusPtsMs);

public sealed record AudioStats(
    bool Usable,
    string? Error,
    int Buffers,
    double FirstTsSec,
    double EndSec,
    int Discontinuities,
    double GapSeconds,
    double RmsDbfs,
    double ControlToneDbfs,
    double ProbeToneDbfs);

public static class TakeStats
{
    public static VideoStats Video(VideoAnalysis v, HashSet<int>? renderedCounters)
    {
        var frames = v.Frames.OrderBy(f => f.PtsSec).ToList();
        if (frames.Count == 0)
            return new VideoStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 0, 0, 0, 0, 0);

        var good = frames.Where(f => !f.Ambiguous).ToList();
        var seen = new HashSet<int>(good.Select(f => f.Counter));
        int min = seen.Count > 0 ? seen.Min() : 0, max = seen.Count > 0 ? seen.Max() : 0;
        int range = seen.Count > 0 ? max - min + 1 : 0;
        int missing = range - seen.Count;
        int renderedInRange = 0, renderedMissing = 0;
        if (renderedCounters != null)
            foreach (int c in renderedCounters.Where(c => c >= min && c <= max))
            {
                renderedInRange++;
                if (!seen.Contains(c)) renderedMissing++;
            }

        double maxGap = 0;
        int dup = 0, regress = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            maxGap = Math.Max(maxGap, (frames[i].PtsSec - frames[i - 1].PtsSec) * 1000.0);
            if (frames[i].Counter == frames[i - 1].Counter) dup++;
            if (frames[i].Counter < frames[i - 1].Counter) regress++;
        }

        double Offset(IEnumerable<VideoFrameInfo> fs) =>
            fs.Select(f => (f.Counter / 60.0 - f.PtsSec) * 1000.0).DefaultIfEmpty(double.NaN).Average();
        double first = frames[0].PtsSec, last = frames[^1].PtsSec;
        return new VideoStats(
            frames.Count, first, last, last > first ? (frames.Count - 1) / (last - first) : 0, maxGap,
            min, max, range, seen.Count, missing, range > 0 ? missing * 100.0 / range : 100,
            renderedMissing, renderedInRange, dup, regress, frames.Count - good.Count,
            Offset(good.Take(120)), Offset(good.Skip(Math.Max(0, good.Count - 120))));
    }

    public static AudioStats Audio(AudioTrack a)
    {
        if (!a.Usable)
            return new AudioStats(false, a.Error ?? "no samples decoded", a.Buffers, a.FirstTsSec, a.EndSec, a.Discontinuities, a.GapSeconds, -200, -200, -200);

        double sumSq = 0;
        foreach (float s in a.Samples) sumSq += (double)s * s;
        double rms = Math.Sqrt(sumSq / a.Samples.Length);
        double control = TakeAnalyzer.Median(TakeAnalyzer.ToneAmplitudes(a.Samples, AudioTrack.Rate, ClapHelper.ControlToneHz));
        var probe = TakeAnalyzer.ToneAmplitudes(a.Samples, AudioTrack.Rate, 440);
        return new AudioStats(true, null, a.Buffers, a.FirstTsSec, a.EndSec, a.Discontinuities, a.GapSeconds,
            TakeAnalyzer.Dbfs(rms), TakeAnalyzer.Dbfs(control), TakeAnalyzer.Dbfs(probe.Count > 0 ? probe.Max() : 0));
    }
}
