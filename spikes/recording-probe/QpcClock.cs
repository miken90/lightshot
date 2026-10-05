using System.Diagnostics;

namespace RecordingProbe;

/// <summary>QPC tick conversions; WASAPI and Media Foundation both count in 100 ns units.</summary>
public static class QpcClock
{
    public static long ToHns(long qpcTicks) => (long)(qpcTicks * 10_000_000.0 / Stopwatch.Frequency);

    public static long FromSeconds(double seconds) => (long)(seconds * Stopwatch.Frequency);
}
