using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Vortice.MediaFoundation;

namespace RecordingProbe;

public class CriterionResult
{
    [JsonPropertyName("status")] public string Status { get; set; } = "FAIL";
    [JsonPropertyName("measurement")] public object? Measurement { get; set; }
    [JsonPropertyName("details")] public string Details { get; set; } = "";
    [JsonPropertyName("fallback")] public string? Fallback { get; set; }
}

public class ProbeReport
{
    [JsonPropertyName("probe")] public string Probe => "recording-probe";
    [JsonPropertyName("overallStatus")] public string OverallStatus { get; set; } = "FAIL";
    [JsonPropertyName("criteria")] public Dictionary<string, CriterionResult> Criteria { get; set; } = new();
    [JsonPropertyName("hardwareMatrix")] public Dictionary<string, object?> HardwareMatrix { get; set; } = new();
}

public static class Program
{
    private const int TargetFps = 60;
    private const int TargetWidth = 2560;
    private const int TargetHeight = 1440;
    private const double SetupLeadSeconds = 12.0;

    private static double? N(double v) => double.IsNaN(v) || double.IsInfinity(v) ? null : Math.Round(v, 3);

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Console.Error.WriteLine($"UNHANDLED EXCEPTION: {e.ExceptionObject}");

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
        {
            string localDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
            if (Directory.Exists(localDotnet)) Environment.SetEnvironmentVariable("DOTNET_ROOT", localDotnet);
        }

        if (args.Length >= 4 && args[0] == "--clap-helper") return ClapHelper.Run(args);
        if (args.Length >= 3 && args[0] == "--worker-kill") return RunWorkerKillMode(args[1], args[2]);

        if (args.Length >= 2 && args[0] == "--dump-audio") { MediaFactory.MFStartup().CheckError(); TakeAnalyzer.Dump(args[1]); return 0; }
        if (args.Length >= 3 && args[0] == "--test-recover")
        {
            MediaFactory.MFStartup().CheckError();
            var res = Recover.RecoverAndRemux(args[1], args[2]);
            Console.WriteLine($"Recover result: Success={res.Success}, Frames={res.VideoFrames}, Mic={res.MicSamples}, Loopback={res.LoopbackSamples}, Dur={res.DurationSeconds:F1}s, Error={res.ErrorMessage}");
            MediaFactory.MFShutdown().CheckError();
            return res.Success ? 0 : 1;
        }

        bool assertMode = args.Contains("--assert", StringComparer.OrdinalIgnoreCase);
        double takeSeconds = 60.0;
        double killAfterSeconds = 40.0;
        int idx = Array.IndexOf(args, "--seconds");
        if (idx >= 0 && idx + 1 < args.Length)
        {
            takeSeconds = double.Parse(args[idx + 1], CultureInfo.InvariantCulture);
            killAfterSeconds = Math.Min(40.0, takeSeconds * 2.0 / 3.0); // short development runs only
        }

