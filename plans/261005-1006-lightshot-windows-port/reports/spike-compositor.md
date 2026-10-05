# Spike C: Compositor Pipeline Report

**Date:** 2026-10-05
**Probe:** `spikes/compositor-probe` (`scripts/spike.ps1 -Name c [-ExtraArgs --host,d3dimage]`; writes `artifacts/compositor-probe-result.json` or `...-d3dimage.json`, gitignored)
**Host machine:** Windows 11 build 26200, NVIDIA GeForce RTX 4060 Laptop GPU, primary DISPLAY1 1920x1080 at 120 dpi (125%), two virtual 1920x1080 monitors at 96 dpi. Refresh measured at 60.0 Hz.
**Overall status:** **FAIL, with a host recommendation.** Three complete runs per host. Neither host meets the seek bar (every seek under 150 ms) at a 2560x1440 client. HwndHost passes the other six criteria in most runs; D3DImage also fails 60 fps in every run. Recommendation: HwndHost.

## What was built

MF source reader with a DXGI device manager (hardware H.264 decode to NV12 D3D11 textures) -> `VideoProcessorBlt` to BGRA -> one `ProbeCompositor.Render(state, t, source, target)` (D2D: blurred backdrop, shadow, rounded-corner video, zoom transform, cursor sprite) -> flip-model swapchain (`FlipDiscard`, 3 buffers, waitable, latency 1) in a WPF `HwndHost` child HWND. The same `Render` runs on a WARP device into an offscreen texture for the export comparison. The 1440p60 H.264 clip (GOP 30, 2400 frames, 14 MB) is generated at run time by an MF sink writer and not committed.

## Results: three complete runs per host (2560x1440 client, defaults: 30 s window after a 2 s warm-up, 100 resize cycles, 10 DPI cycles)

Run 1/2/3 are separate complete executions of the whole probe.

| Criterion | HwndHost (runs 1/2/3) | D3DImage (runs 1/2/3) |
|---|---|---|
| Seek < 150 ms, every seek, 20 per mode | FAIL/FAIL/FAIL. Present-immediately max 163.3/159.9/166.3 ms (1/20 over each run); vblank max 149.8/150.9/148.4 ms (0, 1, 0 over). Medians 88-94 ms and 47-49 ms. 0 stamp mismatches | FAIL/FAIL/FAIL. Present-immediately max 225/255.8/165.6 ms (5, 1, 1 over); vblank max 167.2/138.8/143.5 ms (1, 0, 0 over). 0 stamp mismatches |
| 60 fps, no drops (30 s window) | PASS/FAIL/FAIL. 1800, 1799, 1800 presents, 0 refresh gaps, 0 stalls, 0 unexplained skips. Run 2: 1 source frame skipped. Run 3: 117 skipped and 117 repeated source frames | FAIL/FAIL/FAIL. 58.3-58.4 presents/s; 33-47 unexplained skipped presents; 7-16 display stalls (max gap 34-404 ms); 48-56 source frames skipped |
| Export == preview (WARP vs WARP, same `Render`) | PASS x3, max delta 0 | PASS x3, max delta 0 |
| Overlay over host, no missing frames | PASS/FAIL/PASS. Run 2: 259 of 1187 frames missing from 2.7 s to 6.6 s; the first-miss window dump lists a foreign window (`Engine`, pid 5220) above the overlay | PASS x3 (0 of about 1880 frames missing) |
| Resize: no leak (100 cycles) | PASS x3 (GPU local 0 MB, GDI 0, handles -3 to +1) | PASS x3 (GPU local 0 to 5 MB, handles +3 to +4) |
| Resize: no tear | PASS x3 (0 torn; 204-215 readable frames) | PASS x3 (0 torn; 479-483 readable frames) |
| DPI change (real moves 120 <-> 96) | PASS x3 (20 `WM_DPICHANGED`, 0 mismatches, GPU +6.25 MB after one warm-up cycle) | PASS x3 (20 `WM_DPICHANGED`, 0 mismatches) |

Diagnostic, not a criterion: hardware GPU preview vs WARP shows a max channel delta of 38 on both hosts. The WARP-vs-WARP delta is 0, so the effect chain is deterministic and the 38 is NVIDIA D2D blur/shadow rounding, which would show up as a preview/export difference only if export were rendered on a different adapter.

D3DImage has no frame statistics, so its seek and 60 fps are judged from desktop duplication alone. HwndHost seek is reported from both desktop duplication and frame statistics (frame-statistics max 151-178 ms).

## Old vs new (the single HwndHost run recorded earlier vs the runs above)

