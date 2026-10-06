// Ported for Lightshot Windows Port (Phase 7 R1)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// A raw Win32 topmost window marking the recording area during an active recording session:
/// 3 px red border pulsing opacity 1..0.3 (steady while paused), dims outside the area (if enabled),
/// click-through so user can interact with underlying applications,
/// DirectComposition surface, and excluded from capture via WDA_EXCLUDEFROMCAPTURE.
/// </summary>
public sealed class RecordingFrameWindow : IDisposable
{
    private const int BORDER_WIDTH = 3;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_POPUP = 0x80000000;
    private const uint WM_NCHITTEST = 0x0084;
    private static readonly IntPtr HTTRANSPARENT = (IntPtr)(-1);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref Win32Window.RECT lprc, IntPtr hbr);

    [DllImport("msimg32.dll")]
    private static extern bool AlphaBlend(
        IntPtr hdcDest, int xoriginDest, int yoriginDest, int wDest, int hDest,
        IntPtr hdcSrc, int xoriginSrc, int yoriginSrc, int wSrc, int hSrc,
        BLENDFUNCTION ftn);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private static readonly System.Collections.Generic.List<Win32Window.WndProc> s_pinnedWndProcs = new();
    private readonly Win32Window.WndProc _wndProc;
    private readonly string _className;
    private readonly IntPtr _hBrushBlack;
    private DCompSurface? _dcomp;
    private Timer? _pulseTimer;
    private readonly Stopwatch _stopwatch = new();
    private bool _disposed;

    private Rect _windowBounds;
    private Rect? _recordedHole; // In global coordinates
    private bool _dimsOutside = true;
    private bool _isPaused = false;
    private double _currentOpacity = 1.0;

    public IntPtr Handle { get; private set; }
    public Rect WindowBounds => _windowBounds;
    public Rect? RecordedHole => _recordedHole;
    public bool DimsOutside => _dimsOutside;
    public bool IsPaused => _isPaused;
    public double CurrentOpacity => _currentOpacity;

    public RecordingFrameWindow(Rect? monitorBounds = null)
    {
        _windowBounds = monitorBounds ?? System.Linq.Enumerable.FirstOrDefault(DisplayTopology.GetDisplays(), d => d.IsPrimary)?.Bounds ?? new Rect(0, 0, 1920, 1080);

        _wndProc = WndProc;
        lock (s_pinnedWndProcs)
        {
            s_pinnedWndProcs.Add(_wndProc);
        }
        _className = $"LightshotRecordingFrameWindow_{Guid.NewGuid():N}";
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

        int x = (int)Math.Round(_windowBounds.MinX);
        int y = (int)Math.Round(_windowBounds.MinY);
        int w = (int)Math.Round(_windowBounds.Width);
        int h = (int)Math.Round(_windowBounds.Height);

        uint exStyle = (uint)(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT);

        Handle = Win32Window.CreateWindowExW(
            exStyle,
            _className,
            "LightshotRecordingFrame",
            WS_POPUP,
            x, y, w, h,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (Handle != IntPtr.Zero)
        {
            // CRITICAL: Exclude from screen recording capture
            Win32Window.SetCaptureExclusion(Handle, true);

            // DirectComposition surface initialization
            _dcomp = new DCompSurface(Handle);
        }
    }

    /// <summary>
    /// Displays the pulsing frame around the specified capture region.
    /// </summary>
    public void Show(CaptureRegion region, bool dimsOutside = true, bool isPaused = false)
    {
        ArgumentNullException.ThrowIfNull(region);

        Rect? hole = region switch
        {
            CaptureRegion.RectRegion r => r.Rect.Standardized,
            CaptureRegion.WindowRegion w => w.Frame.Standardized,
            CaptureRegion.DisplayRegion => null, // Whole display: no hole cutout, border around edges
            _ => null
        };

        Show(hole, dimsOutside, isPaused);
    }

    /// <summary>
    /// Displays the pulsing frame around the specified global rectangle.
    /// If hole is null, border is rendered along the window edges without dimming.
    /// </summary>
    public void Show(Rect? hole, bool dimsOutside = true, bool isPaused = false)
    {
        if (Handle == IntPtr.Zero || _disposed) return;

        _recordedHole = hole;
        _dimsOutside = dimsOutside;
        _isPaused = isPaused;

        // Position window on screen
        int x = (int)Math.Round(_windowBounds.MinX);
        int y = (int)Math.Round(_windowBounds.MinY);
        int w = (int)Math.Round(_windowBounds.Width);
        int h = (int)Math.Round(_windowBounds.Height);

        SetWindowPos(Handle, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        if (!isPaused)
        {
            StartPulsing();
        }
        else
        {
            StopPulsing();
            _currentOpacity = 1.0;
        }

        Invalidate();
        _dcomp?.Commit();
    }

    /// <summary>
    /// Updates pause state: paused holds opacity at 1.0 steady, running pulses between 1.0 and 0.3.
    /// </summary>
    public void SetPaused(bool isPaused)
    {
        _isPaused = isPaused;
        if (isPaused)
        {
            StopPulsing();
            _currentOpacity = 1.0;
        }
        else
        {
            StartPulsing();
        }
        Invalidate();
    }

    /// <summary>
    /// Toggles pulse animation.
    /// </summary>
    public void SetPulsing(bool pulsing) => SetPaused(!pulsing);

    public void Hide()
    {
        if (Handle == IntPtr.Zero || _disposed) return;
        StopPulsing();
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_HIDEWINDOW);
        _dcomp?.Commit();
    }

    private void StartPulsing()
    {
        if (_pulseTimer != null) return;
        _stopwatch.Restart();
        // 30 fps pulse update timer
        _pulseTimer = new Timer(OnPulseTick, null, 0, 33);
    }

    private void StopPulsing()
    {
        _pulseTimer?.Dispose();
        _pulseTimer = null;
        _stopwatch.Stop();
    }

    private void OnPulseTick(object? state)
    {
        if (_disposed || Handle == IntPtr.Zero) return;

        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        // Smooth oscillation between 1.0 and 0.3 with 1s duration, autoreverse (2s full period)
        // Cosine period 2s: cos(PI * t) oscillates between 1 and -1 over 2 seconds
        double cosVal = Math.Cos(Math.PI * elapsed);
        _currentOpacity = 0.3 + 0.7 * (0.5 * (1.0 + cosVal));

        Invalidate();
    }

    private void Invalidate()
    {
        if (Handle != IntPtr.Zero && !_disposed)
        {
            Win32Window.InvalidateRect(Handle, IntPtr.Zero, false);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        switch (uMsg)
        {
            case WM_NCHITTEST:
                // Pass all mouse clicks through to windows underneath
                return HTTRANSPARENT;

            case Win32Window.WM_ERASEBKGND:
                return (IntPtr)1;

            case Win32Window.WM_PAINT:
            {
                IntPtr hdc = Win32Window.BeginPaint(hWnd, out var ps);
                Paint(hdc);
                Win32Window.EndPaint(hWnd, ref ps);
                _dcomp?.Commit();
                return IntPtr.Zero;
            }
        }

        return Win32Window.DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    private void Paint(IntPtr hdc)
    {
        int clientW = (int)_windowBounds.Width;
        int clientH = (int)_windowBounds.Height;
        if (clientW <= 0 || clientH <= 0) return;

        IntPtr memDc = CreateCompatibleDC(hdc);
        IntPtr memBmp = CreateCompatibleBitmap(hdc, clientW, clientH);
        IntPtr oldMemBmp = SelectObject(memDc, memBmp);

        IntPtr redDc = CreateCompatibleDC(hdc);
        IntPtr redBmp = CreateCompatibleBitmap(hdc, clientW, clientH);
        IntPtr oldRedBmp = SelectObject(redDc, redBmp);

        IntPtr dimDc = CreateCompatibleDC(hdc);
        IntPtr dimBmp = CreateCompatibleBitmap(hdc, clientW, clientH);
        IntPtr oldDimBmp = SelectObject(dimDc, dimBmp);

        IntPtr hBrushBlack = CreateSolidBrush(0x000000);
        IntPtr hBrushDim = CreateSolidBrush(0x101018);
        IntPtr hBrushRed = CreateSolidBrush(0x0000FF); // Pure red in BGR: (R=255, G=0, B=0)

        try
        {
            // Clear memory DC with black (transparent base)
            var clearRc = new Win32Window.RECT { Left = 0, Top = 0, Right = clientW, Bottom = clientH };
            FillRect(memDc, ref clearRc, hBrushBlack);

            // Fill solid red DC
            FillRect(redDc, ref clearRc, hBrushRed);

            // Fill solid dim DC
            FillRect(dimDc, ref clearRc, hBrushDim);

            // Compute local hole coordinates relative to this window
            Win32Window.RECT? localHole = null;
            if (_recordedHole.HasValue)
            {
                var h = _recordedHole.Value.Standardized;
                int lx = (int)Math.Round(h.MinX - _windowBounds.MinX);
                int ly = (int)Math.Round(h.MinY - _windowBounds.MinY);
                int lw = (int)Math.Round(h.Width);
                int lh = (int)Math.Round(h.Height);

                localHole = new Win32Window.RECT
                {
                    Left = lx,
                    Top = ly,
                    Right = lx + lw,
                    Bottom = ly + lh
                };
            }

            // 1. Dimming layer outside hole (if enabled)
            if (_dimsOutside && localHole.HasValue)
            {
                var hole = localHole.Value;
                var dimBlend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 110, // ~43% dimming
                    AlphaFormat = 0
                };

                // Top strip
                if (hole.Top > 0)
                {
                    AlphaBlend(memDc, 0, 0, clientW, Math.Min(hole.Top, clientH), dimDc, 0, 0, clientW, Math.Min(hole.Top, clientH), dimBlend);
                }
                // Bottom strip
                if (hole.Bottom < clientH)
                {
                    int bTop = Math.Max(0, hole.Bottom);
                    int bHeight = clientH - bTop;
                    AlphaBlend(memDc, 0, bTop, clientW, bHeight, dimDc, 0, bTop, clientW, bHeight, dimBlend);
                }
                // Left strip
                if (hole.Left > 0)
                {
                    int mTop = Math.Max(0, hole.Top);
                    int mHeight = Math.Min(hole.Bottom, clientH) - mTop;
                    if (mHeight > 0)
                    {
                        AlphaBlend(memDc, 0, mTop, Math.Min(hole.Left, clientW), mHeight, dimDc, 0, mTop, Math.Min(hole.Left, clientW), mHeight, dimBlend);
                    }
                }
                // Right strip
                if (hole.Right < clientW)
                {
                    int mTop = Math.Max(0, hole.Top);
                    int mHeight = Math.Min(hole.Bottom, clientH) - mTop;
                    int rLeft = Math.Max(0, hole.Right);
                    int rWidth = clientW - rLeft;
                    if (mHeight > 0 && rWidth > 0)
                    {
                        AlphaBlend(memDc, rLeft, mTop, rWidth, mHeight, dimDc, rLeft, mTop, rWidth, mHeight, dimBlend);
                    }
                }
            }

            // 2. 3 px red border with pulsing opacity
            byte redAlpha = (byte)Math.Clamp((int)Math.Round(_currentOpacity * 255), 0, 255);
            var redBlend = new BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = redAlpha,
                AlphaFormat = 0
            };

            Win32Window.RECT borderTarget;
            if (localHole.HasValue)
            {
                // The border's inner edge touches the recorded area, so it sits just outside it
                var hole = localHole.Value;
                borderTarget = new Win32Window.RECT
                {
                    Left = Math.Max(0, hole.Left - BORDER_WIDTH),
                    Top = Math.Max(0, hole.Top - BORDER_WIDTH),
                    Right = Math.Min(clientW, hole.Right + BORDER_WIDTH),
                    Bottom = Math.Min(clientH, hole.Bottom + BORDER_WIDTH)
                };
            }
            else
            {
                // Whole display: border sits along display edges
                borderTarget = new Win32Window.RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = clientW,
                    Bottom = clientH
                };
            }

            int bw = borderTarget.Right - borderTarget.Left;
            int bh = borderTarget.Bottom - borderTarget.Top;

            if (bw > 0 && bh > 0)
            {
                // Top border line
                AlphaBlend(memDc, borderTarget.Left, borderTarget.Top, bw, Math.Min(BORDER_WIDTH, bh),
                    redDc, 0, 0, bw, Math.Min(BORDER_WIDTH, bh), redBlend);

                // Bottom border line
                if (bh > BORDER_WIDTH)
                {
                    AlphaBlend(memDc, borderTarget.Left, borderTarget.Bottom - BORDER_WIDTH, bw, BORDER_WIDTH,
                        redDc, 0, 0, bw, BORDER_WIDTH, redBlend);
                }

                // Left border line
                int midH = Math.Max(0, bh - (2 * BORDER_WIDTH));
                if (midH > 0)
                {
                    AlphaBlend(memDc, borderTarget.Left, borderTarget.Top + BORDER_WIDTH, Math.Min(BORDER_WIDTH, bw), midH,
                        redDc, 0, 0, Math.Min(BORDER_WIDTH, bw), midH, redBlend);

                    // Right border line
                    if (bw > BORDER_WIDTH)
                    {
                        AlphaBlend(memDc, borderTarget.Right - BORDER_WIDTH, borderTarget.Top + BORDER_WIDTH, BORDER_WIDTH, midH,
                            redDc, 0, 0, BORDER_WIDTH, midH, redBlend);
                    }
                }
            }

            // Blit completed composition to screen DC
            BitBlt(hdc, 0, 0, clientW, clientH, memDc, 0, 0, 0x00CC0020 /* SRCCOPY */);
        }
        finally
        {
            SelectObject(memDc, oldMemBmp);
            DeleteObject(memBmp);
            DeleteDC(memDc);

            SelectObject(redDc, oldRedBmp);
            DeleteObject(redBmp);
            DeleteDC(redDc);

            SelectObject(dimDc, oldDimBmp);
            DeleteObject(dimBmp);
            DeleteDC(dimDc);

            DeleteObject(hBrushBlack);
            DeleteObject(hBrushDim);
            DeleteObject(hBrushRed);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopPulsing();

        if (Handle != IntPtr.Zero)
        {
            _dcomp?.Dispose();
            _dcomp = null;

            Win32Window.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }

        DeleteObject(_hBrushBlack);
    }
}