        MediaFactory.MFStartup().CheckError();
        Process? helper = null;
        try
        {
            return RunProbe(assertMode, takeSeconds, killAfterSeconds, out helper);
        }
        catch (Exception ex)
        {
            var errReport = new ProbeReport { OverallStatus = "FAIL" };
            errReport.Criteria["probe_execution"] = new CriterionResult { Status = "FAIL", Details = ex.ToString(), Measurement = ex.Message };
            Console.WriteLine(JsonSerializer.Serialize(errReport, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally
        {
            try { if (helper != null && !helper.HasExited) helper.Kill(true); } catch { }
            MediaFactory.MFShutdown().CheckError();
        }
    }

    // Command line that re-launches this probe; used for the kill worker (child) and the clap helper (not a child).
    private static (string file, string arguments) SelfCommand(string argumentTail)
    {
        string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet");
        string dotnetExe = Path.Combine(dotnetRoot, "dotnet.exe");
        string dll = Path.Combine(AppContext.BaseDirectory, "recording-probe.dll");
        if (File.Exists(dotnetExe) && File.Exists(dll)) return (dotnetExe, $"exec \"{dll}\" {argumentTail}");
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "recording-probe.exe");
        return (exe, argumentTail);
    }

    /// <summary>
    /// Starts the helper through WMI so its parent is WmiPrvSE, not the probe: the process-excluding loopback
    /// excludes the probe's whole process tree and must still record the helper.
    /// </summary>
    private static Process StartHelperOutsideTree(string argumentTail)
    {
        var (file, arguments) = SelfCommand(argumentTail);
        string commandLine = $"\"{file}\" {arguments}".Replace("'", "''");
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
        if (!m.Success || m.Groups[2].Value != "0") throw new InvalidOperationException("helper launch failed: " + output.Trim());
        Console.WriteLine($"Clap helper started outside the probe process tree: pid {m.Groups[1].Value}");
        return Process.GetProcessById(int.Parse(m.Groups[1].Value));
    }

    private static bool args0Keep => Environment.GetCommandLineArgs().Contains("--keep");

    private static int RunProbe(bool assertMode, double takeSeconds, double killAfterSeconds, out Process? helperOut)
    {
        helperOut = null;
        var report = new ProbeReport();
        var hwEncoders = MfFragmentedWriter.GetHardwareEncoders();

        using var dda = new DdaToNv12(TargetWidth, TargetHeight);
        using var mic = new WasapiMic();
        using var loopback = new ProcessLoopback();

        string baseDir = AppContext.BaseDirectory;
        string artifactsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "artifacts"));
        Directory.CreateDirectory(artifactsDir);
        string take60 = Path.Combine(baseDir, "recording_take_60s.mp4");
        string killed = Path.Combine(baseDir, "killed_take_40s.mp4");
        string killedStartFile = Path.Combine(baseDir, "killed_take_40s.start");
        string recovered = Path.Combine(artifactsDir, "recovered_take_40s.mp4");

        report.HardwareMatrix["adapter"] = dda.AdapterName;
        report.HardwareMatrix["output"] = dda.OutputName;
        report.HardwareMatrix["screenResolution"] = $"{dda.ScreenWidth}x{dda.ScreenHeight}";
        report.HardwareMatrix["targetResolution"] = $"{TargetWidth}x{TargetHeight} @ {TargetFps}fps (scaled by the GPU video processor)";
        report.HardwareMatrix["h264HardwareEncoderEnumerated"] = hwEncoders.h264HwName;
        report.HardwareMatrix["hevcHardwareEncoderEnumerated"] = hwEncoders.hevcHwName;
        report.HardwareMatrix["micEndpoint"] = mic.EndpointName;
        report.HardwareMatrix["renderEndpoint"] = loopback.EndpointName;
        report.HardwareMatrix["logicalCores"] = Environment.ProcessorCount;
        report.HardwareMatrix["takeSeconds"] = takeSeconds;

        // ---------------------------------------------------------------- 60 s take
        long freq = Stopwatch.Frequency;
        long t0 = Stopwatch.GetTimestamp() + QpcClock.FromSeconds(SetupLeadSeconds);
        var clapSecs = new List<double>();
        for (double c = 5.0; c <= takeSeconds - 4.0; c += 10.0) clapSecs.Add(c);
        long[] clapQpc = clapSecs.Select(c => t0 + QpcClock.FromSeconds(c)).ToArray();

        var helper = StartHelperOutsideTree(string.Create(CultureInfo.InvariantCulture, $"--clap-helper {t0} 900 {string.Join(",", clapSecs.Select(c => c.ToString(CultureInfo.InvariantCulture)))}"));
        helperOut = helper;

        var window = new CounterWindow(t0, clapQpc);
        window.Start();
        dda.CounterProbeDpi = window.DpiScale;
        using var endpoint = new EndpointLoopback();
        endpoint.Start();
        mic.Start();
        loopback.Start((uint)Environment.ProcessId);
        loopback.PlayProbeTone(440, 0.5f);
        if (Stopwatch.GetTimestamp() >= t0) throw new InvalidOperationException("setup overran the lead time; start instant already passed");

