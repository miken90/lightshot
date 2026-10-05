using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

public enum OverlayMode
{
    Region,
    Window
}

/// <summary>
/// A raw Win32 topmost overlay window covering one physical monitor.
/// Integrates DirectComposition, capture exclusion, and keyboard/mouse interaction.
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    public const int MIN_SELECTION_SIDE = 4;

    private static readonly List<Win32Window.WndProc> s_pinnedWndProcs = new();
    private readonly Win32Window.WndProc _wndProc;
    private readonly string _className;
    private readonly IntPtr _hBrushBlack;
    private DCompSurface? _dcomp;
    private bool _disposed;

    public IntPtr Handle { get; private set; }
    public Rect MonitorBounds { get; }
    public uint MonitorId { get; }
    public CapturedImage? Backdrop { get; set; }

    public OverlayMode Mode { get; set; } = OverlayMode.Region;
    public bool IsAdjustable { get; set; }
    private EditableSelection _editable;
    public EditableSelection? Editable => _editable.HasSelection ? _editable : null;

    public IReadOnlyList<FrozenWindow> CandidateWindows { get; set; } = [];
    public FrozenWindow? HoveredWindow { get; private set; }

    public Rect? CurrentSelection { get; private set; }
    public bool ShowsControls { get; private set; }
    public bool IsDragging { get; private set; }

    private Point? _dragStart;
    private EditableSelection? _beforeRedraw;

    // Events forwarded to OverlayHost
    public Action<OverlayWindow>? DragBeganOnThisWindow { get; set; }
    public Action<CaptureRegion?>? SelectionCompleted { get; set; }
    public Action<int>? KeyReceivedForTesting { get; set; }

    public OverlayWindow(uint monitorId, Rect monitorBounds)
    {
        MonitorId = monitorId;
        MonitorBounds = monitorBounds;

        _wndProc = WndProc;
        lock (s_pinnedWndProcs)
        {
            s_pinnedWndProcs.Add(_wndProc);
        }
        _className = $"LightshotOverlayWindow_{Guid.NewGuid():N}";
        _hBrushBlack = CreateSolidBrush(0x000000);

        IntPtr hInst = Win32Window.GetModuleHandleW(null);
        var wc = new Win32Window.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Window.WNDCLASSEXW>(),
            style = 3, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInst,
            hbrBackground = _hBrushBlack,
            lpszClassName = _className
        };
        Win32Window.RegisterClassExW(ref wc);

        int x = (int)Math.Round(monitorBounds.MinX);
        int y = (int)Math.Round(monitorBounds.MinY);
        int w = (int)Math.Round(monitorBounds.Width);
        int h = (int)Math.Round(monitorBounds.Height);

        Handle = Win32Window.CreateWindowExW(
            Win32Window.WS_EX_TOPMOST | Win32Window.WS_EX_TOOLWINDOW,
            _className,
            "LightshotOverlay",
            Win32Window.WS_POPUP,
            x, y, w, h,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (Handle != IntPtr.Zero)
        {
            // Apply capture exclusion to live chrome
            Win32Window.SetCaptureExclusion(Handle, true);

            // Initialize DirectComposition surface
            _dcomp = new DCompSurface(Handle);
        }

        _editable = new EditableSelection(monitorBounds);
    }

    public void Show()
    {
        if (Handle == IntPtr.Zero) return;
        Win32Window.ShowWindow(Handle, 5 /* SW_SHOW */);
        Win32Window.UpdateWindow(Handle);
        _dcomp?.Commit();
    }

    public void Hide()
    {
        if (Handle == IntPtr.Zero) return;
        Win32Window.ShowWindow(Handle, 0 /* SW_HIDE */);
        _dcomp?.Commit();
    }

    public void Invalidate()
    {
        if (Handle != IntPtr.Zero)
        {
            Win32Window.InvalidateRect(Handle, IntPtr.Zero, false);
        }
    }

    public void SetSelection(Rect? globalRect, bool settled)
    {
        CurrentSelection = globalRect;
        ShowsControls = settled && globalRect.HasValue;
        if (globalRect.HasValue)
        {
            _editable = new EditableSelection(MonitorBounds, AspectRatio.Freeform, globalRect.Value);
        }
        else
        {
            _editable = new EditableSelection(MonitorBounds);
        }
        Invalidate();
    }

    public void ClearSelection()
    {
        CurrentSelection = null;
        ShowsControls = false;
        IsDragging = false;
        _editable = new EditableSelection(MonitorBounds);
        Invalidate();
    }

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        switch (uMsg)
        {
            case Win32Window.WM_PAINT:
            {
                IntPtr hdc = Win32Window.BeginPaint(hWnd, out var ps);
                int clientW = (int)MonitorBounds.Width;
                int clientH = (int)MonitorBounds.Height;

                if (Mode == OverlayMode.Region)
                {
                    SelectionPainter.Paint(
                        hdc, clientW, clientH, MonitorBounds,
                        CurrentSelection, ShowsControls, Backdrop);
                }
                else
                {
                    WindowHoverPainter.Paint(
                        hdc, clientW, clientH, MonitorBounds,
                        HoveredWindow, Backdrop);
                }

                Win32Window.EndPaint(hWnd, ref ps);
                _dcomp?.Commit();
                return IntPtr.Zero;
            }

            case Win32Window.WM_ERASEBKGND:
                return (IntPtr)1; // Avoid flickering

            case Win32Window.WM_KEYDOWN:
            {
                int vk = (int)wParam;
                KeyReceivedForTesting?.Invoke(vk);
                HandleKeyDown(vk);
                return IntPtr.Zero;
            }

            case Win32Window.WM_MOUSEMOVE:
            {
                int lx = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int ly = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                HandleMouseMove(new Point(MonitorBounds.MinX + lx, MonitorBounds.MinY + ly));
                return IntPtr.Zero;
            }

            case Win32Window.WM_LBUTTONDOWN:
            {
                int lx = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int ly = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                HandleMouseDown(new Point(MonitorBounds.MinX + lx, MonitorBounds.MinY + ly));
                return IntPtr.Zero;
            }

            case Win32Window.WM_LBUTTONUP:
            {
                int lx = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int ly = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
                HandleMouseUp(new Point(MonitorBounds.MinX + lx, MonitorBounds.MinY + ly));
                return IntPtr.Zero;
            }

            case Win32Window.WM_SETCURSOR:
                SetCustomCursor();
                return (IntPtr)1;
        }

        return Win32Window.DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    private void HandleMouseDown(Point globalPt)
    {
        if (Mode == OverlayMode.Window)
        {
            if (HoveredWindow != null)
            {
                SelectionCompleted?.Invoke(new CaptureRegion.WindowRegion(HoveredWindow.Id, HoveredWindow.Frame));
            }
            return;
        }

        DragBeganOnThisWindow?.Invoke(this);
        _dragStart = globalPt;

        if (IsAdjustable && _editable.HasSelection)
        {
            var kind = _editable.DetermineDragKind(globalPt);
            if (kind is EditableSelection.DragKind.Draw)
            {
                _beforeRedraw = _editable;
                _editable = new EditableSelection(MonitorBounds);
                _editable.DragBegan(globalPt);
                CurrentSelection = _editable.Rect;
                ShowsControls = false;
                IsDragging = true;
            }
            else
            {
                _editable.DragBegan(globalPt);
                IsDragging = true;
                ShowsControls = false;
            }
        }
        else
        {
            _editable = new EditableSelection(MonitorBounds);
            _editable.DragBegan(globalPt);
            CurrentSelection = _editable.Rect;
            ShowsControls = false;
            IsDragging = true;
        }

        Invalidate();
    }

    private void HandleMouseMove(Point globalPt)
    {
        if (Mode == OverlayMode.Window)
        {
            FrozenWindow? found = null;
            foreach (var win in CandidateWindows)
            {
                if (win.Frame.Contains(globalPt))
                {
                    found = win;
                    break;
                }
            }

            if (found != HoveredWindow)
            {
                HoveredWindow = found;
                Invalidate();
            }
            return;
        }

        if (IsDragging)
        {
            _editable.DragChanged(globalPt);
            CurrentSelection = _editable.Rect;
            Invalidate();
        }
    }

    private void HandleMouseUp(Point globalPt)
    {
        if (Mode == OverlayMode.Window) return;

        if (IsDragging)
        {
            _editable.DragEnded(globalPt);
            CurrentSelection = _editable.Rect;
            IsDragging = false;

            bool hasRealArea = CurrentSelection.HasValue &&
                               CurrentSelection.Value.Width >= MIN_SELECTION_SIDE &&
                               CurrentSelection.Value.Height >= MIN_SELECTION_SIDE;

            if (!IsAdjustable)
            {
                // Release-to-capture: bare click captures nothing
                if (hasRealArea)
                {
                    SelectionCompleted?.Invoke(new CaptureRegion.RectRegion(CurrentSelection!.Value.Standardized));
                }
                else
                {
                    ClearSelection();
                }
            }
            else
            {
                // Adjustable: releasing drag settles the selection
                if (hasRealArea)
                {
                    ShowsControls = true;
                    Invalidate();
                }
                else
                {
                    // If a redraw resulted in nothing, restore previous selection if one existed
                    if (_beforeRedraw.HasValue && _beforeRedraw.Value.HasSelection)
                    {
                        _editable = _beforeRedraw.Value;
                        CurrentSelection = _editable.Rect;
                        ShowsControls = true;
                    }
                    else
                    {
                        ClearSelection();
                    }
                    Invalidate();
                }
            }
        }

        _dragStart = null;
        _beforeRedraw = null;
    }

    private void HandleKeyDown(int vk)
    {
        switch (vk)
        {
            case 0x1B: // VK_ESCAPE
                SelectionCompleted?.Invoke(null);
                break;

            case 0x0D: // VK_RETURN
                if (Mode == OverlayMode.Window)
                {
                    if (HoveredWindow != null)
                    {
                        SelectionCompleted?.Invoke(new CaptureRegion.WindowRegion(HoveredWindow.Id, HoveredWindow.Frame));
                    }
                }
                else
                {
                    if (CurrentSelection.HasValue &&
                        CurrentSelection.Value.Width >= MIN_SELECTION_SIDE &&
                        CurrentSelection.Value.Height >= MIN_SELECTION_SIDE)
                    {
                        SelectionCompleted?.Invoke(new CaptureRegion.RectRegion(CurrentSelection.Value.Standardized));
                    }
                }
                break;

            case 0x25: // VK_LEFT
            case 0x26: // VK_UP
            case 0x27: // VK_RIGHT
            case 0x28: // VK_DOWN
                if (Mode == OverlayMode.Region && IsAdjustable && _editable.HasSelection)
                {
                    bool shift = (GetKeyState(0x10 /* VK_SHIFT */) & 0x8000) != 0;
                    double dx = (vk == 0x25 ? -1.0 : (vk == 0x27 ? 1.0 : 0.0));
                    double dy = (vk == 0x26 ? -1.0 : (vk == 0x28 ? 1.0 : 0.0));

                    if (shift)
                    {
                        _editable.Resize(dx * 10, dy * 10);
                    }
                    else
                    {
                        _editable.Nudge(dx, dy);
                    }

                    CurrentSelection = _editable.Rect;
                    ShowsControls = true;
                    Invalidate();
                }
                break;
        }
    }

    private void SetCustomCursor()
    {
        IntPtr hCursor;
        if (Mode == OverlayMode.Region && IsAdjustable && _editable.HasSelection && ShowsControls && !IsDragging)
        {
            GetCursorPos(out var pt);
            var gPt = new Point(pt.x, pt.y);
            var kind = _editable.DetermineDragKind(gPt);

            hCursor = kind switch
            {
                EditableSelection.DragKind.Resize r => r.Handle switch
                {
                    Lightshot.Core.Handle.TopLeft or Lightshot.Core.Handle.BottomRight => Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32642 /* IDC_SIZENWSE */),
                    Lightshot.Core.Handle.TopRight or Lightshot.Core.Handle.BottomLeft => Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32643 /* IDC_SIZENESW */),
                    Lightshot.Core.Handle.Top or Lightshot.Core.Handle.Bottom => Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32645 /* IDC_SIZENS */),
                    Lightshot.Core.Handle.Left or Lightshot.Core.Handle.Right => Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32644 /* IDC_SIZEWE */),
                    _ => Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32512 /* IDC_ARROW */)
                },
                EditableSelection.DragKind.Move => Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32646 /* IDC_SIZEALL */),
                _ => Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32515 /* IDC_CROSS */)
            };
        }
        else
        {
            hCursor = Win32Window.LoadCursorW(IntPtr.Zero, (IntPtr)32515 /* IDC_CROSS */);
        }

        if (hCursor != IntPtr.Zero)
        {
            Win32Window.SetCursor(hCursor);
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Hide();

        _dcomp?.Dispose();
        _dcomp = null;

        if (Handle != IntPtr.Zero)
        {
            Win32Window.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }

        IntPtr hInst = Win32Window.GetModuleHandleW(null);
        Win32Window.UnregisterClassW(_className, hInst);

        if (_hBrushBlack != IntPtr.Zero)
        {
            DeleteObject(_hBrushBlack);
        }
    }
}
