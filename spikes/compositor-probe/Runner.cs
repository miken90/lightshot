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
    private readonly D3dHost _host;
    private readonly string _clip;
    private readonly RunOptions _o;
    private readonly double _clipGenSec;
    private readonly ProbeReport _report = new();
    private PreviewPlayer _player = null!;
    private int _syncInterval = 1;
    private double _hz;

    public Runner(App app, MainWindow win, D3dHost host, string clip, RunOptions o, double clipGenSec)
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

    private object SeekRows(List<SeekResult> results, List<(double setPositionMs, double firstFrameMs, double totalMs, int frames)> timings) =>
        results.Select((r, i) => new
        {
            decoder = new { setPositionMs = R(timings[i].setPositionMs, 1), firstFrameMs = R(timings[i].firstFrameMs, 1), totalMs = R(timings[i].totalMs, 1), framesDecoded = timings[i].frames },
            target = r.TargetFrame, framesAfterKeyframe = r.TargetFrame % 30, stamp = r.StampDecoded, ok = r.StampMatches,
            latencyMs = R(r.LatencyMs, 1), decodeReadyMs = R(r.DecodeReadyMs, 1), presentCallMs = R(r.PresentCallMs, 1)
        }).ToList();

    private async Task SeekTest()
    {
        // The stamp sits in a corner of the source; zoom about the cursor can push part of it off the canvas, so the stamp read uses the unzoomed layout.
        _player.ZoomEnabled = false;
        // Same 20 seeks twice: presented with the playback cadence (sync interval 1) and presented immediately (interval 0, scrub behaviour).
        var vsync = await RunSeeks(_syncInterval);
        var now = await RunSeeks(0);
        _player.ZoomEnabled = true;
        string Line(string name, List<SeekResult> r)
        {
            var l = r.Select(x => x.LatencyMs).ToList();
            return $"{name}: median {R(Pct(l, .5), 1)} ms, p95 {R(Pct(l, .95), 1)} ms, max {R(l.Max(), 1)} ms, over 150 ms {l.Count(x => x >= 150)}/{l.Count}, stamp mismatches {r.Count(x => !x.StampMatches)}";
        }
        var latNow = now.results.Select(r => r.LatencyMs).ToList();
        bool pass = now.results.All(r => r.StampMatches) && latNow.Max() < 150;
        _report.Criteria["seek"] = new Criterion
        {
            Status = pass ? "PASS" : "FAIL",
            Summary = "20 seeks per mode (bar < 150 ms every seek). " + Line("present at vblank (sync 1)", vsync.results) + "; " + Line("present immediately (sync 0)", now.results),
            Measurement = new
            {
                bar = "API call QPC -> SyncQPCTime of the present of the target frame, every seek < 150 ms, stamp read from the pre-Present back buffer equals the target. Pass is judged on the scrub (sync 0) mode",
                syncInterval1 = SeekRows(vsync.results, vsync.timings),
                syncInterval0 = SeekRows(now.results, now.timings),
                codePath = "PreviewPlayer.SeekAsync, StampFromCanvas, WaitSync; MfDecoder.SeekTo"
            },
            Fallback = pass ? null : "D3DImage host (not triggered unless HwndHost fails)"
        };
    }

    private async Task ExportTest()
    {
        int[] frames = { 0, 30, 90, 180, 240, 300, 480, 900, 1234, 1777, 2222, 2399 };
        var rows = new List<CompareResult>();
        string adapter;
        using (var export = new OffscreenExport())
        {
            adapter = export.AdapterName;
            foreach (int f in frames)
            {
                var cf = await _player.CaptureFrameAsync(f, strips: true);
                rows.Add(export.Compare(cf));
            }
        }
        int max = rows.Max(r => r.MaxDelta);
        _report.Criteria["export_equals_preview"] = new Criterion
        {
            Status = max <= 2 ? "PASS" : "FAIL",
            Summary = $"{rows.Count} frames at {_host.BufferWidth}x{_host.BufferHeight}: max channel delta {max} (bar <= 2); {rows.Sum(r => r.OverTwo)} channel values over 2",
            Measurement = new
            {
                bar = "presented back-buffer pixels (hardware GPU) vs the same Render() on WARP into an offscreen texture, same BGRA source bytes",
                exportAdapter = adapter, previewAdapter = _host.AdapterName,
                frames = rows.Select(r => new { r.Frame, t = R(r.T, 3), r.MaxDelta, r.OverTwo, r.Zoomed, r.Worst }),
                codePath = "PreviewPlayer.CaptureFrameAsync, OffscreenExport.Compare"
            },
            Fallback = max <= 2 ? null : "none defined in spec; investigate effect parity"
        };
    }

    private async Task PlaybackAndOverlayTest()
    {
        _player.StripsEnabled = false;
        var ov = new OverlayWindow(_win.Hwnd);
        var cr = UI(() => Native.ClientScreenRect(_host.Hwnd));
        ov.ShowAt(cr.Left + 200, cr.Top + 200);
        DdaMonitor? dda = null; string? ddaError = null;
        try { dda = new DdaMonitor(_host.Adapter, _host.Monitor) { OverlayRect = () => ov.ScreenRect, OverlayVisible = () => ov.Visible, MissContext = () => WindowsAbove(ov.Hwnd), MissDumpPath = Path.Combine(Path.GetDirectoryName(_clip)!, "overlay-first-miss-360x96.bgra") }; dda.Start(); }
        catch (Exception ex) { ddaError = ex.Message; }
        await Task.Delay(500);

        var zEvents = new List<double>();
        int nextEvent = 0;
        double[] eventAt = { 8, 16, 24 };
        var stats = await _player.PlayAsync(_o.PlaySeconds, el =>
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

        // ---- 60 fps
        var s = stats.Samples.OrderBy(x => x.PresentCount).ToList();
        long extra = 0, gapEvents = 0; uint maxGap = 0; var gapAt = new List<object>();
        for (int i = 1; i < s.Count; i++)
        {
            long dPc = s[i].PresentCount - s[i - 1].PresentCount;
            long dRc = (long)s[i].PresentRefreshCount - s[i - 1].PresentRefreshCount;
            long over = dRc - dPc * _syncInterval;
            if (over > 0) { extra += over; gapEvents++; maxGap = (uint)Math.Max(maxGap, dRc); gapAt.Add(new { atSec = R((s[i].SyncQpc - s[0].SyncQpc) / (double)Stopwatch.Frequency, 2), extraRefreshes = over, sourceIndex = s[i].SourceIndex }); }
        }
        var rcSpan = s.Count > 1 ? s[^1].PresentRefreshCount - s[0].PresentRefreshCount : 0;
        double[] fm = stats.Samples.Select(x => x.RenderMs).ToArray();
        bool hzOk = Math.Abs(_hz / _syncInterval - 60.0) < 0.6;
        bool fpsPass = hzOk && extra == 0 && stats.SkippedSource == 0 && stats.PresentFailures == 0 && s.Count >= stats.Presents * 0.99;
        string fpsStatus = !hzOk ? "UNCOVERED" : fpsPass ? "PASS" : "FAIL";
        _report.Criteria["preview_60fps"] = new Criterion
        {
            Status = fpsStatus,
            Summary = !hzOk ? $"display refresh {R(_hz, 2)} Hz is not a multiple of 60 Hz; PresentRefreshCount gaps do not map to 60 fps"
                : $"{stats.Presents} presents in {R(stats.DurationSec, 2)} s at {R(stats.Presents / stats.DurationSec, 2)}/s ({R(_hz, 2)} Hz, sync interval {_syncInterval}); " +
                  $"PresentRefreshCount extra refreshes {extra} in {gapEvents} gap events (max gap {maxGap}); source frames skipped {stats.SkippedSource}, repeated {stats.Repeats} (decode-starved {stats.DecodeStarved}); stats recorded for {s.Count}/{stats.Presents} presents",
            Measurement = new
            {
                bar = "no PresentRefreshCount gap beyond the sync interval over 30 s, no skipped source frame",
                seconds = R(stats.DurationSec, 3), presents = stats.Presents, statsSamples = s.Count, refreshSpan = rcSpan,
                extraRefreshes = extra, gapEvents, gaps = gapAt, zOrderStressAtSec = zEvents, maxRefreshDelta = maxGap, sourceSkipped = stats.SkippedSource, repeats = stats.Repeats,
                decodeStarved = stats.DecodeStarved, presentFailures = stats.PresentFailures, statsDisjoint = stats.StatsDisjoint, statsUnavailable = stats.StatsUnavailable,
                renderCpuMsAvg = R(stats.RenderMsSum / Math.Max(1, stats.Presents), 3), renderCpuMsMax = R(stats.RenderMsMax, 3),
                firstSource = stats.FirstSource, lastSource = stats.LastSource, canvas = $"{_host.BufferWidth}x{_host.BufferHeight}",
                codePath = "PreviewPlayer.PlayTick/CollectStats; Runner.PlaybackAndOverlayTest gap analysis"
            },
            Fallback = fpsStatus == "FAIL" ? "D3DImage host" : null
        };

        // ---- overlay
        if (dda == null)
        {
            _report.Criteria["overlay_airspace"] = new Criterion { Status = "UNCOVERED", Summary = "Desktop Duplication could not be created: " + ddaError };
        }
        else
        {
            bool pass = dda.OverlayChecked >= 1000 && dda.OverlayMissing == 0 && dda.Errors == 0;
            _report.Criteria["overlay_airspace"] = new Criterion
            {
                Status = dda.OverlayChecked < 1000 ? "UNCOVERED" : pass ? "PASS" : "FAIL",
                Summary = $"DDA delivered {dda.UpdatedFrames} desktop frames; overlay region checked in {dda.OverlayChecked}, missing in {dda.OverlayMissing} (15 sample points must all read the marker colour); DDA errors {dda.Errors}; z-order stress at {string.Join(", ", zEvents)} s",
                Measurement = new
                {
                    bar = "zero frames with the overlay marker missing over the 30 s run",
                    ddaFrames = dda.Frames, updatedFrames = dda.UpdatedFrames, checkedFrames = dda.OverlayChecked, missing = dda.OverlayMissing,
                    missingAtSec = dda.OverlayMissingAtSec, firstMissWindowsAbove = dda.MissContextText, errors = dda.Errors, lastError = dda.LastError, timeouts = dda.Timeouts, zOrderStressAtSec = zEvents,
                    codePath = "DdaMonitor.CheckOverlay; OverlayWindow (own thread, owned popup); Runner.PlaybackAndOverlayTest"
                },
                Fallback = pass ? null : "D3DImage host (removes the airspace restriction)"
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
        try { dda = new DdaMonitor(_host.Adapter, _host.Monitor) { StripWindow = _host.Hwnd }; dda.Start(); } catch (Exception ex) { ddaError = ex.Message; }

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
        bool leakPass = dPriv < 10 && (!gpuMeasured || (dGpu < 10 && dNon < 10)) && after.gdi - before.gdi < 10 && after.user - before.user < 10 && _host.ResizeFailures == 0;
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
                codePath = "Runner.ResizeTest, Runner.Memory, D3dHost.HandleSize"
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
                    codePath = "DdaMonitor.CheckStrips; ProbeCompositor strips; D3dHost.HandleSize synchronous resize"
                },
                Fallback = tearPass ? null : "D3DImage host"
            };
        }
        dda?.Dispose();
        _player.StripsEnabled = false;
    }

    private async Task DpiTest()
    {
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
            var (wdpi, cw, ch) = UI(() => { Native.GetClientRect(_host.Hwnd, out var c); return (Native.GetDpiForWindow(_win.Hwnd), c.Width, c.Height); });
            bool match = cw == _host.BufferWidth && ch == _host.BufferHeight;
            if (!match) mismatches++;
            if (rows.Count < 40) rows.Add(new { step = i, windowDpi = wdpi, hostClient = $"{cw}x{ch}", buffers = $"{_host.BufferWidth}x{_host.BufferHeight}", match });
        }
        var after = Memory();
        var stats = await play;
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
                otherDpiMessages = _win.OtherDpiMessages, wpfDpiChangedEvents = _win.WpfDpiChangedEvents, mode = real ? "real-monitor-move" : "synthetic-WM_DPICHANGED", steps = _o.DpiCycles * 2, changes, mismatches, rows,
                before = new { privateMB = R(before.priv / 1048576.0), gpuLocalMB = R(before.gpuLocal / 1048576.0) }, after = new { privateMB = R(after.priv / 1048576.0), gpuLocalMB = R(after.gpuLocal / 1048576.0) },
                codePath = "Runner.DpiTest, MainWindow.Hook (WM_DPICHANGED count), D3dHost.HandleSize"
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