        Console.WriteLine($"Recording {takeSeconds:F0} s take with claps at [{string.Join(", ", clapSecs)}] s ...");
        var stats = RecordingSession.Run(take60, takeSeconds, t0, dda, mic, loopback, TargetWidth, TargetHeight, TargetFps);
        bool toneStillPlaying = loopback.ProbeToneStillPlaying;
        mic.Stop();
        loopback.Stop();
        endpoint.Stop();
        var renderedCounters = window.RenderedCounters;
        var flashCommit = window.FlashCommitQpc;
        double dpi = window.DpiScale;
        long renderCallbacks = window.RenderCallbacks;
        window.Close();
        Console.WriteLine($"Take written: {new FileInfo(take60).Length} bytes, {stats.VideoFramesWritten} frames, CPU {stats.CpuPercent:F2}%");

        var layout = new SourceLayout(dpi, (double)TargetWidth / dda.ScreenWidth, (double)TargetHeight / dda.ScreenHeight);
        Console.WriteLine("Decoding the 60 s take (video + both audio tracks) ...");
        var video = Timed("decode 60 s video", 180, () => TakeAnalyzer.DecodeVideo(take60, layout), m => new VideoAnalysis { Error = m });
        if (args0Keep)
        {
            File.WriteAllLines(Path.Combine(artifactsDir, "take-frames.csv"),
                new[] { "idx,ptsSec,counter,flashLuma,ambiguous" }.Concat(video.Frames.Select((f, i) =>
                    string.Create(CultureInfo.InvariantCulture, $"{i},{f.PtsSec:F5},{f.Counter},{f.FlashLuma:F1},{(f.Ambiguous ? 1 : 0)}"))));
        }
        var (micTrack, loopTrack) = Timed("decode 60 s audio", 180, () => ResolveAudioRoles(take60), m => (new AudioTrack { Error = m }, new AudioTrack { Error = m }));
        var vStats = TakeStats.Video(video, renderedCounters);
        var micStats = TakeStats.Audio(micTrack);
        var loopStats = TakeStats.Audio(loopTrack);
        var endpoint440 = endpoint.Level(440);
        var endpoint660 = endpoint.Level(ClapHelper.ControlToneHz);

        var takePlayback = Timed("playback check of the 60 s take", 60, () => Recover.MachineCheckPlayback(take60), m => (wmpPass: false, mediaElementPass: false, error: (string?)m));

