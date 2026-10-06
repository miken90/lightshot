// Ported for Lightshot Windows Port (Phase 7 R1)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// High-level API coordinating recording chrome across the pre-capture and active capture phases:
/// 1. Selection overlay resolving to a RecordingChoice.
/// 2. Live recording frame window with pulsing 3 px red border and dimming.
/// 3. Click-through countdown window (3..2..1) dismissible via Esc or Cancel().
/// </summary>
public sealed class RecordingChromeCoordinator : IDisposable
{
    private readonly List<OverlayWindow> _overlayWindows = new();
    private RecordingFrameWindow? _frameWindow;
    private CountdownWindow? _countdownWindow;
    private TaskCompletionSource<RecordingChoice?>? _selectionTcs;
    private bool _disposed;

    public RecordingFrameWindow? FrameWindow => _frameWindow;
    public CountdownWindow? CountdownWindow => _countdownWindow;

    // =========================================================================
    // 1. Selection API: show selection -> RecordingChoice
    // =========================================================================

    /// <summary>
    /// Displays the recording selection overlay across active displays and waits
    /// for the user to confirm (Return) or cancel (Escape).
    /// Returns the resolved RecordingChoice or null on cancel.
    /// </summary>
    public Task<RecordingChoice?> ShowSelectionAsync(
        CaptureRegion? initial,
        RecordingDefaults defaults,
        FrozenScreen? frozen = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _selectionTcs = new TaskCompletionSource<RecordingChoice?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var displays = DisplayTopology.GetDisplays();
        var primary = System.Linq.Enumerable.FirstOrDefault(displays, d => d.IsPrimary) ?? (displays.Count > 0 ? displays[0] : null);
        Rect primaryBounds = primary?.Bounds ?? new Rect(0, 0, 1920, 1080);

        // Pre-calculate starting rect: initial rect or default 720p rect
        Rect startRect;
        if (initial is CaptureRegion.RectRegion r)
        {
            startRect = r.Rect;
        }
        else if (initial is CaptureRegion.WindowRegion w)
        {
            startRect = w.Frame;
        }
        else
        {
            startRect = RecordingSelectionPainter.GetDefaultRecordingRect(primaryBounds);
        }

        // Clean up any existing overlay windows
        TeardownSelectionOverlays();

        var monitors = OverlayHost.DiscoverMonitors();
        uint id = 1;
        foreach (var mon in monitors)
        {
            var win = new OverlayWindow(id++, mon.Bounds)
            {
                Mode = OverlayMode.Recording,
                IsAdjustable = true
            };

            if (frozen != null)
            {
                var disp = System.Linq.Enumerable.FirstOrDefault(frozen.Displays, d =>
                    Math.Abs(d.Frame.MinX - mon.Bounds.MinX) < 2 &&
                    Math.Abs(d.Frame.MinY - mon.Bounds.MinY) < 2);
                if (disp != null)
                {
                    win.Backdrop = disp.Image;
                }
            }

            win.DragBeganOnThisWindow = active =>
            {
                foreach (var w in _overlayWindows)
                {
                    if (!ReferenceEquals(w, active))
                    {
                        w.ClearSelection();
                    }
                }
            };

            win.SelectionCompleted = region =>
            {
                TeardownSelectionOverlays();
                if (region != null)
                {
                    _selectionTcs.TrySetResult(new RecordingChoice(region, RecordingOutputKind.Video));
                }
                else
                {
                    _selectionTcs.TrySetResult(null);
                }
            };

            // Set initial selection on matching monitor
            if (mon.Bounds.Intersection(startRect) != null)
            {
                win.SetSelection(startRect, true);
            }

            win.Show();
            _overlayWindows.Add(win);
        }

        if (_overlayWindows.Count > 0)
        {
            ForegroundGrant.GrantForeground(_overlayWindows[0].Handle);
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                TeardownSelectionOverlays();
                _selectionTcs.TrySetResult(null);
            });
        }

        return _selectionTcs.Task;
    }

    private void TeardownSelectionOverlays()
    {
        foreach (var win in _overlayWindows)
        {
            try
            {
                win.Hide();
                win.Dispose();
            }
            catch { }
        }
        _overlayWindows.Clear();
    }

    // =========================================================================
    // 2. Frame API: show frame for rect / region
    // =========================================================================

    /// <summary>
    /// Shows the pulsing red frame window around the given capture region.
    /// </summary>
    public void ShowFrame(CaptureRegion region, bool dimsOutside = true, bool isPaused = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var displays = DisplayTopology.GetDisplays();
        var bestDisplay = System.Linq.Enumerable.FirstOrDefault(displays, d => d.IsPrimary) ?? (displays.Count > 0 ? displays[0] : null);

        if (region is CaptureRegion.RectRegion r)
        {
            bestDisplay = DisplayMath.FindLargestOverlap(displays, r.Rect) ?? bestDisplay;
        }
        else if (region is CaptureRegion.WindowRegion w)
        {
            bestDisplay = DisplayMath.FindLargestOverlap(displays, w.Frame) ?? bestDisplay;
        }

        _frameWindow ??= new RecordingFrameWindow(bestDisplay?.Bounds);
        _frameWindow.Show(region, dimsOutside, isPaused);
    }

    /// <summary>
    /// Shows the pulsing red frame window around the given global rectangle.
    /// </summary>
    public void ShowFrame(Rect rect, bool dimsOutside = true, bool isPaused = false)
    {
        ShowFrame(new CaptureRegion.RectRegion(rect), dimsOutside, isPaused);
    }

    /// <summary>
    /// Updates pause state on the recording frame (steady when paused, pulsing when active).
    /// </summary>
    public void SetFramePaused(bool isPaused)
    {
        _frameWindow?.SetPaused(isPaused);
    }

    /// <summary>
    /// Hides and dismisses the recording frame window.
    /// </summary>
    public void HideFrame()
    {
        _frameWindow?.Hide();
    }

    // =========================================================================
    // 3. Countdown API: countdown with cancel
    // =========================================================================

    /// <summary>
    /// Runs the 3-2-1 countdown window. Returns true if zero was reached, false on cancel.
    /// </summary>
    public async Task<bool> RunCountdownAsync(
        int seconds = 3,
        bool playSounds = true,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _countdownWindow ??= new CountdownWindow();
        return await _countdownWindow.RunCountdownAsync(seconds, playSounds, cancellationToken);
    }

    /// <summary>
    /// Cancels any currently active countdown window.
    /// </summary>
    public void CancelCountdown()
    {
        _countdownWindow?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TeardownSelectionOverlays();

        _frameWindow?.Dispose();
        _frameWindow = null;

        _countdownWindow?.Dispose();
        _countdownWindow = null;
    }
}
