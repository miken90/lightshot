using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordingProbe;

/// <summary>
/// Negative control: an ordinary endpoint loopback (all processes). The probe's own 440 Hz tone must show up here,
/// proving it was audible on the endpoint while the process-excluding track did not carry it.
/// </summary>
public sealed class EndpointLoopback : IDisposable
{
    private readonly WasapiLoopbackCapture _capture = new();
    private readonly List<float> _mono = new();
    // Sample index of each DataAvailable chunk and the QPC its first sample was captured at (arrival minus duration).
    private readonly List<(int index, long qpc)> _chunks = new();
    private readonly object _lock = new();
    private readonly int _rate;
    private readonly int _channels;

    public EndpointLoopback()
    {
        _rate = _capture.WaveFormat.SampleRate;
        _channels = _capture.WaveFormat.Channels;
        if (_capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat &&
            !(_capture.WaveFormat is WaveFormatExtensible ext && ext.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT))
            throw new NotSupportedException("endpoint loopback mix format is not float: " + _capture.WaveFormat);
        _capture.DataAvailable += (s, e) =>
        {
            long arrival = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_lock)
            {
                int frames = e.BytesRecorded / (4 * _channels);
                _chunks.Add((_mono.Count, arrival - (long)(frames * (double)System.Diagnostics.Stopwatch.Frequency / _rate)));
                for (int i = 0; i + 4 * _channels <= e.BytesRecorded; i += 4 * _channels)
                    _mono.Add(BitConverter.ToSingle(e.Buffer, i));
            }
        };
    }

    public void Start() => _capture.StartRecording();

    public void Stop() => _capture.StopRecording();

    /// <summary>Median and max amplitude (dBFS) of a tone over 1 s windows.</summary>
    public (double medianDbfs, double maxDbfs, int windows) Level(double freq)
    {
        float[] x;
        lock (_lock) x = _mono.ToArray();
        var amps = new List<double>();
        for (int s = 0; s + _rate <= x.Length; s += _rate / 2) amps.Add(TakeAnalyzer.ToneAmplitude(x, s, _rate, freq, _rate, true));
        if (amps.Count == 0) return (-200, -200, 0);
        return (TakeAnalyzer.Dbfs(TakeAnalyzer.Median(amps)), TakeAnalyzer.Dbfs(amps.Max()), amps.Count);
    }

    /// <summary>
    /// QPC at which the 1 kHz beep scheduled near clapQpc reached the endpoint (half-amplitude crossing of a 5 ms
    /// window). Resolution is limited by the event-driven buffer period (about 10 ms).
    /// </summary>
    public long? BeepOnsetQpc(long clapQpc)
    {
        float[] x; List<(int index, long qpc)> ch;
        lock (_lock) { x = _mono.ToArray(); ch = new List<(int, long)>(_chunks); }
        long freq = System.Diagnostics.Stopwatch.Frequency;
        long QpcOf(int sample)
        {
            int c = ch.FindLastIndex(t => t.index <= sample);
            return c < 0 ? 0 : ch[c].qpc + (long)((sample - ch[c].index) * (double)freq / _rate);
        }
        int win = _rate / 200, hop = _rate / 1000;
        int lo = ch.FindIndex(t => t.qpc >= clapQpc - freq / 2);
        if (lo < 0) return null;
        int start = ch[lo].index;
        int end = Math.Min(x.Length - win, start + (int)(1.5 * _rate));
        var amps = new List<(int s, double a)>();
        for (int s = start; s <= end; s += hop) amps.Add((s, TakeAnalyzer.ToneAmplitude(x, s, win, ClapHelper.BeepHz, _rate, false)));
        if (amps.Count == 0) return null;
        double peak = amps.Max(t => t.a);
        if (peak < 0.1) return null;
        var hit = amps.First(t => t.a >= 0.5 * peak);
        return QpcOf(hit.s + win / 2);
    }

    /// <summary>
    /// Diagnostic only, not used by any criterion: how late the arrival-based stamp of the chunk holding qpc is
    /// against the endpoint's own sample clock. Each chunk's stamp minus its sample index over the rate would be
    /// constant if every chunk were stamped exactly; the earliest such value is the best-stamped chunk, and the excess
    /// over it is the delivery delay folded into the stamp. Also returns the spread of that excess over all chunks.
    /// </summary>
    public (double lateMs, double medianLateMs, double maxLateMs)? StampLateVsSampleClockMs(long qpc)
    {
        List<(int index, long qpc)> ch;
        lock (_lock) ch = new List<(int, long)>(_chunks);
        if (ch.Count < 2) return null;
        double freq = System.Diagnostics.Stopwatch.Frequency;
        var excess = ch.Select(c => (c.qpc - c.index * freq / _rate) * 1000.0 / freq).ToList();
        double floor = excess.Min();
        int i = ch.FindLastIndex(c => c.qpc <= qpc);
        if (i < 0) return null;
        var sorted = excess.Select(e => e - floor).OrderBy(e => e).ToList();
        return (excess[i] - floor, sorted[sorted.Count / 2], sorted[^1]);
    }

    public void Dispose() => _capture.Dispose();
}