        // ---------------------------------------------------------------- drift
        var claps = new List<object>();
        var rawDriftMs = new List<double?>();
        var driftMs = new List<double?>();
        for (int k = 0; k < clapQpc.Length; k++)
        {
            double expected = (clapQpc[k] - t0) / (double)freq;
            var vo = TakeAnalyzer.FlashOnset(video, k, expected);
            var ao = loopTrack.Usable ? TakeAnalyzer.BeepOnset(loopTrack, k, expected) : new Onset(k, expected, null, "loopback track unusable");
            // Where each event really was, independent of the file: the first desktop frame the capture acquired with
            // the flash white, and the instant the beep reached the render endpoint (all-process loopback).
            var seen = dda.AcquiredFlash.FirstOrDefault(f => f.white && f.qpc >= clapQpc[k] - freq / 20);
            double? ddaSeenSec = seen.qpc != 0 ? (seen.qpc - t0) / (double)freq : null;
            long? epQpc = endpoint.BeepOnsetQpc(clapQpc[k]);
            double? epSec = epQpc.HasValue ? (epQpc.Value - t0) / (double)freq : null;

            double? raw = vo.OnsetSec.HasValue && ao.OnsetSec.HasValue ? Math.Abs(vo.OnsetSec.Value - ao.OnsetSec.Value) * 1000.0 : null;
            double? videoFileErr = vo.OnsetSec.HasValue && ddaSeenSec.HasValue ? (vo.OnsetSec.Value - ddaSeenSec.Value) * 1000.0 : null;
            double? audioFileErr = ao.OnsetSec.HasValue && epSec.HasValue ? (ao.OnsetSec.Value - epSec.Value) * 1000.0 : null;
            double? d = videoFileErr.HasValue && audioFileErr.HasValue ? Math.Abs(videoFileErr.Value - audioFileErr.Value) : null;
            rawDriftMs.Add(raw);
            driftMs.Add(d);
            claps.Add(new
            {
                clap = k + 1,
                scheduledSec = Math.Round(expected, 3),
                videoOnsetInFileSec = N(vo.OnsetSec ?? double.NaN),
                audioOnsetInFileSec = N(ao.OnsetSec ?? double.NaN),
                flashFirstAcquiredByCaptureSec = N(ddaSeenSec ?? double.NaN),
                beepReachedEndpointSec = N(epSec ?? double.NaN),
                flashDisplayLatencyMs = N(ddaSeenSec.HasValue ? (ddaSeenSec.Value - expected) * 1000.0 : double.NaN),
                beepEndpointLatencyMs = N(epSec.HasValue ? (epSec.Value - expected) * 1000.0 : double.NaN),
                videoFileMinusCaptureMs = N(videoFileErr ?? double.NaN),
                audioFileMinusEndpointMs = N(audioFileErr ?? double.NaN),
                absFileVideoMinusFileAudioMs = N(raw ?? double.NaN),
                driftMs = N(d ?? double.NaN),
                videoNote = vo.Note,
                audioNote = ao.Note
            });
        }
        bool allMeasured = driftMs.Count > 0 && driftMs.All(x => x.HasValue);
        double? lastDrift = driftMs.Count > 0 ? driftMs[^1] : null;
        string driftStatus = lastDrift.HasValue && lastDrift.Value >= 40.0 ? "FAIL" : (allMeasured ? "PASS" : "UNCOVERED");
        report.Criteria["av_drift_ms"] = new CriterionResult
        {
            Status = driftStatus,
            Measurement = new
            {
                clapCount = clapQpc.Length,
                definition = "drift = |(video onset in file - flash first acquired by capture) - (audio onset in file - beep at endpoint)|: how far the file's video and audio timestamps disagree about two events that really happened; the raw file-only difference is also listed because the test window's own display stalls move the flash by up to hundreds of ms",
                lastClapDriftMs = N(lastDrift ?? double.NaN),
                allClapsDriftMs = driftMs.Select(x => N(x ?? double.NaN)).ToList(),
                allClapsRawFileOnlyDiffMs = rawDriftMs.Select(x => N(x ?? double.NaN)).ToList(),
                audioCarrierTrack = "process-excluding loopback track; beep played by a separate helper process outside the probe process tree",
                perClap = claps
            },
            Details = driftStatus == "UNCOVERED"
                ? "not every clap had all four onsets (file video, capture flash, file audio, endpoint beep); unmeasured claps are null"
                : $"last clap drift {lastDrift:F1} ms (bar 40 ms)",
            Fallback = driftStatus == "FAIL" ? "QPC re-stamping of audio against the render clock with a drift tracking filter" : null
        };