| Criterion | Old (one run) | New |
|---|---|---|
| 60 fps | FAIL: 36 extra refreshes in one gap at 0.62 s (start-up) | The start-up gap (about 40 extra refreshes in the first 2 s) is reported separately and not judged. PASS 1 of 3 runs |
| Seek | FAIL: sync 0 max 159.9, sync 1 max 222.3 ms (7/20 over) | FAIL: sync 1 max 148-151 ms now (0-1/20 over), sync 0 max 160-166 ms (1/20 over). Latency now measured from the desktop frame carrying the stamp |
| Export | FAIL: GPU vs WARP delta 38 | PASS: both sides on WARP, delta 0; the GPU delta is kept as a diagnostic |
| DPI | FAIL: 0 `WM_DPICHANGED` | PASS: see the cause below |
| Leak | PASS | PASS (an unsigned-subtraction bug in the leak check had made it fail once the USER count dropped by 1; fixed) |
| "no frame statistics" abort | About 1 run in 2 | 0 aborts in 6 complete runs; a missing statistic now marks the seek unmeasured instead of aborting |

## Method (code paths)

- Present gaps and 60 fps: `PreviewPlayer.PlayTick/CollectStats`, `Runner.PlaybackAndOverlayTest`, `Runner.AnalyzeDda`. The window starts 2 s after the first present; earlier gaps are reported as start-up. A desktop frame stall is a gap longer than (code advance + 0.5) periods; an unexplained skip is a code advance larger than the presents accumulated in that frame.
- Seek: `Runner.SeekTest`, `PreviewPlayer.SeekAsync`. Latency is the API-call QPC to the `LastPresentTime` of the first desktop frame whose strip code equals the target frame; the 20-bit stamp read back from the presented buffer must equal the target.
- Export: `OffscreenExport.Compare/Diff`, `Runner.ExportTest` (WARP preview vs fresh WARP export, plus a GPU diagnostic and a blur/shadow isolation run through `RenderState.NoBlur/NoShadow`).
- Overlay and tear: `DdaMonitor` (Desktop Duplication of the primary output), 15 magenta sample points, strip codes with guard cells.
- Leak and DPI: `Runner.ResizeTest`, `Runner.DpiTest` (one full 120/96 cycle of warm-up before the memory baseline); private bytes, `QueryVideoMemoryInfo`, GDI/USER/handles.
- Hosts: `D3dHost` (HwndHost, flip-model swapchain, waitable) and `D3dImageHost` (D3D11 shared textures opened as D3D9Ex surfaces, `D3DImage.SetBackBuffer`, paced by a `DwmFlush` thread), both behind `IPresentHost`.

## Findings

- **DPI cause (proven).** `scripts/spike.ps1` ran the probe with `dotnet exec <dll>`, so the process took the awareness of `dotnet.exe` (per-monitor v1, with `__COMPAT_LAYER=HighDpiAware`) and ignored the probe's `app.manifest`. In that state `SetProcessDpiAwarenessContext(PerMonitorV2)` fails with error 5 and no `WM_DPICHANGED` arrives. The script now starts the apphost exe, the process context is 0x22 (PerMonitorV2) and every move delivers `WM_DPICHANGED`. This was a probe launch setting, not a platform limit.
- **Seek.** Decode is not the bottleneck (about 1.5 ms per frame). With the 2560x1440 client wider than the 1920x1080 primary, DWM composes a partly off-screen window. The HwndHost misses the bar by a few ms in about one run in two; D3DImage misses it by more (up to 256 ms in scrub mode), which fits its extra UI-thread commit step. A deeper cause was not isolated.
- **D3DImage 60 fps.** It stays at about 58.4 presents/s with source and display skips, even though every frame is committed. The replaced-before-commit counter is small (5-11), so the loss is mostly in the UI-thread commit and DWM pickup path. Not isolated further; it is a measured fail.
- **HwndHost 60 fps.** Run 1 is fully clean. Run 2 skipped 1 source frame. Run 3 skipped and repeated 117 source frames (decode-starved 0) while the display stayed clean; the cause is not isolated and the run is recorded as FAIL.
- **Overlay.** Run 2 on HwndHost failed because a foreign window (`Engine`, another process) sat above the overlay; that is a desktop condition, not a host fault, but it is a real failure of the measured run.
- **Export.** Before the change the delta of 38 was only in the blurred margin and the shadow band. WARP-vs-WARP through the same `Render` is exact, so the code path is deterministic.
- **Resize tear.** 0 torn frames on both hosts. HwndHost frames with a presented size different from the client (about 190 of the checked frames) show the stale-size transient while WPF resizes; D3DImage shows about 70.
- **Not covered.** Tear during DPI change: desktop duplication watches the primary output only.

## Decision

HwndHost is the better host: it passes the DPI, export, leak, tear and (in most runs) overlay criteria and is clean on 60 fps in one run of three, while D3DImage fails 60 fps every run and is slower on seek. Neither host meets the 150 ms seek bar on this machine at 2560x1440. Whether to accept that, shrink the client, or investigate the compose path further is for the spike gate.
