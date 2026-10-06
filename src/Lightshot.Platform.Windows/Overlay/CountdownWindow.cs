// Ported for Lightshot Windows Port (Phase 7 R1)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Lightshot.Platform.Windows.Recording;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// A transparent, click-through topmost window displaying a centered countdown (3..2..1)
/// before recording starts. DirectComposition surface, excluded from capture via
/// WDA_EXCLUDEFROMCAPTURE, and dismissible via Esc or Cancel().
/// </summary>
public sealed class CountdownWindow : IDisposable
{
    private const int VK_ESCAPE = 0x1B;
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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateFontW(
        int nHeight, int nWidth, int nEscapement, int nOrientation, int fnWeight,
        uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet,
        uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality,
        uint fdwPitchAndFamily, string lpszFace);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int iBkMode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint crColor);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref Win32Window.RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern int FrameRect(IntPtr hDC, ref Win32Window.RECT lprc, IntPtr hbr);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hDC, string lpchText, int nCount, ref Win32Window.RECT lpRect, uint uFormat);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);

    private const int TRANSPARENT = 1;
    private const uint DT_CENTER = 0x0001;
    private const uint DT_VCENTER = 0x0004;
    private const uint DT_SINGLELINE = 0x0020;

    private static readonly System.Collections.Generic.List<Win32Window.WndProc> s_pinnedWndProcs = new();
    private readonly Win32Window.WndProc _wndProc;
    private readonly string _className;
    private readonly IntPtr _hBrushBlack;
    private DCompSurface? _dcomp;
    private bool _disposed;

    private Rect _windowBounds;
    private int _remainingSeconds = 3;
    private CancellationTokenSource? _countdownCts;

    public IntPtr Handle { get; private set; }
    public Rect WindowBounds => _windowBounds;
    public int RemainingSeconds => _remainingSeconds;
    public bool IsRunning => _countdownCts != null && !_countdownCts.IsCancellationRequested;

    public CountdownWindow(Rect? monitorBounds = null)
    {
        _windowBounds = monitorBounds ?? System.Linq.Enumerable.FirstOrDefault(DisplayTopology.GetDisplays(), d => d.IsPrimary)?.Bounds ?? new Rect(0, 0, 1920, 1080);

        _wndProc = WndProc;
        lock (s_pinnedWndProcs)
        {
            s_pinnedWndProcs.Add(_wndProc);
        }
        _className = $"LightshotCountdownWindow_{Guid.NewGuid():N}";
        _hBrushBlack = CreateSolidBrush(0x000000);

        IntPtr hInst = Win32Window.GetModuleHandleW(null);
        var wc = new Win32Window.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Window.WNDCLASSEXW>(),
            style = 3,
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
            "LightshotCountdown",
            WS_POPUP,
            x, y, w, h,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (Handle != IntPtr.Zero)
        {
            // CRITICAL: Exclude from capture
            Win32Window.SetCaptureExclusion(Handle, true);

            // DirectComposition surface initialization
            _dcomp = new DCompSurface(Handle);
        }
    }

    /// <summary>
    /// Runs the countdown from seconds down to 1.
    /// Returns true if countdown reached 0, false if cancelled or dismissed.
    /// </summary>
    public async Task<bool> RunCountdownAsync(
        int seconds = 3,
        bool playSounds = true,
        CancellationToken cancellationToken = default)
    {
        if (Handle == IntPtr.Zero || _disposed) return false;

        Cancel(); // Cancel any existing run

        _countdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _countdownCts.Token;

        _remainingSeconds = Math.Max(1, seconds);

        // Show window topmost
        int x = (int)Math.Round(_windowBounds.MinX);
        int y = (int)Math.Round(_windowBounds.MinY);
        int w = (int)Math.Round(_windowBounds.Width);
        int h = (int)Math.Round(_windowBounds.Height);

        SetWindowPos(Handle, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Invalidate();
        _dcomp?.Commit();

        try
        {
            for (int r = _remainingSeconds; r >= 1; r--)
            {
                if (ct.IsCancellationRequested) return false;

                _remainingSeconds = r;
                Invalidate();
                _dcomp?.Commit();

                RecordingSounds.Play(RecordingCue.Tick, playSounds);

                // Wait 1 second in 50ms slices to promptly react to Escape or cancellation
                for (int slice = 0; slice < 20; slice++)
                {
                    if (ct.IsCancellationRequested) return false;

                    // Check if Escape key was pressed
                    if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
                    {
                        Cancel();
                        return false;
                    }

                    await Task.Delay(50, ct);
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            Hide();
            _countdownCts?.Dispose();
            _countdownCts = null;
        }
    }

    /// <summary>
    /// Cancels any active countdown and immediately hides the window.
    /// </summary>
    public void Cancel()
    {
        _countdownCts?.Cancel();
        Hide();
    }

    public void Hide()
    {
        if (Handle == IntPtr.Zero || _disposed) return;
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_HIDEWINDOW);
        _dcomp?.Commit();
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
                // Pass mouse through so user can interact with underlying target app
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

        double scale = Handle != IntPtr.Zero ? Dpi.GetWindowDpi(Handle) / 96.0 : 1.0;
        if (scale <= 0) scale = 1.0;

        IntPtr memDc = CreateCompatibleDC(hdc);
        IntPtr memBmp = CreateCompatibleBitmap(hdc, clientW, clientH);
        IntPtr oldBmp = SelectObject(memDc, memBmp);

        IntPtr hBrushBlack = CreateSolidBrush(0x000000);
        IntPtr hBrushBadge = CreateSolidBrush(0x202020);
        IntPtr hPenBorder = CreateSolidBrush(0x404040);

        // Large bold font for the number (~120pt)
        IntPtr hFontNumber = CreateFontW(
            -(int)Math.Round(140 * scale), 0, 0, 0, 700 /* Bold */, 0, 0, 0, 1 /* DEFAULT_CHARSET */,
            0, 0, 5 /* CLEARTYPE_QUALITY */, 0, "Segoe UI");
        // Smaller font for "Esc to cancel" (~12pt)
        IntPtr hFontSub = CreateFontW(
            -(int)Math.Round(16 * scale), 0, 0, 0, 400 /* Normal */, 0, 0, 0, 1,
            0, 0, 5, 0, "Segoe UI");

        try
        {
            // Clear background with black (transparent base)
            var clearRc = new Win32Window.RECT { Left = 0, Top = 0, Right = clientW, Bottom = clientH };
            FillRect(memDc, ref clearRc, hBrushBlack);

            // Centered dark HUD badge
            int badgeW = (int)Math.Round(280 * scale);
            int badgeH = (int)Math.Round(240 * scale);
            int bx = (clientW - badgeW) / 2;
            int by = (clientH - badgeH) / 2;

            var badgeRc = new Win32Window.RECT
            {
                Left = bx,
                Top = by,
                Right = bx + badgeW,
                Bottom = by + badgeH
            };

            FillRect(memDc, ref badgeRc, hBrushBadge);
            FrameRect(memDc, ref badgeRc, hPenBorder);

            // Draw countdown number
            string numStr = _remainingSeconds.ToString();
            var numRc = new Win32Window.RECT
            {
                Left = bx,
                Top = by + (int)Math.Round(20 * scale),
                Right = bx + badgeW,
                Bottom = by + badgeH - (int)Math.Round(50 * scale)
            };

            SetBkMode(memDc, TRANSPARENT);
            SetTextColor(memDc, 0xFFFFFF);

            IntPtr oldFont = SelectObject(memDc, hFontNumber);
            DrawTextW(memDc, numStr, numStr.Length, ref numRc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

            // Draw "Esc to cancel"
            var subRc = new Win32Window.RECT
            {
                Left = bx,
                Top = by + badgeH - (int)Math.Round(45 * scale),
                Right = bx + badgeW,
                Bottom = by + badgeH - (int)Math.Round(15 * scale)
            };

            SelectObject(memDc, hFontSub);
            SetTextColor(memDc, 0xCCCCCC);
            DrawTextW(memDc, "Esc to cancel", 13, ref subRc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

            SelectObject(memDc, oldFont);

            BitBlt(hdc, 0, 0, clientW, clientH, memDc, 0, 0, 0x00CC0020 /* SRCCOPY */);
        }
        finally
        {
            SelectObject(memDc, oldBmp);
            DeleteObject(memBmp);
            DeleteDC(memDc);

            DeleteObject(hBrushBlack);
            DeleteObject(hBrushBadge);
            DeleteObject(hPenBorder);
            DeleteObject(hFontNumber);
            DeleteObject(hFontSub);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cancel();

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