        // ---------------------------------------------------------------- drops
        var ddaSet = new HashSet<int>(dda.AcquiredCounters);
        int ddaRange = ddaSet.Count == 0 ? 0 : ddaSet.Max() - ddaSet.Min() + 1;
        var outSet = new HashSet<int>(video.Frames.Select(f => f.Counter));
        int ddaLost = ddaSet.Count(c => !outSet.Contains(c));
        bool dropsMeasured = video.Error == null && vStats.Frames > 0 && vStats.CounterRange > 0;
        string dropsStatus = !dropsMeasured ? "FAIL" : (vStats.MissingPercent < 1.0 ? "PASS" : "FAIL");
        report.Criteria["dropped_frames_percent"] = new CriterionResult
        {
            Status = dropsStatus,
            Measurement = new
            {
                expectedSourceFramesAt60fpsFor60s = 3600,
                counterFirst = vStats.CounterMin,
                counterLast = vStats.CounterMax,
                counterRange = vStats.CounterRange,
                uniqueCountersDecoded = vStats.UniqueCounters,
                missingCounterValues = vStats.MissingCounters,
                missingPercent = N(vStats.MissingPercent),
                renderedCounterValuesInRange = vStats.RenderedCountersInRange,
                renderedValuesMissingInOutput = vStats.MissingRenderedCounters,
                windowRenderCallbacks = renderCallbacks,
                outputFramesDecoded = vStats.Frames,
                outputFramesWritten = stats.VideoFramesWritten,
                duplicateFramesWritten = stats.DuplicateFrames,
                surplusFramesThrottled = stats.ThrottledFrames,
                ddaAcquiredFrames = dda.AcquiredFrames,
                ddaAcquiredCounterRange = ddaRange,
                ddaAcquiredUniqueCounters = ddaSet.Count,
                ddaDeliveredCountersMissingInOutput = ddaLost,
                ddaDeliveredLossPercent = N(ddaSet.Count == 0 ? double.NaN : 100.0 * ddaLost / ddaSet.Count),
                sourceSkippedBeforeCapture = ddaRange - ddaSet.Count,
                ddaCoalescedUpdatesNeverAcquired = dda.AccumulatedExtraFrames,
                duplicateCounterFramesInOutput = vStats.DuplicateCounterFrames,
                counterRegressions = vStats.CounterRegressions,
                ambiguousCounterFrames = vStats.AmbiguousFrames,
                avgOutputFps = N(vStats.AvgFps),
                maxPtsGapMs = N(vStats.MaxPtsGapMs),
                counterMinusPtsEarlyMs = N(vStats.EarlyCounterMinusPtsMs),
                counterMinusPtsLateMs = N(vStats.LateCounterMinusPtsMs),
                decodeError = video.Error
            },
            Details = dropsMeasured
                ? $"counter decoded from pixels of every output frame: {vStats.MissingCounters} of {vStats.CounterRange} counter values missing ({vStats.MissingPercent:F3}%)"
                : "no counter could be decoded from the take",
            Fallback = dropsStatus == "FAIL" ? "decoupled capture queue with a dedicated encoder-feed thread" : null
        };

        // ---------------------------------------------------------------- CPU
        report.Criteria["cpu_percent"] = new CriterionResult
        {
            Status = stats.CpuPercent < 15.0 ? "PASS" : "FAIL",
            Measurement = new
            {
                logicalCores = Environment.ProcessorCount,
                totalCpuSeconds = N(stats.TotalCpuSeconds),
                elapsedSeconds = N(stats.ElapsedSeconds),
                cpuPercent = N(stats.CpuPercent),
                includes = "whole probe process: capture loop, counter window rendering, audio callbacks; excludes the separate helper process and the post-take analysis",
                encoderMft = stats.EncoderTransform
            },
            Details = $"{stats.TotalCpuSeconds:F2} CPU s / ({stats.ElapsedSeconds:F2} s x {Environment.ProcessorCount} cores) = {stats.CpuPercent:F2}% (bar 15%)",
            Fallback = stats.CpuPercent < 15.0 ? null : "thread priority and encoder profile tuning"
        };

