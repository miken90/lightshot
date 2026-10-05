# Spike C: Compositor Pipeline Report

**Date:** 2026-10-05
**Probe:** `spikes/compositor-probe` (run with `scripts/spike.ps1 -Name c`; writes `artifacts/compositor-probe-result.json`, gitignored)
**Host:** Windows 11 build 26200, NVIDIA GeForce RTX 4060 Laptop GPU, primary DISPLAY1 1920x1080 at 120 dpi (125%), two virtual 1920x1080 monitors at 96 dpi. Refresh measured from `GetFrameStatistics`: 60.0 Hz (`DwmGetCompositionTimingInfo` fails with 0x88980090, informational only).
**Overall status:** **FAIL** on the HwndHost path (3 of 7 criteria pass: overlay, resize leak, resize tear; seek, export, 60 fps and DPI change fail). The D3DImage fallback was not built (see "Decision needed").

## What was built

MF source reader with a DXGI device manager (hardware H.264 decode to NV12 D3D11 textures) -> `VideoProcessorBlt` to BGRA -> one `ProbeCompositor.Render(state, t, source, target)` (D2D: blurred backdrop, shadow, rounded-corner video, zoom transform, cursor sprite) -> flip-model swapchain (`FlipDiscard`, 3 buffers, waitable, latency 1) in a WPF `HwndHost` child HWND. The same `Render` runs on a WARP device into an offscreen texture for the export comparison. The 1440p60 H.264 clip (GOP 30, 2400 frames, 14 MB) is generated at run time by an MF sink writer and not committed.

## Results (one complete run, 2560x1440 client)

| Criterion | Measured | Result |
|---|---|---|
| 60 fps, no drops over 30 s | 1807 presents in 30.02 s; `PresentRefreshCount` span 1843, 36 extra refreshes in a single gap at 0.62 s (playback start-up, source index 2); no gap in the remaining ~29.4 s; 0 source frames skipped, 6 repeated; stats for 1574 of 1807 presents; avg render CPU 16.6 ms, max 23.0 ms | FAIL |
| Seek < 150 ms | sync 0 (scrub): median 72.7, p95 110.8, max 159.9 ms, 1/20 over; sync 1: median 98.9, p95 178, max 222.3, 7/20 over; 0 stamp mismatches in 40 seeks | FAIL |
| Export == preview (delta <= 2) | max channel delta 38, 69,502 channel values over 2 across 12 frames | FAIL |
| Overlay HWND, no flicker/z-order loss | 1064 DDA frames, 0 missing, z-order stress at 8/16/24 s | PASS (see flake note) |
| Resize: no leak | 100 resizes; GPU local 0 MB, GDI 0, USER 0, handles +4, resize failures 0; private bytes -184 MB | PASS |
| Resize: no tear | 208 readable DDA frames, 0 torn; 118 unreadable, no claim made | PASS (see note) |
| DPI change | real moves between 120 and 96 dpi dispatched, buffer == client at every step, 0 failures; but 0 `WM_DPICHANGED` messages and 0 WPF `DpiChanged` events seen | FAIL |

## Method (code paths)

- Present gaps: `PreviewPlayer.PlayTick/CollectStats`, `Runner.PlaybackAndOverlayTest`. Extra refreshes = refresh delta - sync interval * present delta.
- Seek: `Runner.RunSeeks`, `PreviewPlayer.SeekAsync`. Latency = API-call QPC to `SyncQPCTime` of the target present; the 20-bit stamp in the source pixels (`FrameStamp`) is read back from the presented buffer and must equal the target frame.
- Export: `OffscreenExport.Compare`, hardware back-buffer readback vs WARP offscreen render of the same BGRA source bytes.
- Overlay and tear: `DdaMonitor` (Desktop Duplication of the primary output). Overlay uses 15 sample points of a magenta marker; tear uses 16-bit strip codes top and bottom with guard cells, located in the same frame via a cyan ruler column.
- Leak and DPI: `Runner.ResizeTest`, `Runner.DpiTest`; private bytes, `QueryVideoMemoryInfo`, GDI/USER/handle counts after warm-up and GC.

## Findings

- **Seek.** Decode is not the bottleneck (about 1.5 ms per frame, at most 37 ms for 30 frames after the keyframe, GOP 30). Latency is dominated by the gap between the `Present` call and the frame's `SyncQPCTime`, with a floor near 40 ms and a bimodal cluster near 90 ms. The 2560x1440 client exceeds the 1920x1080 primary, so DWM composes a partly off-screen window; this may add pipeline depth and was not isolated. Scrub-mode (sync 0) is close to the bar, vblank-synced seek is not.
- **Export delta.** Frame 0 shows the video interior is pixel-exact; differing pixels (up to 38 on B/G) lie only in the blurred backdrop margin and the shadow band below the video, in a diagonal dither-like pattern. That points at D2D `GaussianBlur`/`Shadow` producing different low bits on NVIDIA than on WARP. An attempt to attribute it by disabling blur or shadow with an environment switch did not change the numbers and is not trusted. Not isolated further.
- **60 fps.** Steady state is clean; the only gap is the first ~0.6 s after playback starts. It is a real miss against a 30 s window.
- **Overlay flake.** Earlier runs on a busy desktop showed 274 missing frames starting at 6.1 s or 16 s. The first-miss dump showed the overlay intact but with a translucent foreign shape over its right edge; the run in this report and several others show 0 missing. The pass is therefore conditional on no external window covering the overlay.
- **Tear.** 0 torn frames, but 187 of the checked frames showed a presented size different from the current client size (stale-size transient while WPF resizes the HwndHost), and 118 frames could not be validated.
- **DPI.** `WM_DPICHANGED` was never delivered to the host window; cause not found (manifest/DPI awareness route not isolated). The DPI tear is UNCOVERED: DDA watches the primary output only.
- **Leak.** Private bytes swing between -193 and +297 MB across runs (GC and WPF noise); the criterion judged on GPU memory, GDI, USER and handles, which were flat. One earlier run showed +104 handles; not reproduced.
- **Flake.** About one run in two aborts with `no frame statistics for present N` during the sync-0 seek pass (`PreviewPlayer.WaitSync`); the probe records the execution error and exits without criteria.

## Decision needed

The D3DImage host (spec fallback) is not built. Seek and 60 fps may be host-related; export delta and DPI are not (they are effect precision and message routing). Options: (1) build the D3DImage host and re-measure seek and 60 fps; (2) accept the HwndHost numbers and move on to a gate decision with these failures recorded.
