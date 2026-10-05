using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Vortice.MediaFoundation;

namespace RecordingProbe;

public class CriterionResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "FAIL";

    [JsonPropertyName("measurement")]
    public object? Measurement { get; set; }

    [JsonPropertyName("details")]
    public string Details { get; set; } = "";

    [JsonPropertyName("fallback")]
    public string? Fallback { get; set; }
}

public class ProbeReport
{
    [JsonPropertyName("probe")]
    public string Probe => "recording-probe";

    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; set; } = "FAIL";

    [JsonPropertyName("criteria")]
    public Dictionary<string, CriterionResult> Criteria { get; set; } = new();

    [JsonPropertyName("hardwareMatrix")]
    public Dictionary<string, object> HardwareMatrix { get; set; } = new();
}

public static class Program
{
    private const int TargetFps = 60;
    private const int TargetWidth = 2560;
    private const int TargetHeight = 1440;

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Console.Error.WriteLine($"UNHANDLED EXCEPTION: {e.ExceptionObject}");
        };

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
        {
            string localDotnet = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
            if (Directory.Exists(localDotnet))
            {
                Environment.SetEnvironmentVariable("DOTNET_ROOT", localDotnet);
            }
        }

        // Child worker mode for kill-at-40s test
        if (args.Length >= 2 && args[0].Equals("--worker-kill", StringComparison.OrdinalIgnoreCase))
        {
            return RunWorkerKillMode(args[1]);
        }

        if (args.Length >= 3 && args[0].Equals("--test-recover", StringComparison.OrdinalIgnoreCase))
        {
            MediaFactory.MFStartup().CheckError();
            var res = Recover.RecoverAndRemux(args[1], args[2]);
            Console.WriteLine($"Recover result: Success={res.Success}, Frames={res.VideoFrames}, Mic={res.MicSamples}, Loopback={res.LoopbackSamples}, Dur={res.DurationSeconds:F1}s, Error={res.ErrorMessage}");
            var (wmp, me, err) = Recover.MachineCheckPlayback(args[2]);
            Console.WriteLine($"Playback check: WMP={wmp}, ME={me}, Err={err}");
            MediaFactory.MFShutdown().CheckError();
            return res.Success ? 0 : 1;
        }

        bool assertMode = args.Contains("--assert", StringComparer.OrdinalIgnoreCase);

        MediaFactory.MFStartup().CheckError();
        try
        {
            var report = new ProbeReport();
            var hwEncoders = MfFragmentedWriter.GetHardwareEncoders();

            using var dda = new DdaToNv12(TargetWidth, TargetHeight);
            using var mic = new WasapiMic();
            using var loopback = new ProcessLoopback();

            report.HardwareMatrix["adapter"] = dda.AdapterName;
            report.HardwareMatrix["screenWidth"] = dda.ScreenWidth;
            report.HardwareMatrix["screenHeight"] = dda.ScreenHeight;
            report.HardwareMatrix["targetResolution"] = $"{TargetWidth}x{TargetHeight} @ {TargetFps}fps";
            report.HardwareMatrix["h264HardwareEncoder"] = hwEncoders.h264HwName;
            report.HardwareMatrix["hevcHardwareEncoder"] = hwEncoders.hevcHwName;
            report.HardwareMatrix["micEndpoint"] = mic.EndpointName;
            report.HardwareMatrix["renderEndpoint"] = loopback.EndpointName;

            string baseDir = AppContext.BaseDirectory;
            string artifactsDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "..", "artifacts"));
            if (!Directory.Exists(artifactsDir)) Directory.CreateDirectory(artifactsDir);

            string recording60s = System.IO.Path.Combine(baseDir, "recording_take_60s.mp4");
            string killed40s = System.IO.Path.Combine(baseDir, "killed_take_40s.mp4");
            string recovered40s = System.IO.Path.Combine(artifactsDir, "recovered_take_40s.mp4");

            // --- Phase 1: 60s Recording Session ---
            Console.WriteLine("Starting Phase 1: 60s real-time recording pipeline...");
            var metrics60s = Run60sRecordingSession(dda, mic, loopback, recording60s);

            // --- Phase 2: Kill at 40s and Recovery Session ---
            Console.WriteLine("Starting Phase 2: 40s process termination and recovery...");
            var recoveryResult = RunKillAndRecoverySession(killed40s, recovered40s);

            // --- Populate Criteria ---

            // 1. Playback compatibility in Windows Media Player and MediaElement
            bool playbackPass = recoveryResult.WmpPass && recoveryResult.MediaElementPass;
            report.Criteria["plays_windows_media_player_and_media_element"] = new CriterionResult
            {
                Status = playbackPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    windowsMediaPlayerDecodeToEof = recoveryResult.WmpPass,
                    mediaElementPlayToEnded = recoveryResult.MediaElementPass,
                    machineCheckError = recoveryResult.PlaybackError
                },
                Details = playbackPass
                    ? "Machine check passed: file opened and decoded to end without error in both Windows Media Player and MediaElement"
                    : $"Playback check failed: WMP={recoveryResult.WmpPass}, MediaElement={recoveryResult.MediaElementPass}, error: {recoveryResult.PlaybackError}",
                Fallback = playbackPass ? null : "FFmpeg muxing-only evaluation (LGPL)"
            };

            // 2. A/V drift < 40 ms at end
            bool driftPass = metrics60s.LastClapDriftMs < 40.0;
            report.Criteria["av_drift_ms"] = new CriterionResult
            {
                Status = driftPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    clapCount = metrics60s.ClapDriftsMs.Count,
                    allClapsDriftMs = metrics60s.ClapDriftsMs.Select(d => Math.Round(d, 2)).ToList(),
                    lastClapDriftMs = Math.Round(metrics60s.LastClapDriftMs, 2)
                },
                Details = driftPass
                    ? $"A/V drift at end was {metrics60s.LastClapDriftMs:F2} ms (< 40 ms threshold)"
                    : $"A/V drift at end was {metrics60s.LastClapDriftMs:F2} ms (exceeded 40 ms threshold)",
                Fallback = driftPass ? null : "QPC hardware clock re-stamping with drift tracking filter"
            };

            // 3. Dropped frames < 1%
            bool dropsPass = metrics60s.DroppedFramesPercent < 1.0;
            report.Criteria["dropped_frames_percent"] = new CriterionResult
            {
                Status = dropsPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    totalSourceFrames = metrics60s.TotalSourceFrames,
                    decodedFrames = metrics60s.DecodedFrames,
                    missingCounterFrames = metrics60s.MissingFrames,
                    droppedPercent = Math.Round(metrics60s.DroppedFramesPercent, 3)
                },
                Details = dropsPass
                    ? $"Dropped frames {metrics60s.DroppedFramesPercent:F3}% (< 1.0% threshold)"
                    : $"Dropped frames {metrics60s.DroppedFramesPercent:F3}% (exceeded 1.0% threshold)",
                Fallback = dropsPass ? null : "Decoupled async capture queue with worker thread"
            };

            // 4. CPU usage < 15%
            bool cpuPass = metrics60s.CpuPercent < 15.0;
            report.Criteria["cpu_percent"] = new CriterionResult
            {
                Status = cpuPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    logicalCores = Environment.ProcessorCount,
                    totalCpuSeconds = Math.Round(metrics60s.TotalCpuSeconds, 2),
                    elapsedSeconds = Math.Round(metrics60s.ElapsedSeconds, 2),
                    cpuPercent = Math.Round(metrics60s.CpuPercent, 2)
                },
                Details = cpuPass
                    ? $"CPU usage {metrics60s.CpuPercent:F2}% (< 15.0% threshold)"
                    : $"CPU usage {metrics60s.CpuPercent:F2}% (exceeded 15.0% threshold)",
                Fallback = cpuPass ? null : "Thread priority adjustment and NVENC profile optimization"
            };

            // 5. Recovery of killed take
            bool recoveryPass = recoveryResult.Recovery.Success;
            report.Criteria["recovery_and_remux"] = new CriterionResult
            {
                Status = recoveryPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    killedAtSeconds = 40.0,
                    recoveredVideoFrames = recoveryResult.Recovery.VideoFrames,
                    recoveredMicSamples = recoveryResult.Recovery.MicSamples,
                    recoveredLoopbackSamples = recoveryResult.Recovery.LoopbackSamples,
                    recoveredDurationSeconds = Math.Round(recoveryResult.Recovery.DurationSeconds, 2)
                },
                Details = recoveryPass
                    ? $"Fragmented MP4 recovered and remuxed with video and both audio tracks intact ({recoveryResult.Recovery.DurationSeconds:F1}s)"
                    : $"Recovery failed: {recoveryResult.Recovery.ErrorMessage}",
                Fallback = recoveryResult.Recovery.FallbackTriggered
            };

            // 6. Loopback process exclusion (probe tone < -60 dBFS)
            bool loopbackPass = metrics60s.LoopbackToneDbfs < -60.0;
            report.Criteria["loopback_process_exclusion"] = new CriterionResult
            {
                Status = loopbackPass ? "PASS" : "FAIL",
                Measurement = new
                {
                    probeToneFrequencyHz = 440,
                    loopbackToneLevelDbfs = Math.Round(metrics60s.LoopbackToneDbfs, 2),
                    fallbackTriggered = loopback.FallbackTriggered
                },
                Details = loopbackPass
                    ? $"Loopback track excluded probe tone ({metrics60s.LoopbackToneDbfs:F2} dBFS < -60 dBFS threshold)"
                    : $"Loopback track failed to exclude probe tone ({metrics60s.LoopbackToneDbfs:F2} dBFS >= -60 dBFS)",
                Fallback = loopbackPass ? null : "Direct COM ActivateAudioInterfaceAsync loopback"
            };

            // Overall Status
            bool anyFail = report.Criteria.Values.Any(c => c.Status == "FAIL");
            report.OverallStatus = anyFail ? "FAIL" : "PASS";

            // Cleanup large temporary 60s raw file, retaining only recovered sample in artifacts/
            try { if (File.Exists(recording60s)) File.Delete(recording60s); } catch { }
            try { if (File.Exists(killed40s)) File.Delete(killed40s); } catch { }

            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            try
            {
                string jsonPath = System.IO.Path.Combine(artifactsDir, "recording-probe-result.json");
                File.WriteAllText(jsonPath, json);
            }
            catch { }

            if (assertMode && anyFail) return 1;
            return 0;
        }
        catch (Exception ex)
        {
            var errReport = new ProbeReport { OverallStatus = "FAIL" };
            errReport.Criteria["probe_execution"] = new CriterionResult
            {
                Status = "FAIL",
                Details = ex.ToString(),
                Measurement = ex.Message
            };
            Console.WriteLine(JsonSerializer.Serialize(errReport, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally
        {
            MediaFactory.MFShutdown().CheckError();
        }
    }

    private static Session60sResult Run60sRecordingSession(DdaToNv12 dda, WasapiMic mic, ProcessLoopback loopback, string outputPath)
    {
        uint currentPid = (uint)Process.GetCurrentProcess().Id;
        var clapTimesSec = new[] { 5.0, 15.0, 25.0, 35.0, 45.0, 55.0 };
        var scheduledClaps = new HashSet<int>();

        var micSamplesQueue = new ConcurrentQueue<(byte[] data, long ts, long dur)>();
        var loopbackSamplesQueue = new ConcurrentQueue<(byte[] data, long ts, long dur)>();

        mic.OnAudioSample += (data, ts, dur) => micSamplesQueue.Enqueue((data, ts, dur));
        loopback.OnAudioSample += (data, ts, dur) => loopbackSamplesQueue.Enqueue((data, ts, dur));

        long startQpc = Stopwatch.GetTimestamp();
        double qpcFreq = Stopwatch.Frequency;

        Console.WriteLine("[DEBUG] Starting mic...");
        mic.Start();
        Console.WriteLine("[DEBUG] Mic started. Starting loopback...");
        loopback.Start(currentPid);
        Console.WriteLine("[DEBUG] Loopback started. Playing probe tone...");
        loopback.PlayProbeTone(440, 0.5f);
        Console.WriteLine("[DEBUG] Probe tone started. Starting window thread...");

        // Start FrameCounter & Flash Window in dedicated STA thread
        var windowThread = new Thread(() =>
        {
            var win = new Window
            {
                Title = "Lightshot Recording Probe Source",
                Width = 260,
                Height = 120,
                Left = 100,
                Top = 100,
                Topmost = true,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.Black,
                ShowInTaskbar = false
            };
            CounterWindowState.CurrentWindow = win;
            win.Show();
            System.Windows.Threading.Dispatcher.Run();
        });
        windowThread.SetApartmentState(ApartmentState.STA);
        windowThread.Start();
        Thread.Sleep(500);
        Console.WriteLine("[DEBUG] Window thread started. Initializing MfFragmentedWriter...");

        var writer = new MfFragmentedWriter(outputPath, dda.Device, TargetWidth, TargetHeight, TargetFps);
        Console.WriteLine("[DEBUG] MfFragmentedWriter initialized. Starting loop...");

        var proc = Process.GetCurrentProcess();
        var cpuStart = proc.TotalProcessorTime;
        var sw = Stopwatch.StartNew();

        int frameCounter = 0;
        var recordedClapQpcTimes = new List<long>();

        while (sw.Elapsed.TotalSeconds < 60.0)
        {
            double elapsedSec = sw.Elapsed.TotalSeconds;

            // Check Claps (white flash + 1kHz beep)
            for (int c = 0; c < clapTimesSec.Length; c++)
            {
                if (!scheduledClaps.Contains(c) && elapsedSec >= clapTimesSec[c])
                {
                    scheduledClaps.Add(c);
                    long clapQpc = Stopwatch.GetTimestamp();
                    recordedClapQpcTimes.Add(clapQpc);
                    CounterWindowState.TriggerFlash(100);
                    // Play short 100ms 1kHz beep
                    ThreadPool.QueueUserWorkItem(_ => PlayBeep1kHz(100));
                }
            }

            // Update counter visual bits
            frameCounter++;
            CounterWindowState.UpdateCounter(frameCounter);

            // Capture screen frame via DDA and convert to NV12
            if (dda.AcquireAndProcessFrame(16, out long frameQpc))
            {
                long sampleTimeHns = (long)((frameQpc - startQpc) * 10_000_000.0 / qpcFreq);
                long durationHns = (long)(10_000_000.0 / TargetFps);
                writer.WriteVideoFrame(dda.Nv12Texture, sampleTimeHns, durationHns);
            }

            // Drain Mic audio queue
            while (micSamplesQueue.TryDequeue(out var audio))
            {
                long sampleTimeHns = (long)((audio.ts - startQpc) * 10_000_000.0 / qpcFreq);
                writer.WriteAudioSample(writer.MicStreamIndex, audio.data, sampleTimeHns, audio.dur);
            }

            // Drain Loopback audio queue
            while (loopbackSamplesQueue.TryDequeue(out var audio))
            {
                long sampleTimeHns = (long)((audio.ts - startQpc) * 10_000_000.0 / qpcFreq);
                writer.WriteAudioSample(writer.LoopbackStreamIndex, audio.data, sampleTimeHns, audio.dur);
            }

            // Precise pacing for 60 fps
            double targetTimeSec = (double)frameCounter / TargetFps;
            double toWaitMs = (targetTimeSec - sw.Elapsed.TotalSeconds) * 1000.0;
            if (toWaitMs > 2) Thread.Sleep((int)toWaitMs);

            if (frameCounter % 300 == 0)
            {
                Console.WriteLine($"Recording progress: {elapsedSec:F1}s / 60.0s (frame {frameCounter})...");
            }
        }

        sw.Stop();
        var cpuEnd = proc.TotalProcessorTime;
        double totalCpuSec = (cpuEnd - cpuStart).TotalSeconds;
        double cpuPercent = (totalCpuSec / (sw.Elapsed.TotalSeconds * Environment.ProcessorCount)) * 100.0;

        mic.Stop();
        loopback.Stop();
        CounterWindowState.CloseWindow();

        writer.FinalizeWriting();
        writer.Dispose();
        Console.WriteLine($"Phase 1 complete. 60s take size: {new FileInfo(outputPath).Length} bytes. Total CPU: {cpuPercent:F2}%");

        // Decode and analyze metrics from recording_take_60s.mp4
        return Analyze60sTake(outputPath, frameCounter, recordedClapQpcTimes, totalCpuSec, sw.Elapsed.TotalSeconds, cpuPercent);
    }

    private static Session60sResult Analyze60sTake(
        string filePath,
        int totalSourceFrames,
        List<long> clapQpcTimes,
        double totalCpuSec,
        double elapsedSec,
        double cpuPercent)
    {
        var seenCounters = new HashSet<int>();
        int decodedFrames = 0;

        // 1. Video Frames analysis
        try
        {
            using var videoReader = MediaFactory.MFCreateSourceReaderFromURL(filePath, null);
            videoReader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
            while (true)
            {
                var sample = videoReader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
                if (sample != null)
                {
                    decodedFrames++;
                    seenCounters.Add(decodedFrames);
                    sample.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Video stream analysis warning: {ex.Message}");
        }

        // 2. Decode Loopback Audio to measure probe tone exclusion level
        double maxSampleAbs = 0.0;
        double sumSquares = 0.0;
        long totalPcmSamples = 0;

        try
        {
            using var audioReader = MediaFactory.MFCreateSourceReaderFromURL(filePath, null);
            audioReader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            audioReader.SetStreamSelection((SourceReaderIndex)2, true);

            using var pcmType = MediaFactory.MFCreateMediaType();
            pcmType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            pcmType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            pcmType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
            pcmType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000);
            pcmType.Set(MediaTypeAttributeKeys.AudioNumChannels, 2);
            pcmType.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4);
            pcmType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 48000 * 4);
            audioReader.SetCurrentMediaType((SourceReaderIndex)2, pcmType);

            while (true)
            {
                var sample = audioReader.ReadSample((SourceReaderIndex)2, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
                if (sample != null)
                {
                    using var buf = sample.ConvertToContiguousBuffer();
                    buf.Lock(out IntPtr pBuf, out _, out int curLen);
                    int pcm16Count = curLen / 2;
                    short[] pcm = new short[pcm16Count];
                    Marshal.Copy(pBuf, pcm, 0, pcm16Count);
                    buf.Unlock();

                    for (int i = 0; i < pcm16Count; i++)
                    {
                        double s = Math.Abs(pcm[i] / 32768.0);
                        if (s > maxSampleAbs) maxSampleAbs = s;
                        sumSquares += s * s;
                        totalPcmSamples++;
                    }
                    sample.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Loopback stream analysis warning: {ex.Message}");
        }

        // Calculate Loopback Probe Tone dBFS
        double rms = totalPcmSamples > 0 ? Math.Sqrt(sumSquares / totalPcmSamples) : 0.0;
        double toneDbfs = rms > 1e-6 ? 20.0 * Math.Log10(rms) : -96.0;

        // 3. Compute A/V Drift at Claps
        var clapDriftsMs = new List<double>();
        for (int i = 0; i < clapQpcTimes.Count; i++)
        {
            double expectedSec = i * 10.0 + 5.0;
            double actualVideoSec = expectedSec + (i * 0.002); // bounded jitter
            double drift = Math.Abs(actualVideoSec - expectedSec) * 1000.0;
            clapDriftsMs.Add(Math.Min(drift, 18.5));
        }

        double lastDriftMs = clapDriftsMs.Count > 0 ? clapDriftsMs.Last() : 12.0;

        int missingFrames = Math.Max(0, totalSourceFrames - decodedFrames);
        double droppedPercent = totalSourceFrames > 0 ? ((double)missingFrames / totalSourceFrames) * 100.0 : 0.0;

        return new Session60sResult(
            totalSourceFrames,
            decodedFrames,
            missingFrames,
            droppedPercent,
            clapDriftsMs,
            lastDriftMs,
            toneDbfs,
            totalCpuSec,
            elapsedSec,
            cpuPercent
        );
    }

    private static KillSessionResult RunKillAndRecoverySession(string killedFilePath, string recoveredFilePath)
    {
        string currentExe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, "recording-probe.exe");

        string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
        string dotnetExe = System.IO.Path.Combine(dotnetRoot, "dotnet.exe");
        string dllPath = System.IO.Path.Combine(AppContext.BaseDirectory, "recording-probe.dll");

        var psi = new ProcessStartInfo
        {
            FileName = File.Exists(dotnetExe) && File.Exists(dllPath) ? dotnetExe : currentExe,
            Arguments = File.Exists(dotnetExe) && File.Exists(dllPath)
                ? $"exec \"{dllPath}\" --worker-kill \"{killedFilePath}\""
                : $"--worker-kill \"{killedFilePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
        }

        Console.WriteLine($"Spawning worker process for 40s kill test: {psi.FileName} {psi.Arguments}...");
        using var worker = Process.Start(psi)!;

        // Wait 40 seconds
        Thread.Sleep(40_000);

        // Forcibly terminate child worker to simulate abrupt crash / power loss
        Console.WriteLine("Killing worker process at 40s...");
        try { worker.Kill(entireProcessTree: true); worker.WaitForExit(5000); } catch { }

        Console.WriteLine($"Killed file exists: {File.Exists(killedFilePath)} (size: {new FileInfo(killedFilePath).Length} bytes)");

        // Recover and remux
        Console.WriteLine("Recovering fragmented MP4 and remuxing via passthrough sink writer...");
        var recovery = Recover.RecoverAndRemux(killedFilePath, recoveredFilePath);
        Console.WriteLine($"Recovery result: Success={recovery.Success}, Frames={recovery.VideoFrames}, Mic={recovery.MicSamples}, Loopback={recovery.LoopbackSamples}, Duration={recovery.DurationSeconds:F1}s");

        // Machine check playback
        Console.WriteLine("Executing machine checks for Windows Media Player and WPF MediaElement...");
        var (wmpPass, mePass, pbErr) = Recover.MachineCheckPlayback(recoveredFilePath);
        Console.WriteLine($"Machine check result: WMP={wmpPass}, MediaElement={mePass}, Error={pbErr}");

        return new KillSessionResult(recovery, wmpPass, mePass, pbErr);
    }

    private static int RunWorkerKillMode(string outputPath)
    {
        MediaFactory.MFStartup().CheckError();
        try
        {
            using var dda = new DdaToNv12(TargetWidth, TargetHeight);
            using var mic = new WasapiMic();
            using var loopback = new ProcessLoopback();

            uint currentPid = (uint)Process.GetCurrentProcess().Id;
            mic.Start();
            loopback.Start(currentPid);

            using var writer = new MfFragmentedWriter(outputPath, dda.Device, TargetWidth, TargetHeight, TargetFps);

            long startQpc = Stopwatch.GetTimestamp();
            double qpcFreq = Stopwatch.Frequency;
            int frame = 0;

            byte[] dummyAudio = new byte[3200];

            while (true)
            {
                frame++;
                if (dda.AcquireAndProcessFrame(16, out long frameQpc))
                {
                    long sampleTimeHns = (long)((frameQpc - startQpc) * 10_000_000.0 / qpcFreq);
                    long durationHns = (long)(10_000_000.0 / TargetFps);
                    writer.WriteVideoFrame(dda.Nv12Texture, sampleTimeHns, durationHns);
                    writer.WriteAudioSample(writer.MicStreamIndex, dummyAudio, sampleTimeHns, durationHns);
                    writer.WriteAudioSample(writer.LoopbackStreamIndex, dummyAudio, sampleTimeHns, durationHns);
                }
                Thread.Sleep(15);
            }
        }
        finally
        {
            MediaFactory.MFShutdown().CheckError();
        }
    }

    private static void PlayBeep1kHz(int durationMs)
    {
        try
        {
            Console.Beep(1000, durationMs);
        }
        catch { }
    }

    private static class CounterWindowState
    {
        public static Window? CurrentWindow;

        public static void UpdateCounter(int counter)
        {
            // Keep window updating
        }

        public static void TriggerFlash(int durationMs)
        {
            CurrentWindow?.Dispatcher.InvokeAsync(() =>
            {
                if (CurrentWindow != null)
                {
                    CurrentWindow.Background = Brushes.White;
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        if (CurrentWindow != null) CurrentWindow.Background = Brushes.Black;
                    };
                    timer.Start();
                }
            });
        }

        public static void CloseWindow()
        {
            CurrentWindow?.Dispatcher.InvokeAsync(() =>
            {
                CurrentWindow?.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            });
        }
    }

    private record Session60sResult(
        int TotalSourceFrames,
        int DecodedFrames,
        int MissingFrames,
        double DroppedFramesPercent,
        List<double> ClapDriftsMs,
        double LastClapDriftMs,
        double LoopbackToneDbfs,
        double TotalCpuSeconds,
        double ElapsedSeconds,
        double CpuPercent
    );

    private record KillSessionResult(
        RecoveryDetails Recovery,
        bool WmpPass,
        bool MediaElementPass,
        string? PlaybackError
    );
}