        // ---------------------------------------------------------------- loopback exclusion
        bool probeToneAudible = loopback.ProbeToneStarted && toneStillPlaying && endpoint440.maxDbfs > -30.0;
        bool controlPresent = loopStats.Usable && loopStats.ControlToneDbfs >= -30.0;
        string loopStatus;
        string loopDetail;
        if (!loopStats.Usable) { loopStatus = "FAIL"; loopDetail = $"loopback track empty or undecodable: {loopStats.Error}"; }
        else if (!controlPresent) { loopStatus = "FAIL"; loopDetail = $"positive control missing: helper 660 Hz tone at {loopStats.ControlToneDbfs:F1} dBFS in the loopback track (needs >= -30)"; }
        else if (!probeToneAudible) { loopStatus = "FAIL"; loopDetail = "probe tone was not demonstrably playing on the endpoint, exclusion untested"; }
        else if (loopStats.ProbeToneDbfs < -60.0) { loopStatus = "PASS"; loopDetail = $"probe 440 Hz tone {loopStats.ProbeToneDbfs:F1} dBFS in the loopback track while the helper control tone reads {loopStats.ControlToneDbfs:F1} dBFS and the endpoint carried the probe tone at {endpoint440.maxDbfs:F1} dBFS"; }
        else { loopStatus = "FAIL"; loopDetail = $"probe 440 Hz tone leaked into the loopback track at {loopStats.ProbeToneDbfs:F1} dBFS (bar -60)"; }
        report.Criteria["loopback_process_exclusion"] = new CriterionResult
        {
            Status = loopStatus,
            Measurement = new
            {
                loopbackTrackDecoded = loopStats.Usable,
                loopbackSeconds = N(loopStats.EndSec),
                probeToneHz = 440,
                probeToneDbfsInLoopbackTrack = N(loopStats.ProbeToneDbfs),
                controlToneHz = ClapHelper.ControlToneHz,
                controlToneNominalDbfs = -20,
                controlToneDbfsInLoopbackTrack = N(loopStats.ControlToneDbfs),
                endpointLoopbackProbeToneMaxDbfs = N(endpoint440.maxDbfs),
                endpointLoopbackProbeToneMedianDbfs = N(endpoint440.medianDbfs),
                endpointLoopbackControlToneMedianDbfs = N(endpoint660.medianDbfs),
                probeToneStartedAndPlayingAtEnd = loopback.ProbeToneStarted && toneStillPlaying,
                loopbackPacketsMissingQpc = loopback.MissingQpcPackets,
                loopbackDiscontinuityPackets = loopback.DiscontinuityPackets,
                loopbackSilentPackets = loopback.SilentPackets,
                loopbackFallback = loopback.FallbackTriggered
            },
            Details = loopDetail,
            Fallback = loopStatus == "PASS" ? null : "direct COM ActivateAudioInterfaceAsync process loopback"
        };

        // Large raw take is no longer needed
        if (!args0Keep) { try { File.Delete(take60); } catch { } }

        // ---------------------------------------------------------------- killed take and recovery
        Console.WriteLine($"Spawning recording worker, kill {killAfterSeconds:F0} s after its recording start ...");
        var killResult = RunKillAndRecover(killed, killedStartFile, recovered, killAfterSeconds, layout);

        bool playbackPass = takePlayback.wmpPass && takePlayback.mediaElementPass && killResult.Playback.wmpPass && killResult.Playback.mediaElementPass;
        report.Criteria["plays_windows_media_player_and_media_element"] = new CriterionResult
        {
            Status = playbackPass ? "PASS" : "FAIL",
            Measurement = new
            {
                recording60s = new { mediaFoundationDecodeToEof = takePlayback.wmpPass, mediaElementPlaysThenEnds = takePlayback.mediaElementPass, error = takePlayback.error },
                recoveredTake = new { mediaFoundationDecodeToEof = killResult.Playback.wmpPass, mediaElementPlaysThenEnds = killResult.Playback.mediaElementPass, error = killResult.Playback.error },
                windowsMediaPlayerApp = "not driven by the probe; see the manual check recorded in the report"
            },
            Details = "Media Foundation source reader decodes both files to EOF; WPF MediaElement plays 2 s (position advances), then seeks near the end and reaches MediaEnded",
            Fallback = playbackPass ? null : "FFmpeg (LGPL, dynamically linked) for muxing only"
        };

