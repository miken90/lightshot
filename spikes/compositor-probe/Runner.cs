using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Vortice.DXGI;

namespace CompositorProbe;

/// <summary>Runs the spike C measurements in order against the live HwndHost window and fills the report.</summary>
public sealed class Runner
{
    private readonly App _app;
    private readonly MainWindow _win;
    private readonly IPresentHost _host;
    private readonly string _clip;
    private readonly RunOptions _o;
    private readonly double _clipGenSec;
    private readonly ProbeReport _report = new();
    private PreviewPlayer _player = null!;
    private int _syncInterval = 1;
    private double _hz;

    public Runner(App app, MainWindow win, IPresentHost host, string clip, RunOptions o, double clipGenSec)
    { _app = app; _win = win; _host = host; _clip = clip; _o = o; _clipGenSec = clipGenSec; }

    private T UI<T>(Func<T> f) => _app.Dispatcher.Invoke(f);
    private void UI(Action a) => _app.Dispatcher.Invoke(a);
    private static double Pct(List<double> v, double p) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? double.NaN : s[Math.Min(s.Count - 1, (int)Math.Ceiling(p * s.Count) - 1)]; }
    private static double R(double v, int d = 2) => double.IsNaN(v) || double.IsInfinity(v) ? double.NaN : Math.Round(v, d);

    public async Task<ProbeReport> RunAsync()
    {
        await Task.Delay(500);
        CollectEnvironment();
        SetClientSize(2560, 1440);
        _report.Environment["hostClientAtStart"] = $"{_host.BufferWidth}x{_host.BufferHeight}";
        _player = new PreviewPlayer(_host, _clip) { SyncInterval = _syncInterval };
        try
        {
            await WarmUp();
            await MeasureRefresh();
            await SeekTest();
            await ExportTest();
            await PlaybackAndOverlayTest();
            await ResizeTest();
            await DpiTest();
        }
        catch (Exception ex) { _report.ExecutionError = ex.ToString(); }
        finally { if (_player.RenderError != null) _report.Environment["renderError"] = _player.RenderError.ToString(); _player.Dispose(); }
        return _report;
    }

    // ---------------------------------------------------------------- environment
    private void CollectEnvironment()
    {
        var e = _report.Environment;
        e["adapter"] = _host.AdapterName; e["output"] = _host.OutputName;
        e["osBuild"] = Environment.OSVersion.Version.ToString();
        e["windowDpi"] = UI(() => Native.GetDpiForWindow(_win.Hwnd));
        var info = new DWM_TIMING_INFO { cbSize = (uint)Marshal.SizeOf<DWM_TIMING_INFO>() };
        int hr = Native.DwmGetCompositionTimingInfo(_win.Hwnd, ref info);
        _hz = hr == 0 && info.qpcRefreshPeriod > 0 ? Stopwatch.Frequency / (double)info.qpcRefreshPeriod : double.NaN;
        e["dwmRefreshHz"] = R(_hz, 3); e["dwmTimingHr"] = $"0x{hr:X8}";
        int interval = (int)Math.Round(_hz / 60.0);
        _syncInterval = interval >= 1 && Math.Abs(_hz / interval - 60.0) < 0.6 ? interval : 0;
        e["syncIntervalFor60fps"] = _syncInterval;
        if (_syncInterval == 0) _syncInterval = 1;
        var mons = new List<object>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT rc, IntPtr d) =>
        {
            Native.GetDpiForMonitor(h, Native.MDT_EFFECTIVE_DPI, out uint dx, out _);
            mons.Add(new { handle = h.ToInt64(), rect = rc.ToString(), dpi = dx, primaryOfWindow = h == _host.Monitor });
            return true;
        }, IntPtr.Zero);
        e["monitors"] = mons;
    }

    // ---------------------------------------------------------------- window helpers
    private void SetClientSize(int cw, int ch, int? x = null, int? y = null)
    {
        for (int i = 0; i < 4; i++)
        {
            UI(() =>
            {
                Native.GetWindowRect(_win.Hwnd, out var wr); Native.GetClientRect(_win.Hwnd, out var cr);
                Native.SetWindowPos(_win.Hwnd, IntPtr.Zero, x ?? wr.Left, y ?? wr.Top, cw + (wr.Width - cr.Width), ch + (wr.Height - cr.Height), Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            });
            var end = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            while (Stopwatch.GetTimestamp() < end && (_host.BufferWidth != cw || _host.BufferHeight != ch)) Thread.Sleep(10);
            if (_host.BufferWidth == cw && _host.BufferHeight == ch) return;
        }
    }

    private (long priv, long gpuLocal, long gpuNonLocal, uint gdi, uint user, int handles) Memory()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Thread.Sleep(300);
        var p = Process.GetCurrentProcess(); p.Refresh();
        long loc = 0, non = 0;
        try
        {
            using var a3 = _host.Adapter.QueryInterface<IDXGIAdapter3>();
            loc = (long)a3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local).CurrentUsage;
            non = (long)a3.QueryVideoMemoryInfo(0, MemorySegmentGroup.NonLocal).CurrentUsage;
        }
        catch { loc = -1; non = -1; }
        return (p.PrivateMemorySize64, loc, non, Native.GetGuiResources(p.Handle, 0), Native.GetGuiResources(p.Handle, 1), p.HandleCount);
    }

    // ---------------------------------------------------------------- phases
    private async Task WarmUp()
    {
        for (int i = 0; i < 3; i++) await _player.SeekAsync(100 + i * 37);
    }

    // The display rate comes from the swapchain's own frame statistics, not from a configured value.
    private async Task MeasureRefresh()
    {
        var m = await _player.MeasureRefreshAsync(150);
        _hz = m.hz;
        _report.Environment["measuredRefreshHz"] = R(m.hz, 3);
        _report.Environment["refreshMeasure"] = $"{m.refreshes} refreshes in {R(m.seconds, 4)} s over {m.presents} presents (GetFrameStatistics SyncRefreshCount / SyncQPCTime)";
        int interval = (int)Math.Round(m.hz / 60.0);
        _syncInterval = interval >= 1 && Math.Abs(m.hz / interval - 60.0) < 1.0 ? interval : 0;
        _report.Environment["syncIntervalFor60fps"] = _syncInterval;
        if (_syncInterval == 0) _syncInterval = 1;
        _player.SyncInterval = _syncInterval;
    }

    private async Task<(List<SeekResult> results, List<(double setPositionMs, double firstFrameMs, double totalMs, int frames)> timings)> RunSeeks(int syncInterval)
    {
        var rng = new Random(261005);
        var targets = new List<int>();
        for (int i = 0; i < _o.SeekCount; i++) targets.Add(rng.Next(0, _o.ClipFramesAvailable));
        targets[0] = 0; targets[1] = 2399; targets[2] = 1200; targets[3] = 1201; // edges, mid, and the immediate next frame
        var results = new List<SeekResult>();
        var timings = new List<(double setPositionMs, double firstFrameMs, double totalMs, int frames)>();
        foreach (var t in targets) { results.Add(await _player.SeekAsync(t, syncInterval)); timings.Add(_player.LastSeekTiming); await Task.Delay(120); }
        return (results, timings);
    }

    private static double Ms(long qpc) => qpc * 1000.0 / Stopwatch.Frequency;
    private string? FallbackNote => _host.Kind == "HwndHost" ? "D3DImage host" : "none left in the spec";

    // Latency the desktop observer saw: the first desktop frame carrying the target's strip code whose display-side present time is at or after the API call.
    private static (double presentMs, double arrivalMs) DdaLatency(SeekResult r, DdaObservation[] obs)
    {
        int code = r.TargetFrame & 0xFFFF;
        foreach (var o in obs)
            if (o.Readable && o.Code == code && o.LastPresentQpc >= r.T0Qpc) return (Ms(o.LastPresentQpc - r.T0Qpc), Ms(o.ArrivalQpc - r.T0Qpc));
        return (double.NaN, double.NaN);
    }

    private async Task SeekTest()
    {
        // The stamp sits in a corner of the source; zoom about the cursor can push part of it off the canvas, so the stamp read uses the unzoomed layout.
        _player.ZoomEnabled = false;
        DdaMonitor? dda = null; string? ddaError = null;
        try { dda = new DdaMonitor(_host.Adapter, _host.Monitor) { ObserveWindow = _host.ClientHwnd }; dda.Start(); } catch (Exception ex) { ddaError = ex.Message; }
        await Task.Delay(300);
        // Same 20 seeks twice: presented with the playback cadence (sync interval 1) and presented immediately (interval 0, scrub behaviour).
        var vsync = await RunSeeks(_syncInterval);
        var now = await RunSeeks(0);
        _player.ZoomEnabled = true;
        await Task.Delay(300);
        dda?.Stop();
        var obs = dda?.Observations() ?? Array.Empty<DdaObservation>();
        double Dda(SeekResult r) => DdaLatency(r, obs).presentMs;
        string Line(string name, List<SeekResult> r)
        {
            var l = r.Select(Dda).ToList(); var f = l.Where(x => !double.IsNaN(x)).ToList();
            var sl = r.Select(x => x.LatencyMs).Where(x => !double.IsNaN(x)).ToList();
            return $"{name}: desktop-observed median {R(Pct(f, .5), 1)} ms, p95 {R(Pct(f, .95), 1)} ms, max {R(f.Count == 0 ? double.NaN : f.Max(), 1)} ms, over 150 ms {f.Count(x => x >= 150)}/{l.Count}, unmeasured {l.Count - f.Count}; " +
                   (sl.Count == 0 ? "frame-statistics latency n/a" : $"frame-statistics max {R(sl.Max(), 1)} ms over {sl.Count} measured") + $"; stamp mismatches {r.Count(x => !x.StampMatches)}";
        }
        var latNow = now.results.Select(Dda).ToList();
        int unmeasured = latNow.Count(double.IsNaN), over = latNow.Count(x => !double.IsNaN(x) && x >= 150);
        int mismatches = now.results.Count(r => !r.StampMatches);
        string status = mismatches > 0 || over > 0 ? "FAIL" : unmeasured > 0 || dda == null ? "UNCOVERED" : "PASS";
        object Rows(List<SeekResult> results, List<(double setPositionMs, double firstFrameMs, double totalMs, int frames)> timings) =>
            results.Select((r, i) => new
            {
                decoder = new { setPositionMs = R(timings[i].setPositionMs, 1), firstFrameMs = R(timings[i].firstFrameMs, 1), totalMs = R(timings[i].totalMs, 1), framesDecoded = timings[i].frames },
                target = r.TargetFrame, framesAfterKeyframe = r.TargetFrame % 30, stamp = r.StampDecoded, ok = r.StampMatches,
                desktopObservedMs = R(DdaLatency(r, obs).presentMs, 1), desktopArrivalMs = R(DdaLatency(r, obs).arrivalMs, 1),
                frameStatsMs = R(r.LatencyMs, 1), syncError = r.SyncError, decodeReadyMs = R(r.DecodeReadyMs, 1), presentCallMs = R(r.PresentCallMs, 1)
            }).ToList();
        _report.Criteria["seek"] = new Criterion
        {
            Status = status,
            Summary = "20 seeks per mode (bar < 150 ms every seek, judged on the scrub mode). " + Line("present at vblank (sync 1)", vsync.results) + "; " + Line("present immediately (sync 0)", now.results) + (ddaError != null ? "; DDA unavailable: " + ddaError : ""),
            Measurement = new
            {
                bar = "API call QPC -> display-side present time (DDA LastPresentTime) of the first desktop frame whose top strip reads the target frame number, every seek < 150 ms; stamp read from the pre-Present back buffer equals the target. A seek whose frame never appeared on the desktop is unmeasured, never a pass",
                judgedOn = "sync interval 0 (scrub), desktop-observed latency",
                syncInterval1 = Rows(vsync.results, vsync.timings),
                syncInterval0 = Rows(now.results, now.timings),
                ddaObservations = obs.Length,
                codePath = "PreviewPlayer.SeekAsync (strip = frame number), Runner.DdaLatency, DdaMonitor.Observe; frame-statistics path PreviewPlayer.WaitSync (HwndHost only)"
            },
            Fallback = status == "FAIL" ? FallbackNote : null
        };
        dda?.Dispose();
    }

    // Per-frame detail for the diagnostic rows: which effect carries the GPU-vs-WARP and WARP-vs-WARP deltas.
    private async Task ExportTest()
    {
        int[] frames = { 0, 30, 90, 180, 240, 300, 480, 900, 1234, 1777, 2222, 2399 };
        var rows = new List<CompareResult>(); var gpuRows = new List<CompareResult>();
        string adapter;
        using (var previewWarp = new OffscreenExport())
        {
            adapter = previewWarp.AdapterName;
            foreach (int f in frames)
            {
                var cf = await _player.CaptureFrameAsync(f, strips: true);
                gpuRows.Add(OffscreenExport.Diff(cf, cf.PreviewPixels, previewWarp.Render(cf)));
                // Criterion: the preview path on a WARP device vs a fresh WARP device doing the export, same Render().
                var previewPixels = previewWarp.Render(cf);
                byte[] exported;
                using (var export = new OffscreenExport()) exported = export.Render(cf);
                rows.Add(OffscreenExport.Diff(cf, previewPixels, exported));
            }
        }
        int max = rows.Max(r => r.MaxDelta), gpuMax = gpuRows.Max(r => r.MaxDelta);

        // Effect isolation on two frames: switch blur and shadow off, on the GPU preview, on WARP-vs-WARP.
        var iso = new List<object>();
        foreach (int f in new[] { 0, 30 })
            foreach (var (name, nb, ns) in new[] { ("all effects", false, false), ("no blur", true, false), ("no shadow", false, true), ("no blur, no shadow", true, true) })
            {
                var cf = await _player.CaptureFrameAsync(f, strips: true, noBlur: nb, noShadow: ns);
                using var a = new OffscreenExport(); using var b = new OffscreenExport();
                var warp = OffscreenExport.Diff(cf, a.Render(cf), b.Render(cf));
                var gpu = OffscreenExport.Diff(cf, cf.PreviewPixels, a.Render(cf));
                iso.Add(new { frame = f, effects = name, warpVsWarpMaxDelta = warp.MaxDelta, warpVsWarpOverTwo = warp.OverTwo, gpuVsWarpMaxDelta = gpu.MaxDelta, gpuVsWarpOverTwo = gpu.OverTwo, gpuWorst = gpu.Worst });
            }
        _report.Criteria["export_equals_preview"] = new Criterion
        {
            Status = max <= 2 ? "PASS" : "FAIL",
            Summary = $"{rows.Count} frames at {_host.BufferWidth}x{_host.BufferHeight}, preview rendered on WARP vs export rendered on a fresh WARP device through the same Render: max channel delta {max} (bar <= 2), {rows.Sum(r => r.OverTwo)} channel values over 2. " +
                      $"Diagnostic, not the criterion: hardware GPU ({_host.AdapterName}) preview vs WARP max delta {gpuMax} ({gpuRows.Sum(r => r.OverTwo)} channel values over 2)",
            Measurement = new
            {
                bar = "preview path and export path both rendered on WARP through ProbeCompositor.Render, same BGRA source bytes, max channel delta <= 2",
                exportAdapter = adapter,
                frames = rows.Select(r => new { r.Frame, t = R(r.T, 3), r.MaxDelta, r.OverTwo, r.Zoomed, r.Worst }),
                diagnosticGpuVsWarp = new { adapter = _host.AdapterName, maxDelta = gpuMax, frames = gpuRows.Select(r => new { r.Frame, r.MaxDelta, r.OverTwo, r.Worst }) },
                effectIsolation = iso,
                codePath = "PreviewPlayer.CaptureFrameAsync, OffscreenExport.Render/Diff, Runner.ExportTest"
            },
            Fallback = max <= 2 ? null : "none defined in spec; effect isolation rows show blur vs shadow"
        };
    }

    private sealed record DdaSequence(int Observations, int Pairs, double PeriodMs, int Stalls, List<object> StallRows, long UnexplainedSkips, int SkipEvents, List<object> SkipRows, int Discontinuities, int Unreadable, double MaxDtMs);

    // Pairs of consecutive readable desktop frames in the window. dcode = presents the strip advanced by, accumulated = desktop updates DWM merged into the
    // frame the observer received. A stall is a display-side gap beyond what dcode explains; an unexplained skip is a code advance larger than the merged updates.
    private static DdaSequence AnalyzeDda(DdaObservation[] all, long wStart, long wEnd, long fp)
    {
        var win = all.Where(o => o.LastPresentQpc >= wStart && o.LastPresentQpc < wEnd).ToList();
        var pairs = new List<(int dc, double dt, uint acc, long q)>(); int unreadable = 0;
        DdaObservation? prev = null;
        foreach (var o in win)
        {
            if (!o.Readable) { unreadable++; prev = null; continue; }
            if (prev is { } p) pairs.Add(((o.Code - p.Code) & 0xFFFF, Ms(o.LastPresentQpc - p.LastPresentQpc), o.Accumulated, o.LastPresentQpc));
            prev = o;
        }
        var unit = pairs.Where(x => x.dc == 1).Select(x => x.dt).ToList();
        double period = Pct(unit, .5);
        int stalls = 0, skipEv = 0, disc = 0; long skips = 0; double maxDt = 0;
        var stallRows = new List<object>(); var skipRows = new List<object>();
        if (!double.IsNaN(period))
            foreach (var x in pairs)
            {
                if (x.dc == 0) continue;
                if (x.dc > 600) { disc++; continue; }
                maxDt = Math.Max(maxDt, x.dt);
                double at = (x.q - fp) / (double)Stopwatch.Frequency;
                if (x.dt > (x.dc + 0.5) * period) { stalls++; if (stallRows.Count < 60) stallRows.Add(new { atSec = R(at, 2), gapMs = R(x.dt, 1), advancedBy = x.dc, accumulated = x.acc }); }
                if (x.dc > x.acc) { skips += x.dc - x.acc; skipEv++; if (skipRows.Count < 60) skipRows.Add(new { atSec = R(at, 2), codeAdvance = x.dc, accumulated = x.acc }); }
            }
        return new DdaSequence(win.Count, pairs.Count, period, stalls, stallRows, skips, skipEv, skipRows, disc, unreadable, maxDt);
    }

    private async Task PlaybackAndOverlayTest()
    {
        // Strips on during playback so the desktop observer can follow the presents; they are part of every measured frame.
        _player.StripsEnabled = true;
        const double warm = 2.0;
        var ov = new OverlayWindow(_win.Hwnd);
        var cr = UI(() => Native.ClientScreenRect(_host.ClientHwnd));
        ov.ShowAt(cr.Left + 200, cr.Top + 200);
        DdaMonitor? dda = null; string? ddaError = null;
        try { dda = new DdaMonitor(_host.Adapter, _host.Monitor) { ObserveWindow = _host.ClientHwnd, OverlayRect = () => ov.ScreenRect, OverlayVisible = () => ov.Visible, MissContext = () => WindowsAbove(ov.Hwnd), MissDumpPath = Path.Combine(Path.GetDirectoryName(_clip)!, "overlay-first-miss-360x96.bgra") }; dda.Start(); }
        catch (Exception ex) { ddaError = ex.Message; }
        await Task.Delay(500);
        var dih = _host as D3dImageHost;
        long replaced0 = dih?.ReplacedBeforeCommit ?? 0, commits0 = dih?.Commits ?? 0;

        var zEvents = new List<double>();
        int nextEvent = 0;
        double[] eventAt = { 8, 16, 24 };
        var stats = await _player.PlayAsync(_o.PlaySeconds + warm + 0.3, el =>
        {
            if (nextEvent < eventAt.Length && el >= eventAt[nextEvent])
            {
                nextEvent++; zEvents.Add(R(el, 1));
                // z-order stress: re-activate and raise the owner window; an owned popup must stay above it.
                UI(() => { _win.Activate(); Native.BringWindowToTop(_win.Hwnd); Native.SetForegroundWindow(_win.Hwnd); });
            }
        });
        dda?.Stop();
        ov.Hide();
        long replaced = (dih?.ReplacedBeforeCommit ?? 0) - replaced0, commits = (dih?.Commits ?? 0) - commits0;
        _player.StripsEnabled = false;

        // ---- 60 fps: the window starts `warm` s after the first present; every gap inside it counts, start-up gaps are listed apart.
        double F = Stopwatch.Frequency;
        long fp = stats.FirstPresentQpc, wStart = fp + (long)(warm * F), wEnd = wStart + (long)(_o.PlaySeconds * F);
        var s = stats.Samples.OrderBy(x => x.PresentCount).ToList();
        long extra = 0, startupExtra = 0; int gapEvents = 0, startupGaps = 0; uint maxGap = 0; var gapAt = new List<object>(); var startupAt = new List<object>();
        for (int i = 1; i < s.Count; i++)
        {
            long dPc = s[i].PresentCount - s[i - 1].PresentCount;
            long dRc = (long)s[i].PresentRefreshCount - s[i - 1].PresentRefreshCount;
            long over = dRc - dPc * _syncInterval;
            if (over <= 0) continue;
            double at = (s[i].SyncQpc - fp) / F;
            var row = new { atSec = R(at, 2), extraRefreshes = over, sourceIndex = s[i].SourceIndex };
            if (s[i].SyncQpc < wStart) { startupExtra += over; startupGaps++; startupAt.Add(row); }
            else if (s[i].SyncQpc < wEnd) { extra += over; gapEvents++; maxGap = (uint)Math.Max(maxGap, dRc); gapAt.Add(row); }
        }
        int srcSkipped = stats.SkipEvents.Where(e => e.qpc >= wStart && e.qpc < wEnd).Sum(e => e.skipped);
        int srcSkippedStartup = stats.SkipEvents.Where(e => e.qpc < wStart).Sum(e => e.skipped);
        int repeatsWin = stats.RepeatEvents.Count(e => e.qpc >= wStart && e.qpc < wEnd), starvedWin = stats.RepeatEvents.Count(e => e.qpc >= wStart && e.qpc < wEnd && e.starved);
        int presentsWin = stats.PresentQpcs.Count(q => q >= wStart && q < wEnd);
        double rate = presentsWin / _o.PlaySeconds;
        var seq = dda == null ? null : AnalyzeDda(dda.Observations(), wStart, wEnd, fp);
        bool haveStats = s.Count > 0;
        double statsHz = haveStats ? _hz : double.NaN;
        bool hzOk = !haveStats || Math.Abs(_hz / _syncInterval - 60.0) < 0.6;
        bool displayOk = seq != null && !double.IsNaN(seq.PeriodMs);
        bool pass = hzOk && stats.PresentFailures == 0 && srcSkipped == 0 && Math.Abs(rate - 60.0) < 1.0 && (!haveStats || extra == 0) && displayOk && seq!.Stalls == 0 && seq.UnexplainedSkips == 0;
        string fpsStatus = (haveStats && !hzOk) || !displayOk ? "UNCOVERED" : pass ? "PASS" : "FAIL";
        _report.Criteria["preview_60fps"] = new Criterion
        {
            Status = fpsStatus,
            Summary = $"window {R(_o.PlaySeconds, 0)} s starting {warm} s after the first present: {presentsWin} presents ({R(rate, 2)}/s); " +
                      (haveStats ? $"PresentRefreshCount extra refreshes {extra} in {gapEvents} gaps (max gap {maxGap}, {R(_hz, 2)} Hz, sync {_syncInterval}); " : "host has no frame statistics; ") +
                      (seq == null ? "desktop observer unavailable: " + ddaError : $"desktop-observed over {seq.Observations} frames ({seq.Pairs} pairs, unit period {R(seq.PeriodMs, 2)} ms): display stalls {seq.Stalls} (max gap {R(seq.MaxDtMs, 1)} ms), unexplained skipped presents {seq.UnexplainedSkips} in {seq.SkipEvents} events, unreadable {seq.Unreadable}; ") +
                      $"source frames skipped {srcSkipped}, repeated {repeatsWin} (decode-starved {starvedWin}); start-up (first {warm} s, not judged): {startupGaps} gaps / {startupExtra} extra refreshes, {srcSkippedStartup} source skipped" +
                      (dih == null ? "" : $"; D3DImage commits {commits}, presents replaced before commit {replaced}"),
            Measurement = new
            {
                bar = "30 s window after a 2 s warm-up: no PresentRefreshCount gap beyond the sync interval (when the host has frame statistics), no desktop-observed stall, no unexplained skipped present, no skipped source frame, 60 +- 1 presents/s",
                windowSeconds = _o.PlaySeconds, warmupSeconds = warm, presentsInWindow = presentsWin, presentsPerSec = R(rate, 3), totalPresents = stats.Presents, playSeconds = R(stats.DurationSec, 3),
                frameStats = haveStats ? new { samples = s.Count, extraRefreshes = extra, gapEvents, maxRefreshDelta = maxGap, gaps = gapAt, refreshHz = R(statsHz, 3) } : null,
                startup = new { gapEvents = startupGaps, extraRefreshes = startupExtra, gaps = startupAt, sourceSkipped = srcSkippedStartup },
                desktopObserved = seq == null ? null : new { seq.Observations, seq.Pairs, periodMs = R(seq.PeriodMs, 3), seq.Stalls, stalls = seq.StallRows, unexplainedSkippedPresents = seq.UnexplainedSkips, skipEvents = seq.SkipEvents, skips = seq.SkipRows, seq.Discontinuities, seq.Unreadable, maxGapMs = R(seq.MaxDtMs, 2) },
                sourceSkippedInWindow = srcSkipped, repeatsInWindow = repeatsWin, decodeStarvedInWindow = starvedWin, zOrderStressAtSec = zEvents,
                d3dImage = dih == null ? null : new { commits, replacedBeforeCommit = replaced, frontBufferLostEvents = dih.FrontBufferLostEvents },
                presentFailures = stats.PresentFailures, statsDisjoint = stats.StatsDisjoint, statsUnavailable = stats.StatsUnavailable,
                renderCpuMsAvg = R(stats.RenderMsSum / Math.Max(1, stats.Presents), 3), renderCpuMsMax = R(stats.RenderMsMax, 3), canvas = $"{_host.BufferWidth}x{_host.BufferHeight}",
                codePath = "PreviewPlayer.PlayTick/CollectStats; Runner.PlaybackAndOverlayTest window + AnalyzeDda; DdaMonitor.Observe"
            },
            Fallback = fpsStatus == "FAIL" ? FallbackNote : null
        };

        // ---- overlay
        if (dda == null)
        {
            _report.Criteria["overlay_airspace"] = new Criterion { Status = "UNCOVERED", Summary = "Desktop Duplication could not be created: " + ddaError };
        }
        else
        {
            bool ovPass = dda.OverlayChecked >= 1000 && dda.OverlayMissing == 0 && dda.Errors == 0;
            _report.Criteria["overlay_airspace"] = new Criterion
            {
                Status = dda.OverlayChecked < 1000 ? "UNCOVERED" : ovPass ? "PASS" : "FAIL",
                Summary = $"DDA delivered {dda.UpdatedFrames} desktop frames; overlay region checked in {dda.OverlayChecked}, missing in {dda.OverlayMissing} (15 sample points must all read the marker colour); DDA errors {dda.Errors}; z-order stress at {string.Join(", ", zEvents)} s",
                Measurement = new
                {
                    bar = "zero frames with the overlay marker missing over the run",
                    ddaFrames = dda.Frames, updatedFrames = dda.UpdatedFrames, checkedFrames = dda.OverlayChecked, missing = dda.OverlayMissing,
                    missingAtSec = dda.OverlayMissingAtSec, firstMissWindowsAbove = dda.MissContextText, errors = dda.Errors, lastError = dda.LastError, timeouts = dda.Timeouts, zOrderStressAtSec = zEvents,
                    codePath = "DdaMonitor.CheckOverlay; OverlayWindow (own thread, owned popup); Runner.PlaybackAndOverlayTest"
                },
                Fallback = ovPass ? null : FallbackNote
            };
        }
        dda?.Dispose(); ov.Dispose();
    }

    private async Task ResizeTest()
    {
        var sizes = new (int w, int h)[] { (1280, 720), (1600, 900), (1000, 700), (1500, 840), (1280, 800), (1100, 650) };
        SetClientSize(1280, 720, 0, 0);
        _player.StripsEnabled = true;
        _player.ZoomEnabled = true;
        DdaMonitor? dda = null; string? ddaError = null;
        try { dda = new DdaMonitor(_host.Adapter, _host.Monitor) { StripWindow = _host.ClientHwnd }; dda.Start(); } catch (Exception ex) { ddaError = ex.Message; }

        int warm = 10;
        double playSeconds = (warm + _o.ResizeCycles) * 0.1 + 4;
        var play = _player.PlayAsync(playSeconds);
        await Task.Delay(1500);
        for (int i = 0; i < warm; i++) { SetClientSize(sizes[i % sizes.Length].w, sizes[i % sizes.Length].h, 0, 0); await Task.Delay(30); }
        int resizeBefore = _host.ResizeCount;
        var before = Memory();
        var trend = new List<object>();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < _o.ResizeCycles; i++)
        {
            var sz = sizes[i % sizes.Length];
            UI(() =>
            {
                Native.GetWindowRect(_win.Hwnd, out var wr); Native.GetClientRect(_win.Hwnd, out var cr);
                Native.SetWindowPos(_win.Hwnd, IntPtr.Zero, 0, 0, sz.w + wr.Width - cr.Width, sz.h + wr.Height - cr.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            });
            await Task.Delay(40);
            if ((i + 1) % 25 == 0) { var m = Memory(); trend.Add(new { cycle = i + 1, handles = m.handles, gdi = m.gdi, privateMB = R(m.priv / 1048576.0), gpuLocalMB = R(m.gpuLocal / 1048576.0), gpuNonLocalMB = R(m.gpuNonLocal / 1048576.0) }); }
        }
        var after = Memory();
        dda?.Stop();
        var stats = await play;
        int resizes = _host.ResizeCount - resizeBefore;

        double dPriv = (after.priv - before.priv) / 1048576.0, dGpu = (after.gpuLocal - before.gpuLocal) / 1048576.0, dNon = (after.gpuNonLocal - before.gpuNonLocal) / 1048576.0;
        bool gpuMeasured = before.gpuLocal >= 0;
        bool leakPass = dPriv < 10 && (!gpuMeasured || (dGpu < 10 && dNon < 10)) && (int)after.gdi - (int)before.gdi < 10 && (int)after.user - (int)before.user < 10 && _host.ResizeFailures == 0;
        _report.Criteria["resize_no_leak"] = new Criterion
        {
            Status = leakPass ? "PASS" : "FAIL",
            Summary = $"{resizes} swapchain resizes while presenting; private bytes {R(dPriv)} MB, GPU local {R(dGpu)} MB, GPU non-local {R(dNon)} MB, GDI {(int)after.gdi - (int)before.gdi}, USER {(int)after.user - (int)before.user}, handles {after.handles - before.handles}; resize failures {_host.ResizeFailures} (bar: each growth under 10 MB; a leaked buffer set would be 17+ MB per cycle)",
            Measurement = new
            {
                bar = "growth after warm-up and GC under 10 MB private and GPU, no resize failures",
                cycles = _o.ResizeCycles, warmCycles = warm, resizes, seconds = R(sw.Elapsed.TotalSeconds, 1),
                before = new { privateMB = R(before.priv / 1048576.0), gpuLocalMB = R(before.gpuLocal / 1048576.0), gpuNonLocalMB = R(before.gpuNonLocal / 1048576.0), before.gdi, before.user, before.handles },
                after = new { privateMB = R(after.priv / 1048576.0), gpuLocalMB = R(after.gpuLocal / 1048576.0), gpuNonLocalMB = R(after.gpuNonLocal / 1048576.0), after.gdi, after.user, after.handles },
                trend, resizeFailures = _host.ResizeFailures, lastResizeError = _host.LastResizeError,
                gpuMeasureNote = "IDXGIAdapter3.QueryVideoMemoryInfo CurrentUsage (this process); ID3D11Debug live-object report is not available without the Graphics Tools layer",
                codePath = "Runner.ResizeTest, Runner.Memory, host resize path"
            }
        };

        if (dda == null)
        {
            _report.Criteria["resize_no_tear"] = new Criterion { Status = "UNCOVERED", Summary = "Desktop Duplication could not be created: " + ddaError };
        }
        else
        {
            long consistent = dda.StripChecked - dda.StripUnreadable;
            bool tearPass = consistent >= 100 && dda.StripTorn == 0 && stats.PresentFailures == 0;
            _report.Criteria["resize_no_tear"] = new Criterion
            {
                Status = consistent < 100 ? "UNCOVERED" : tearPass ? "PASS" : "FAIL",
                Summary = $"{dda.StripChecked} stable desktop frames of the client area checked during resize, {consistent} readable (both guard-checked strips found in the same frame via the ruler), {dda.StripSizeTransient} of the checked frames showed a presented size different from the current client size: torn (top strip code != bottom strip code) {dda.StripTorn}; frames where a strip could not be validated and no claim is made {dda.StripUnreadable}; frames skipped because the client rect moved during the read {dda.StripUnstable}; present failures {stats.PresentFailures}",
                Measurement = new
                {
                    bar = "no readable frame where the top and bottom present-sequence strips disagree",
                    checkedFrames = dda.StripChecked, presentedSizeDiffersFromClient = dda.StripSizeTransient, readable = consistent, torn = dda.StripTorn, unreadable = dda.StripUnreadable, unstable = dda.StripUnstable,
                    ddaFrames = dda.Frames, updated = dda.UpdatedFrames, errors = dda.Errors, samples = dda.StripSamples,
                    codePath = "DdaMonitor.CheckStrips; ProbeCompositor strips; host resize path synchronous resize"
                },
                Fallback = tearPass ? null : FallbackNote
            };
        }
        dda?.Dispose();
        _player.StripsEnabled = false;
    }

    private static string ContextName(IntPtr c)
    {
        if (c == IntPtr.Zero) return "null";
        foreach (var (v, n) in new[] { (-4, "PER_MONITOR_AWARE_V2"), (-3, "PER_MONITOR_AWARE"), (-2, "SYSTEM_AWARE"), (-1, "UNAWARE"), (-5, "UNAWARE_GDISCALED") })
            if (Native.AreDpiAwarenessContextsEqual(c, (IntPtr)v)) return n;
        return "other 0x" + c.ToString("X");
    }

    // Everything that decides whether WM_DPICHANGED is sent: the process manifest, the awareness context of the thread, the top-level window and the host child window.
    private object DpiDiagnostics() => UI(() =>
    {
        string manifest = "";
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string mp = Path.Combine(d.FullName, "app.manifest");
            if (File.Exists(mp)) { manifest = string.Join(" ", File.ReadAllLines(mp).Where(l => l.Contains("dpiAware", StringComparison.OrdinalIgnoreCase)).Select(l => l.Trim())); break; }
        }
        var mainCtx = Native.GetWindowDpiAwarenessContext(_win.Hwnd); var childCtx = Native.GetWindowDpiAwarenessContext(_host.ClientHwnd);
        return new
        {
            manifestDpiLines = manifest, setProcessDpiAwarenessV2 = Program.DpiSetResult, mainRaw = "0x" + mainCtx.ToString("X"), hostRaw = "0x" + childCtx.ToString("X"), threadRaw = "0x" + Native.GetThreadDpiAwarenessContext().ToString("X"),
            thread = ContextName(Native.GetThreadDpiAwarenessContext()),
            mainWindow = ContextName(mainCtx), mainAwareness = Native.GetAwarenessFromDpiAwarenessContext(mainCtx),
            hostWindow = ContextName(childCtx), hostIsMainWindow = _host.ClientHwnd == _win.Hwnd,
            systemDpi = Native.GetDpiForSystem(), mainDpi = Native.GetDpiForWindow(_win.Hwnd), hostDpi = Native.GetDpiForWindow(_host.ClientHwnd)
        };
    });

    private async Task DpiTest()
    {
        _report.Environment["dpiDiagnostics"] = DpiDiagnostics();
        _win.CountMessages = true;
        int baseDpi = (int)UI(() => Native.GetDpiForWindow(_win.Hwnd));
        var monitors = new List<(IntPtr h, RECT rc, uint dpi)>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT rc, IntPtr d) =>
        {
            Native.GetDpiForMonitor(h, Native.MDT_EFFECTIVE_DPI, out uint dx, out _);
            monitors.Add((h, rc, dx)); return true;
        }, IntPtr.Zero);
        var other = monitors.FirstOrDefault(m => m.dpi != baseDpi && m.h != IntPtr.Zero);
        bool real = other.h != IntPtr.Zero;
        SetClientSize(1280, 720, 100, 100);
        var primary = monitors.First(m => m.h == _host.Monitor);
        _player.StripsEnabled = false;
        var play = _player.PlayAsync(_o.DpiCycles * 2 * 0.6 + 6);
        await Task.Delay(1500);

        int changeBefore = _win.DpiChangedMessages;
        var before = Memory();
        var rows = new List<object>(); int mismatches = 0; int failures0 = _host.ResizeFailures; int rs0 = _host.ResizeCount;
        for (int i = 0; i < _o.DpiCycles * 2; i++)
        {
            // One full 120/96 cycle is warm-up: the first visit to each size allocates buffers once, and only growth after that is a leak.
            if (i == 2) before = Memory();
            bool toOther = i % 2 == 0;
            uint dpiTarget;
            if (real)
            {
                var m = toOther ? other : primary;
                UI(() => Native.SetWindowPos(_win.Hwnd, IntPtr.Zero, m.rc.Left + 100, m.rc.Top + 100, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE));
                dpiTarget = m.dpi;
            }
            else
            {
                dpiTarget = toOther ? 144u : (uint)baseDpi;
                UI(() =>
                {
                    Native.GetWindowRect(_win.Hwnd, out var wr);
                    double k = dpiTarget / (double)Native.GetDpiForWindow(_win.Hwnd);
                    var suggested = new RECT { Left = wr.Left, Top = wr.Top, Right = wr.Left + (int)(wr.Width * k), Bottom = wr.Top + (int)(wr.Height * k) };
                    IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
                    Marshal.StructureToPtr(suggested, p, false);
                    Native.SendMessage(_win.Hwnd, Native.WM_DPICHANGED, (IntPtr)((int)dpiTarget | ((int)dpiTarget << 16)), p);
                    Marshal.FreeHGlobal(p);
                });
            }
            await Task.Delay(400);
            var (wdpi, cw, ch, wmon) = UI(() => { Native.GetClientRect(_host.ClientHwnd, out var c); return (Native.GetDpiForWindow(_win.Hwnd), c.Width, c.Height, Native.MonitorFromWindow(_win.Hwnd, Native.MONITOR_DEFAULTTONEAREST)); });
            bool match = cw == _host.BufferWidth && ch == _host.BufferHeight;
            if (!match) mismatches++;
            if (rows.Count < 40) rows.Add(new { step = i, windowDpi = wdpi, windowMonitor = wmon.ToInt64(), wantedDpi = dpiTarget, hostClient = $"{cw}x{ch}", buffers = $"{_host.BufferWidth}x{_host.BufferHeight}", match });
        }
        var after = Memory();
        var stats = await play;
        _win.CountMessages = false;
        var msgHistogram = _win.Messages.OrderByDescending(kv => kv.Value).Take(25).Select(kv => $"0x{kv.Key:X4}:{kv.Value}").ToList();
        int changes = _win.DpiChangedMessages - changeBefore;
        double dPriv = (after.priv - before.priv) / 1048576.0, dGpu = (after.gpuLocal - before.gpuLocal) / 1048576.0;
        bool pass = mismatches == 0 && _host.ResizeFailures == failures0 && stats.PresentFailures == 0 && dPriv < 10 && (before.gpuLocal < 0 || dGpu < 10) && (changes > 0 || _win.WpfDpiChangedEvents > 0);
        _report.Criteria["dpi_change"] = new Criterion
        {
            Status = !real ? "UNCOVERED" : pass ? "PASS" : "FAIL",
            Summary = (real ? $"real monitor moves ({baseDpi} <-> {other.dpi} dpi)" : $"no second monitor with a different DPI; synthetic WM_DPICHANGED only ({baseDpi} <-> 144)") +
                      $": {changes} WM_DPICHANGED handled, {_host.ResizeCount - rs0} swapchain resizes, buffer/client mismatches {mismatches}, resize failures {_host.ResizeFailures - failures0}, present failures {stats.PresentFailures}, private {R(dPriv)} MB, GPU local {R(dGpu)} MB. " +
                      "Tear during DPI change is not captured: DDA watches only the primary output and the strip check ran in the resize test",
            Measurement = new
            {
                bar = "after every DPI change the swapchain equals the client size; no resize or present failure; no memory growth",
                otherDpiMessages = _win.OtherDpiMessages, getDpiScaledSizeMessages = _win.GetDpiScaledSizeMessages, messageHistogramTop25 = msgHistogram, diagnostics = DpiDiagnostics(), wpfDpiChangedEvents = _win.WpfDpiChangedEvents, mode = real ? "real-monitor-move" : "synthetic-WM_DPICHANGED", steps = _o.DpiCycles * 2, changes, mismatches, rows,
                before = new { privateMB = R(before.priv / 1048576.0), gpuLocalMB = R(before.gpuLocal / 1048576.0) }, after = new { privateMB = R(after.priv / 1048576.0), gpuLocalMB = R(after.gpuLocal / 1048576.0) },
                codePath = "Runner.DpiTest, MainWindow.Hook (WM_DPICHANGED count), host resize path"
            }
        };
    }

    // Visible top-level windows stacked above the overlay at the moment of the first miss, so a covered overlay can be told from a lost one.
    private static string WindowsAbove(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder();
        for (var w = Native.GetWindow(hwnd, 3); w != IntPtr.Zero; w = Native.GetWindow(w, 3))
        {
            if (!Native.IsWindowVisible(w)) continue;
            Native.GetWindowRect(w, out var rc);
            var cls = new System.Text.StringBuilder(128); Native.GetClassName(w, cls, 128);
            var title = new System.Text.StringBuilder(128); Native.GetWindowText(w, title, 128);
            Native.GetWindowThreadProcessId(w, out var pid);
            sb.Append($"[{cls}|{title}|pid {pid}|{rc.Left},{rc.Top},{rc.Right},{rc.Bottom}] ");
        }
        return sb.ToString();
    }
}
