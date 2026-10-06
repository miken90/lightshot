// Lightshot Windows Port
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Audio;
using Lightshot.TestSupport;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class ProcessLoopbackTests
{
    private sealed class ToneProvider : ISampleProvider
    {
        private readonly double _freq;
        private long _sample;
        public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public ToneProvider(double freq) => _freq = freq;

        public int Read(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i += 2)
            {
                float s = (float)(Math.Sin(2.0 * Math.PI * _freq * (_sample / 48000.0)) * 0.5);
                buffer[i] = s;
                if (i + 1 < buffer.Length) buffer[i + 1] = s;
                _sample++;
            }
            return buffer.Length;
        }

        public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    }

    [Fact]
    [Unit]
    public void InitializesWithTargetPidAndDriftFilter()
    {
        var filter = new AudioClockDriftFilter();
        using var capture = new ProcessLoopbackCapture(12345, filter);

        Assert.Equal(12345u, capture.ExcludedProcessId);
        Assert.Same(filter, capture.DriftFilter);
    }

    [Fact]
    [Media]
    public async Task ExcludesOwnProcessAudio()
    {
        // 1. Verify host has active default audio render endpoint
        MMDevice? defaultRender = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (COMException ex)
        {
            Console.WriteLine($"UNCOVERED: no audio render device on host: {ex.Message}");
            return;
        }

        if (defaultRender == null || defaultRender.State != DeviceState.Active)
        {
            Console.WriteLine("UNCOVERED: no active audio render device on host");
            return;
        }

        // 2. Generate 880 Hz WAV tone for external child process
        string childWav = CreateWavTone(880, 2.0);

        try
        {
            var recordedSamples = new List<float>();
            var samplesLock = new object();

            uint ownPid = (uint)Environment.ProcessId;
            using var capture = new ProcessLoopbackCapture(ownPid);

            capture.OnAudioSample += frame =>
            {
                lock (samplesLock)
                {
                    recordedSamples.AddRange(frame.Samples);
                }
            };

            // 3. Start loopback capture excluding own process tree
            capture.Start();

            // 4. Play 440 Hz tone in-process via WASAPI (associated with ownPid)
#pragma warning disable CS0618
            using var wasapiOut = new WasapiOut(AudioClientShareMode.Shared, 50);
            wasapiOut.Init(new ToneProvider(440.0));
            wasapiOut.Play();
#pragma warning restore CS0618

            // 5. Spawn external child process via WMI playing 880 Hz tone (parent is WmiPrvSE)
            string childScript = $"$p = New-Object System.Media.SoundPlayer '{childWav}'; $p.PlaySync();";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(childScript));
            string cmd = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"$res = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{{ CommandLine = '{cmd}' }}; $proc = Get-Process -Id $res.ProcessId -ErrorAction SilentlyContinue; if ($proc) {{ $proc.WaitForExit() }}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var wmiProc = Process.Start(psi);
            if (wmiProc != null)
            {
                await wmiProc.WaitForExitAsync(TestContext.Current.CancellationToken);
            }

            // 6. Stop playback and capture
            wasapiOut.Stop();
            capture.Stop();

            // 7. Verify frequency spectrum via Goertzel algorithm
            float[] captured;
            lock (samplesLock)
            {
                captured = recordedSamples.ToArray();
            }

            Assert.NotEmpty(captured);

            double power440 = CalculateGoertzelPower(captured, 440.0, 48000.0);
            double power880 = CalculateGoertzelPower(captured, 880.0, 48000.0);

            // Own 440 Hz audio must be excluded: power at 440 Hz must be negligible
            // while 880 Hz from child process must be clearly captured
            Assert.True(power880 > 1e-4, $"Child process 880 Hz was not detected: power={power880:E4}");
            Assert.True(power440 < power880 * 0.05,
                $"Own process audio (440 Hz, power={power440:E4}) was not excluded relative to child audio (880 Hz, power={power880:E4})");
        }
        finally
        {
            TryDeleteFile(childWav);
        }
    }

    private static double CalculateGoertzelPower(IReadOnlyList<float> samples, double targetFreq, double sampleRate, int channels = 2)
    {
        if (samples.Count == 0) return 0.0;
        double omega = 2.0 * Math.PI * targetFreq / sampleRate;
        double coeff = 2.0 * Math.Cos(omega);
        double s0 = 0, s1 = 0, s2 = 0;
        int frameCount = samples.Count / channels;

        for (int i = 0; i < samples.Count; i += channels)
        {
            s0 = samples[i] + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }

        double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
        return power / frameCount;
    }

    private static string CreateWavTone(int freq, double durationSec)
    {
        string path = Path.Combine(Path.GetTempPath(), $"lightshot_tone_{freq}_{Guid.NewGuid():N}.wav");
        int rate = 48000, count = (int)(rate * durationSec);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8); bw.Write(36 + count * 2); bw.Write("WAVEfmt "u8);
        bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(rate);
        bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8); bw.Write(count * 2);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / rate;
            short s = (short)(Math.Sin(2.0 * Math.PI * freq * t) * 0.8 * 32767.0);
            bw.Write(s);
        }
        return path;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