        var rv = killResult.VideoStats;
        var rm = killResult.MicStats;
        var rl = killResult.LoopStats;
        bool tracksIntact = killResult.Recovery.Success && rm.Usable && rl.Usable && rv.Frames > 0
            && Math.Abs(rm.EndSec - rv.LastPtsSec) < 1.5 && Math.Abs(rl.EndSec - rv.LastPtsSec) < 1.5
            && rl.ControlToneDbfs >= -30.0;
        string recStatus = tracksIntact ? "PASS" : "FAIL";
        report.Criteria["recovery_and_remux"] = new CriterionResult
        {
            Status = recStatus,
            Measurement = new
            {
                recordingSecondsBeforeKill = N(killResult.RecordedSecondsAtKill),
                killedFileBytes = killResult.KilledBytes,
                recoveredVideoFrames = rv.Frames,
                recoveredVideoSeconds = N(rv.LastPtsSec - rv.FirstPtsSec),
                recoveredAvgFps = N(rv.AvgFps),
                recoveredMaxPtsGapMs = N(rv.MaxPtsGapMs),
                lostTailSecondsVsKill = N(killResult.RecordedSecondsAtKill - rv.LastPtsSec),
                counterRange = rv.CounterRange,
                missingCounterValues = rv.MissingCounters,
                missingPercent = N(rv.MissingPercent),
                remuxedSampleCounts = new { video = killResult.Recovery.VideoFrames, mic = killResult.Recovery.MicSamples, loopback = killResult.Recovery.LoopbackSamples },
                micTrack = rm,
                loopbackTrack = rl,
                recoveryError = killResult.Recovery.ErrorMessage,
                workerEncoderMft = killResult.Encoder
            },
            Details = tracksIntact
                ? $"killed take remuxed: {rv.Frames} video frames over {rv.LastPtsSec - rv.FirstPtsSec:F1} s ({rv.AvgFps:F1} fps), mic and loopback tracks decode and end within 1.5 s of the video; loopback carries the control tone"
                : $"recovery incomplete: {killResult.Recovery.ErrorMessage}; mic usable={rm.Usable}, loopback usable={rl.Usable}",
            Fallback = recStatus == "PASS" ? null : "FFmpeg (LGPL, dynamically linked) for muxing only"
        };

        report.OverallStatus = report.Criteria.Values.Any(c => c.Status == "FAIL") ? "FAIL"
            : report.Criteria.Values.Any(c => c.Status == "UNCOVERED") ? "UNCOVERED" : "PASS";
        report.HardwareMatrix["take60Video"] = vStats;
        report.HardwareMatrix["take60Mic"] = micStats;
        report.HardwareMatrix["take60Loopback"] = loopStats;
        report.HardwareMatrix["sourceDpiScale"] = dpi;
        report.HardwareMatrix["discardedPreStartAudioPackets"] = stats.PreStartAudioDiscarded;

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        File.WriteAllText(Path.Combine(artifactsDir, "recording-probe-result.json"), json);

