using System.Diagnostics;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// High-resolution Query Performance Counter (QPC) conversions for A/V timing.
/// Media Foundation and WASAPI count in 100 ns units (HNS).
/// </summary>
public static class QpcClock
{
    public static long Frequency => Stopwatch.Frequency;

    public static long NowTicks => Stopwatch.GetTimestamp();

    public static long ToHns(long qpcTicks) => (long)(qpcTicks * 10_000_000.0 / Stopwatch.Frequency);

    public static long FromSeconds(double seconds) => (long)(seconds * Stopwatch.Frequency);

    public static double ToSeconds(long qpcTicks) => (double)qpcTicks / Stopwatch.Frequency;

    public static double ToSourceSeconds(long currentQpcTicks, long startQpcTicks) =>
        (double)(currentQpcTicks - startQpcTicks) / Stopwatch.Frequency;
}
