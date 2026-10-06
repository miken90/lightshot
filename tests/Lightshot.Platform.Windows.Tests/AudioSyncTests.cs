// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Vortice.MediaFoundation;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class AudioSyncTests : IDisposable
{
    private readonly string _tempDir;

    public AudioSyncTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Lightshot_AudioSync_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        try { MediaFactory.MFStartup(); } catch { }
    }

    [Fact]
    [Media]
    public async Task DriftUnder40MsOver60Seconds()
    {
        var displays = DisplayTopology.GetDisplays();
        Assert.NotEmpty(displays);
        var primary = displays[0];

        int fps = 60;
        string outputPath = Path.Combine(_tempDir, "sync_take_60s.mp4");

        var defaults = new RecordingDefaults
        {
            Video = new VideoSettings(Core.VideoCodec.H264, fps, MaxResolution.Original, false),
            RecordMicrophone = true,
            RecordComputerAudio = true,
            SeparateAudioTracks = false,
            CountdownSeconds = 0
        };

        var options = RecordingOptions.Resolve(
            new CaptureRegion.DisplayRegion(primary.DisplayId),
            RecordingOutputKind.Video,
            defaults);

        double[] clapSeconds = [5.0, 15.0, 25.0, 35.0, 45.0, 55.0];
        double totalSeconds = 60.0;

        string probeDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\spikes\recording-probe\bin\Debug\net10.0-windows10.0.22621.0"));
        string helperExe = Path.Combine(probeDir, "recording-probe.exe");
        Assert.True(File.Exists(helperExe), $"Helper probe executable must exist at: {helperExe}");

        long leadTicks = (long)(2.0 * Stopwatch.Frequency);
        long t0 = Stopwatch.GetTimestamp() + leadTicks;
        long[] clapTicks = new long[clapSeconds.Length];
        for (int i = 0; i < clapSeconds.Length; i++)
        {
            clapTicks[i] = t0 + (long)(clapSeconds[i] * Stopwatch.Frequency);
        }

        string clapsCsv = string.Join(",", clapSeconds);
        var helperProcess = StartClapHelperOutsideTree(helperExe, t0, 900, clapsCsv);

        using var flashWindow = new SyncFlashWindow(clapTicks);
        using var service = new WindowsRecordingService();

        Process currentProcess = Process.GetCurrentProcess();
        TimeSpan cpuStart = currentProcess.TotalProcessorTime;
        long wallStart = Stopwatch.GetTimestamp();

        try
        {
            flashWindow.Start();

            while (Stopwatch.GetTimestamp() < t0 - (Stopwatch.Frequency / 10))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            var startError = await service.StartAsync(options, outputPath, ev => { });
            Assert.Null(startError);

            long endTick = t0 + (long)(totalSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < endTick)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            var (finalPath, stopError) = await service.StopAsync();
            Assert.Null(stopError);
            Assert.NotNull(finalPath);
            Assert.True(File.Exists(finalPath));

            TimeSpan cpuEnd = currentProcess.TotalProcessorTime;
            long wallEnd = Stopwatch.GetTimestamp();

            double wallSeconds = (wallEnd - wallStart) / (double)Stopwatch.Frequency;
            double cpuSeconds = (cpuEnd - cpuStart).TotalSeconds;
            double cpuPercent = (cpuSeconds / (wallSeconds * Environment.ProcessorCount)) * 100.0;

            double firstExpected = clapSeconds[0];
            double lastExpected = clapSeconds[^1];

            var (firstVideo, firstAudio) = SyncTakeAnalyzer.MeasureOnsets(finalPath, firstExpected);
            var (lastVideo, lastAudio) = SyncTakeAnalyzer.MeasureOnsets(finalPath, lastExpected);

            Assert.True(firstVideo.HasValue, "Video flash onset at start clap must be detected");
            Assert.True(firstAudio.HasValue, "Audio beep onset at start clap must be detected");
            Assert.True(lastVideo.HasValue, "Video flash onset at end clap must be detected");
            Assert.True(lastAudio.HasValue, "Audio beep onset at end clap must be detected");

            double offsetStartMs = Math.Abs(firstVideo.Value - firstAudio.Value) * 1000.0;
            double offsetEndMs = Math.Abs(lastVideo.Value - lastAudio.Value) * 1000.0;
            double driftMs = Math.Abs(offsetEndMs - offsetStartMs);

            var (frameCount, droppedPercent) = SyncTakeAnalyzer.AnalyzeVideoFrames(finalPath, fps, totalSeconds);

            Console.WriteLine($"[AudioSync] Start offset: {offsetStartMs:F2} ms, End offset: {offsetEndMs:F2} ms, Drift: {driftMs:F2} ms");
            Console.WriteLine($"[AudioSync] Decoded frames: {frameCount}, Dropped: {droppedPercent:F3}%, CPU: {cpuPercent:F2}%");

            Assert.True(driftMs < 40.0, $"A/V drift over 60s must be < 40 ms. Measured: {driftMs:F2} ms (Start offset: {offsetStartMs:F2} ms, End offset: {offsetEndMs:F2} ms)");
            Assert.True(droppedPercent < 1.0, $"Dropped frames must be < 1.0%. Measured: {droppedPercent:F3}% ({frameCount} frames)");

            if (cpuPercent > 0.0)
            {
                Assert.True(cpuPercent < 15.0, $"CPU utilization must be < 15%. Measured: {cpuPercent:F2}%");
            }
        }
        finally
        {
            try { if (!helperProcess.HasExited) helperProcess.Kill(true); } catch { }
        }
    }

    private static Process StartClapHelperOutsideTree(string exe, long t0, int totalSec, string clapsCsv)
    {
        string commandLine = $"\"{exe}\" --clap-helper {t0} {totalSec} {clapsCsv}";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create " +
                        $"-Arguments @{{CommandLine='{commandLine.Replace("\"", "\\\"")}'}}; Write-Output ('PID=' + $r.ProcessId + ' RC=' + $r.ReturnValue)\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var launcher = Process.Start(psi)!;
        string output = launcher.StandardOutput.ReadToEnd();
        launcher.WaitForExit();
        var m = System.Text.RegularExpressions.Regex.Match(output, @"PID=(\d+) RC=(\d+)");
        if (!m.Success || m.Groups[2].Value != "0") throw new InvalidOperationException("Clap helper launch failed: " + output.Trim());
        return Process.GetProcessById(int.Parse(m.Groups[1].Value));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