        if (!assertMode) return 0;
        return report.OverallStatus == "PASS" ? 0 : 1;
    }

    // A Media Foundation call can block forever (the sink writer did when audio ran ahead of video). Every post-take
    // step runs on a background thread with a deadline; a timeout becomes an explicit error that the criterion turns
    // into FAIL, never a hang and never a default value.
    private static T Timed<T>(string step, int seconds, Func<T> work, Func<string, T> onTimeout)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        if (!t.Join(TimeSpan.FromSeconds(seconds)))
        {
            Console.WriteLine($"TIMEOUT: {step} did not finish within {seconds} s");
            return onTimeout($"{step} timed out after {seconds} s");
        }
        if (error != null)
        {
            Console.WriteLine($"ERROR: {step}: {error.Message}");
            return onTimeout($"{step} failed: {error.Message}");
        }
        return result;
    }

    // The source reader lists the two AAC tracks in a container-dependent order (it differs between the direct
    // take and the remuxed file), so the loopback track is the one that carries the helper's control tone.
    private static (AudioTrack mic, AudioTrack loop) ResolveAudioRoles(string path)
    {
        var a = TakeAnalyzer.DecodeAudio(path, 0);
        var b = TakeAnalyzer.DecodeAudio(path, 1);
        double la = a.Usable ? TakeStats.Audio(a).ControlToneDbfs : -200;
        double lb = b.Usable ? TakeStats.Audio(b).ControlToneDbfs : -200;
        return lb > la ? (a, b) : (b, a);
    }

    private sealed record KillResult(
        RecoveryDetails Recovery,
        (bool wmpPass, bool mediaElementPass, string? error) Playback,
        VideoStats VideoStats,
        AudioStats MicStats,
        AudioStats LoopStats,
        double RecordedSecondsAtKill,
        long KilledBytes,
        string Encoder);

    private static KillResult RunKillAndRecover(string killedPath, string startFile, string recoveredPath, double killAfterSeconds, SourceLayout layout)
    {
        if (File.Exists(startFile)) File.Delete(startFile);
        var (file, arguments) = SelfCommand($"--worker-kill \"{killedPath}\" \"{startFile}\"");
        var psi = new ProcessStartInfo { FileName = file, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true };
        using var worker = Process.Start(psi)!;

        // The worker writes its recording start QPC (and encoder MFT) once the loop begins.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(startFile) || new FileInfo(startFile).Length == 0)
        {
            if (DateTime.UtcNow > deadline || worker.HasExited) throw new InvalidOperationException("worker never started recording");
            Thread.Sleep(100);
        }
        Thread.Sleep(200);
        var lines = File.ReadAllLines(startFile);
        long workerStart = long.Parse(lines[0], CultureInfo.InvariantCulture);
        string encoder = lines.Length > 1 ? lines[1] : "unknown";

        long killAt = workerStart + QpcClock.FromSeconds(killAfterSeconds);
        while (Stopwatch.GetTimestamp() < killAt) Thread.Sleep(5);
        long killQpc = Stopwatch.GetTimestamp();
        try { worker.Kill(entireProcessTree: true); worker.WaitForExit(5000); } catch { }
        double recorded = (killQpc - workerStart) / (double)Stopwatch.Frequency;
        long killedBytes = new FileInfo(killedPath).Length;
        Console.WriteLine($"Worker killed after {recorded:F2} s of recording; killed file {killedBytes} bytes");

        var recovery = Timed("recover and remux", 180, () => Recover.RecoverAndRemux(killedPath, recoveredPath), m => new RecoveryDetails(false, 0, 0, 0, 0, m));
        Console.WriteLine($"Recovery: Success={recovery.Success}, video={recovery.VideoFrames}, mic={recovery.MicSamples}, loopback={recovery.LoopbackSamples}");

        var playback = Timed("playback check of the recovered take", 60, () => Recover.MachineCheckPlayback(recoveredPath), m => (wmpPass: false, mediaElementPass: false, error: (string?)m));
        var video = Timed("decode recovered video", 120, () => TakeAnalyzer.DecodeVideo(recoveredPath, layout), m => new VideoAnalysis { Error = m });
        var (mic, loop) = Timed("decode recovered audio", 120, () => ResolveAudioRoles(recoveredPath), m => (new AudioTrack { Error = m }, new AudioTrack { Error = m }));
        var result = new KillResult(recovery, playback, TakeStats.Video(video, null), TakeStats.Audio(mic), TakeStats.Audio(loop), recorded, killedBytes, encoder);
        try { File.Delete(killedPath); File.Delete(startFile); } catch { }
        return result;
    }

    // Child process: the same record loop as the 60 s take (real DDA, mic and loopback) until it is killed.
    private static int RunWorkerKillMode(string outputPath, string startFile)
    {
        MediaFactory.MFStartup().CheckError();
        try
        {
            using var dda = new DdaToNv12(TargetWidth, TargetHeight);
            using var mic = new WasapiMic();
            using var loopback = new ProcessLoopback();
            long t0 = Stopwatch.GetTimestamp() + QpcClock.FromSeconds(3.0);
            var window = new CounterWindow(t0, Array.Empty<long>());
            window.Start();
            mic.Start();
            loopback.Start((uint)Environment.ProcessId);
            RecordingSession.Run(outputPath, 0, t0, dda, mic, loopback, TargetWidth, TargetHeight, TargetFps, onLoopStarted: encoderName =>
            {
                File.WriteAllText(startFile, t0.ToString(CultureInfo.InvariantCulture) + Environment.NewLine + encoderName);
            });
            return 0;
        }
        finally
        {
            MediaFactory.MFShutdown().CheckError();
        }
    }
}
