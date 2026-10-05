using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecordingProbe;

/// <summary>
/// Separate process (not in the probe's process tree, so the process-excluding loopback records it) that plays a
/// continuous 660 Hz control tone plus a 1 kHz beep at each clap QPC time. QPC is system-wide, so the probe and the
/// helper share the same clock.
/// </summary>
public static class ClapHelper
{
    public const int ControlToneHz = 660;
    public const float ControlToneAmplitude = 0.1f;   // nominal -20 dBFS
    public const int BeepHz = 1000;
    public const float BeepAmplitude = 0.5f;           // nominal -6 dBFS
    public const int BeepMs = 100;

    /// <summary>args: --clap-helper t0Qpc totalSeconds clapSecondsCsv</summary>
    public static int Run(string[] args)
    {
        long t0 = long.Parse(args[1], CultureInfo.InvariantCulture);
        double totalSec = double.Parse(args[2], CultureInfo.InvariantCulture);
        long[] claps = args[3].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => t0 + QpcClock.FromSeconds(double.Parse(c, CultureInfo.InvariantCulture))).ToArray();

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var player = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
        player.Init(new ClapProvider(claps));
        player.Play();
        Console.WriteLine($"clap helper pid {Environment.ProcessId} playing on {device.FriendlyName}");

        long end = t0 + QpcClock.FromSeconds(totalSec);
        while (Stopwatch.GetTimestamp() < end) Thread.Sleep(100);
        player.Stop();
        return 0;
    }

    private sealed class ClapProvider : IWaveProvider
    {
        private readonly long[] _claps;
        private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private double _controlPhase;
        private double _beepPhase;
        private readonly int _beepSamples = 48000 * BeepMs / 1000;
        private readonly int _rampSamples = 48; // 1 ms
        private int _beepPos = -1;
        private int _nextClap;

        public ClapProvider(long[] claps) { _claps = claps; }

        public WaveFormat WaveFormat => _format;

        public int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public int Read(Span<byte> buffer)
        {
            int frames = buffer.Length / 8;
            long readQpc = Stopwatch.GetTimestamp();
            for (int i = 0; i < frames; i++)
            {
                // Time at which this frame is written; the beep starts at the first frame at or after the clap time.
                long frameQpc = readQpc + (long)(i * (double)Stopwatch.Frequency / 48000.0);
                if (_beepPos < 0 && _nextClap < _claps.Length && frameQpc >= _claps[_nextClap])
                {
                    _beepPos = 0;
                    _nextClap++;
                }

                float v = (float)(Math.Sin(_controlPhase) * ControlToneAmplitude);
                _controlPhase += 2.0 * Math.PI * ControlToneHz / 48000.0;
                if (_controlPhase > 2.0 * Math.PI) _controlPhase -= 2.0 * Math.PI;

                if (_beepPos >= 0)
                {
                    double env = 1.0;
                    if (_beepPos < _rampSamples) env = _beepPos / (double)_rampSamples;
                    else if (_beepPos > _beepSamples - _rampSamples) env = (_beepSamples - _beepPos) / (double)_rampSamples;
                    v += (float)(Math.Sin(_beepPhase) * BeepAmplitude * env);
                    _beepPhase += 2.0 * Math.PI * BeepHz / 48000.0;
                    if (_beepPhase > 2.0 * Math.PI) _beepPhase -= 2.0 * Math.PI;
                    if (++_beepPos >= _beepSamples) { _beepPos = -1; _beepPhase = 0; }
                }

                System.Runtime.InteropServices.MemoryMarshal.Write(buffer.Slice(i * 8, 4), in v);
                System.Runtime.InteropServices.MemoryMarshal.Write(buffer.Slice(i * 8 + 4, 4), in v);
            }
            return frames * 8;
        }
    }
}
