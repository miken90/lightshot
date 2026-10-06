// Ported for Lightshot Windows Port (Phase 7 R1)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Encapsulates the interactive state and operations of a recording area selection:
/// default 720p rectangle, 8-handle resizing, 1 px and 10 px arrow nudges,
/// aspect ratio locking, typed dimensions, window picking, and multi-display confinement.
/// Resolves directly into a RecordingChoice for the recording engine.
/// </summary>
public sealed class RecordingSelectionSession
{
    private EditableSelection _selection;
    private FrozenWindow? _snappedWindow;

    public Rect DisplayBounds { get; }
    public uint DisplayId { get; }

    public EditableSelection Selection => _selection;
    public Rect? CurrentRect => _selection.Rect;
    public AspectRatio Ratio => _selection.Ratio;
    public bool HasSelection => _selection.HasSelection;
    public bool IsDragging => _selection.IsDragging;
    public FrozenWindow? SnappedWindow => _snappedWindow;

    public RecordingSelectionSession(
        Rect displayBounds,
        uint displayId = 1,
        Rect? initialRect = null,
        AspectRatio initialRatio = AspectRatio.Freeform)
    {
        DisplayBounds = displayBounds.Standardized;
        DisplayId = displayId;

        // If an initial rect is provided and valid within bounds, use it;
        // otherwise default to a centered 720p rect.
        Rect startingRect = initialRect ?? RecordingSelectionPainter.GetDefaultRecordingRect(DisplayBounds);
        _selection = new EditableSelection(DisplayBounds, initialRatio, startingRect);
    }

    /// <summary>
    /// Performs an arrow nudge: 1 px default, or 10 px when largeStep (Shift) is true.
    /// Breaks any snapped window back into a free editable rect.
    /// </summary>
    public void Nudge(double dx, double dy, bool largeStep = false)
    {
        _snappedWindow = null;
        RecordingSelectionPainter.Nudge(ref _selection, dx, dy, largeStep);
    }

    /// <summary>
    /// Locks or changes the aspect ratio using Core AspectRatio values.
    /// </summary>
    public void SetRatio(AspectRatio ratio)
    {
        _snappedWindow = null;
        RecordingSelectionPainter.SetRatio(ref _selection, ratio);
    }

    /// <summary>
    /// Sets a typed width in pixels, preserving aspect ratio if locked.
    /// </summary>
    public void SetWidth(double width)
    {
        _snappedWindow = null;
        RecordingSelectionPainter.SetWidth(ref _selection, width);
    }

    /// <summary>
    /// Sets a typed height in pixels, preserving aspect ratio if locked.
    /// </summary>
    public void SetHeight(double height)
    {
        _snappedWindow = null;
        RecordingSelectionPainter.SetHeight(ref _selection, height);
    }

    /// <summary>
    /// Sets typed width and height dimensions.
    /// </summary>
    public void SetSize(double width, double height)
    {
        _snappedWindow = null;
        RecordingSelectionPainter.SetSize(ref _selection, width, height);
    }

    /// <summary>
    /// Resets the selection back to the default centered 720p rectangle.
    /// </summary>
    public void ResetToDefault720p()
    {
        _snappedWindow = null;
        var defaultRect = RecordingSelectionPainter.GetDefaultRecordingRect(DisplayBounds);
        _selection = new EditableSelection(DisplayBounds, AspectRatio.Freeform, defaultRect);
    }

    /// <summary>
    /// Snaps the selection to a specific window. The picked window records its area.
    /// </summary>
    public void PickWindow(FrozenWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _snappedWindow = window;
        _selection.Snap(window.Frame);
    }

    /// <summary>
    /// Snaps the selection to a window id and frame.
    /// </summary>
    public void PickWindow(uint windowId, Rect frame)
    {
        var fw = new FrozenWindow(windowId, frame);
        PickWindow(fw);
    }

    /// <summary>
    /// Confines the current selection to the display with the largest overlap
    /// among the provided active displays.
    /// </summary>
    public void ConfineToDisplay(IEnumerable<DisplayInfo> displays)
    {
        if (_selection.Rect == null) return;
        var confined = RecordingSelectionPainter.ConfineToDisplayWithLargestOverlap(_selection.Rect.Value, displays);
        _selection = new EditableSelection(DisplayBounds, _selection.Ratio, confined);
    }

    // Drag gestures
    public void DragBegan(Point point)
    {
        _snappedWindow = null;
        _selection.DragBegan(point);
    }

    public void DragChanged(Point point, bool forceSquare = false)
    {
        _selection.DragChanged(point, forceSquare);
    }

    public void DragEnded(Point point, bool forceSquare = false)
    {
        _selection.DragEnded(point, forceSquare);
    }

    /// <summary>
    /// Resolves the current selection into a CaptureRegion.
    /// If snapped to a window, returns WindowRegion (which records its area).
    /// If covering the entire display bounds, returns DisplayRegion.
    /// Otherwise returns RectRegion.
    /// </summary>
    public CaptureRegion? ResolveRegion()
    {
        if (!_selection.HasSelection || _selection.Rect == null)
        {
            return null;
        }

        if (_snappedWindow != null)
        {
            return new CaptureRegion.WindowRegion(_snappedWindow.Id, _selection.Rect.Value);
        }

        var r = _selection.Rect.Value.Standardized;
        var b = DisplayBounds.Standardized;

        // If selection covers the full display
        if (Math.Abs(r.MinX - b.MinX) < 1.0 &&
            Math.Abs(r.MinY - b.MinY) < 1.0 &&
            Math.Abs(r.Width - b.Width) < 1.0 &&
            Math.Abs(r.Height - b.Height) < 1.0)
        {
            return new CaptureRegion.DisplayRegion(DisplayId);
        }

        return new CaptureRegion.RectRegion(r);
    }

    /// <summary>
    /// Resolves the session into a complete RecordingChoice for the engine.
    /// </summary>
    public RecordingChoice? ToRecordingChoice(
        RecordingOutputKind output = RecordingOutputKind.Video,
        RecordingOverrides? overrides = null,
        string? microphoneDeviceId = null,
        string? cameraDeviceId = null)
    {
        var region = ResolveRegion();
        if (region == null) return null;

        return new RecordingChoice(
            Region: region,
            Output: output,
            Overrides: overrides ?? RecordingOverrides.None,
            MicrophoneDeviceID: microphoneDeviceId,
            CameraDeviceID: cameraDeviceId);
    }
}
