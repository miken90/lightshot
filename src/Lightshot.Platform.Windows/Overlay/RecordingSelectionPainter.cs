// Ported for Lightshot Windows Port (Phase 7 R1)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Interop;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Renders the recording capture area selection:
/// dimmed backdrop cutout, 8 resize handles, selection border,
/// floating pixel readout badge with aspect ratio, and hovered window highlight.
/// Also provides pure geometry helpers for nudging, aspect locking, typed sizing,
/// default 720p rect calculation, and display confinement.
/// </summary>
public static class RecordingSelectionPainter
{
    private const int HANDLE_SIZE = 8;
    private const int HANDLE_HALF = HANDLE_SIZE / 2;

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
    private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool Rectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

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

    [DllImport("msimg32.dll")]
    private static extern bool AlphaBlend(
        IntPtr hdcDest, int xoriginDest, int yoriginDest, int wDest, int hDest,
        IntPtr hdcSrc, int xoriginSrc, int yoriginSrc, int wSrc, int hSrc,
        BLENDFUNCTION ftn);

    [DllImport("gdi32.dll")]
    private static extern int StretchDIBits(
        IntPtr hdc, int XDest, int YDest, int nDestWidth, int nDestHeight,
        int XSrc, int YSrc, int nSrcWidth, int nSrcHeight,
        byte[] lpBits, ref BITMAPINFO lpBitsInfo, uint iUsage, uint dwRop);

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    private const uint SRCCOPY = 0x00CC0020;
    private const int TRANSPARENT = 1;
    private const uint DT_CENTER = 0x0001;
    private const uint DT_VCENTER = 0x0004;
    private const uint DT_SINGLELINE = 0x0020;

    /// <summary>
    /// Paints the recording selection on the window device context.
    /// </summary>
    public static void Paint(
        IntPtr hdc,
        int clientWidth,
        int clientHeight,
        Rect screenBounds,
        Rect? globalSelection,
        bool showsControls,
        CapturedImage? backdrop,
        FrozenWindow? hoveredWindow = null,
        AspectRatio ratio = AspectRatio.Freeform)
    {
        if (hdc == IntPtr.Zero || clientWidth <= 0 || clientHeight <= 0) return;

        IntPtr memDc = CreateCompatibleDC(hdc);
        IntPtr memBmp = CreateCompatibleBitmap(hdc, clientWidth, clientHeight);
        IntPtr oldBmp = SelectObject(memDc, memBmp);

        IntPtr dimDc = CreateCompatibleDC(hdc);
        IntPtr dimBmp = CreateCompatibleBitmap(hdc, clientWidth, clientHeight);
        IntPtr oldDimBmp = SelectObject(dimDc, dimBmp);

        IntPtr hBrushDim = CreateSolidBrush(0x101018); // Dark tint in BGR
        IntPtr hBrushRed = CreateSolidBrush(0x0000FF); // Red border in BGR
        IntPtr hBrushWhite = CreateSolidBrush(0xFFFFFF);
        IntPtr hBrushBadge = CreateSolidBrush(0x222222);

        IntPtr hPenRed = CreatePen(0, 2, 0x0000FF);
        IntPtr hPenWhite = CreatePen(0, 1, 0xFFFFFF);
        IntPtr hPenDark = CreatePen(0, 1, 0x333333);

        try
        {
            // 1. Draw backdrop if available, otherwise clear
            if (backdrop.HasValue && backdrop.Value.Data.Length > 0)
            {
                DrawBackdrop(memDc, clientWidth, clientHeight, backdrop.Value);
            }
            else
            {
                var bgRc = new Win32Window.RECT { Left = 0, Top = 0, Right = clientWidth, Bottom = clientHeight };
                FillRect(memDc, ref bgRc, hBrushDim);
            }

            // 2. Prepare dimming layer
            var wholeRc = new Win32Window.RECT { Left = 0, Top = 0, Right = clientWidth, Bottom = clientHeight };
            FillRect(dimDc, ref wholeRc, hBrushDim);

            // Calculate local selection relative to this window's top-left
            Win32Window.RECT? localSel = null;
            if (globalSelection.HasValue)
            {
                var g = globalSelection.Value.Standardized;
                int lx = (int)Math.Round(g.MinX - screenBounds.MinX);
                int ly = (int)Math.Round(g.MinY - screenBounds.MinY);
                int lw = (int)Math.Round(g.Width);
                int lh = (int)Math.Round(g.Height);

                localSel = new Win32Window.RECT
                {
                    Left = lx,
                    Top = ly,
                    Right = lx + lw,
                    Bottom = ly + lh
                };
            }

            // 3. Alpha blend dimming mask
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0, // AC_SRC_OVER
                BlendFlags = 0,
                SourceConstantAlpha = 110, // ~43% dimming
                AlphaFormat = 0
            };

            if (localSel.HasValue)
            {
                var sel = localSel.Value;

                // Dim 4 rectangles surrounding the selection
                if (sel.Top > 0)
                {
                    AlphaBlend(memDc, 0, 0, clientWidth, Math.Min(sel.Top, clientHeight), dimDc, 0, 0, clientWidth, Math.Min(sel.Top, clientHeight), blend);
                }
                if (sel.Bottom < clientHeight)
                {
                    int bTop = Math.Max(0, sel.Bottom);
                    int bHeight = clientHeight - bTop;
                    AlphaBlend(memDc, 0, bTop, clientWidth, bHeight, dimDc, 0, bTop, clientWidth, bHeight, blend);
                }
                if (sel.Left > 0)
                {
                    int mTop = Math.Max(0, sel.Top);
                    int mHeight = Math.Min(sel.Bottom, clientHeight) - mTop;
                    if (mHeight > 0)
                    {
                        AlphaBlend(memDc, 0, mTop, Math.Min(sel.Left, clientWidth), mHeight, dimDc, 0, mTop, Math.Min(sel.Left, clientWidth), mHeight, blend);
                    }
                }
                if (sel.Right < clientWidth)
                {
                    int mTop = Math.Max(0, sel.Top);
                    int mHeight = Math.Min(sel.Bottom, clientHeight) - mTop;
                    int rLeft = Math.Max(0, sel.Right);
                    int rWidth = clientWidth - rLeft;
                    if (mHeight > 0 && rWidth > 0)
                    {
                        AlphaBlend(memDc, rLeft, mTop, rWidth, mHeight, dimDc, rLeft, mTop, rWidth, mHeight, blend);
                    }
                }

                // 4. Draw selection border (outer red, inner white for contrast)
                var borderRc = sel;
                FrameRect(memDc, ref borderRc, hBrushRed);
                borderRc.Left += 1; borderRc.Top += 1; borderRc.Right -= 1; borderRc.Bottom -= 1;
                FrameRect(memDc, ref borderRc, hBrushWhite);

                // 5. Draw 8 resize handles when settled in adjustable mode
                if (showsControls)
                {
                    DrawEightHandles(memDc, sel, hBrushWhite, hPenDark);
                }

                // 6. Draw floating dimension badge
                DrawDimensionBadge(memDc, sel, clientWidth, clientHeight, hBrushBadge, hPenDark, ratio);
            }
            else
            {
                // No active selection: full dimming
                AlphaBlend(memDc, 0, 0, clientWidth, clientHeight, dimDc, 0, 0, clientWidth, clientHeight, blend);

                // Highlight hovered window if present
                if (hoveredWindow != null)
                {
                    DrawHoveredWindow(memDc, screenBounds, hoveredWindow, hBrushRed, hBrushWhite);
                }
            }

            // Blit completed frame to target DC
            BitBlt(hdc, 0, 0, clientWidth, clientHeight, memDc, 0, 0, SRCCOPY);
        }
        finally
        {
            SelectObject(memDc, oldBmp);
            DeleteObject(memBmp);
            DeleteDC(memDc);

            SelectObject(dimDc, oldDimBmp);
            DeleteObject(dimBmp);
            DeleteDC(dimDc);

            DeleteObject(hBrushDim);
            DeleteObject(hBrushRed);
            DeleteObject(hBrushWhite);
            DeleteObject(hBrushBadge);
            DeleteObject(hPenRed);
            DeleteObject(hPenWhite);
            DeleteObject(hPenDark);
        }
    }

    /// <summary>
    /// Draws 8 resize handles around the selection rectangle:
    /// TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left.
    /// </summary>
    public static void DrawEightHandles(IntPtr hdc, Win32Window.RECT sel, IntPtr hBrush, IntPtr hPen)
    {
        IntPtr oldBrush = SelectObject(hdc, hBrush);
        IntPtr oldPen = SelectObject(hdc, hPen);

        try
        {
            int midX = (sel.Left + sel.Right) / 2;
            int midY = (sel.Top + sel.Bottom) / 2;

            (int x, int y)[] handles =
            [
                (sel.Left, sel.Top),       // TopLeft
                (midX, sel.Top),           // Top
                (sel.Right, sel.Top),      // TopRight
                (sel.Right, midY),         // Right
                (sel.Right, sel.Bottom),   // BottomRight
                (midX, sel.Bottom),        // Bottom
                (sel.Left, sel.Bottom),    // BottomLeft
                (sel.Left, midY)           // Left
            ];

            foreach (var (hx, hy) in handles)
            {
                Rectangle(hdc, hx - HANDLE_HALF, hy - HANDLE_HALF, hx + HANDLE_HALF, hy + HANDLE_HALF);
            }
        }
        finally
        {
            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
        }
    }

    private static void DrawDimensionBadge(
        IntPtr hdc, Win32Window.RECT sel, int clientWidth, int clientHeight,
        IntPtr hBrush, IntPtr hPen, AspectRatio ratio)
    {
        int width = sel.Right - sel.Left;
        int height = sel.Bottom - sel.Top;
        string text = ratio == AspectRatio.Freeform
            ? $"{width} x {height}"
            : $"{width} x {height} ({ratio.Title()})";

        int badgeWidth = Math.Max(100, text.Length * 8 + 16);
        int badgeHeight = 22;

        int bx = sel.Left + (width - badgeWidth) / 2;
        int by = sel.Bottom + 6;

        if (by + badgeHeight > clientHeight - 4)
        {
            by = sel.Top - badgeHeight - 6;
        }
        if (by < 4)
        {
            by = sel.Top + 6;
        }

        bx = Math.Clamp(bx, 4, Math.Max(4, clientWidth - badgeWidth - 4));

        var badgeRc = new Win32Window.RECT
        {
            Left = bx,
            Top = by,
            Right = bx + badgeWidth,
            Bottom = by + badgeHeight
        };

        FillRect(hdc, ref badgeRc, hBrush);
        FrameRect(hdc, ref badgeRc, hPen);

        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, 0xFFFFFF);
        DrawTextW(hdc, text, text.Length, ref badgeRc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }

    private static void DrawHoveredWindow(
        IntPtr hdc, Rect screenBounds, FrozenWindow window,
        IntPtr hBrushOuter, IntPtr hBrushInner)
    {
        var f = window.Frame.Standardized;
        int lx = (int)Math.Round(f.MinX - screenBounds.MinX);
        int ly = (int)Math.Round(f.MinY - screenBounds.MinY);
        int lw = (int)Math.Round(f.Width);
        int lh = (int)Math.Round(f.Height);

        var borderRc = new Win32Window.RECT
        {
            Left = lx,
            Top = ly,
            Right = lx + lw,
            Bottom = ly + lh
        };

        FrameRect(hdc, ref borderRc, hBrushOuter);
        borderRc.Left += 1; borderRc.Top += 1; borderRc.Right -= 1; borderRc.Bottom -= 1;
        FrameRect(hdc, ref borderRc, hBrushInner);
    }

    private static void DrawBackdrop(IntPtr hdc, int clientWidth, int clientHeight, CapturedImage image)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = image.PixelWidth,
                biHeight = -image.PixelHeight, // Top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            }
        };

        StretchDIBits(
            hdc,
            0, 0, clientWidth, clientHeight,
            0, 0, image.PixelWidth, image.PixelHeight,
            image.Data.ToArray(),
            ref bmi,
            0,
            SRCCOPY);
    }

    // =========================================================================
    // Pure Geometry & Selection Math Helpers
    // =========================================================================

    /// <summary>
    /// Default recording rect: a centred 720p rect on large displays,
    /// 60% width 16:9 on smaller displays, fitted within display bounds.
    /// Uses Core EditableSelection.DefaultRecordingRect.
    /// </summary>
    public static Rect GetDefaultRecordingRect(Rect bounds) =>
        EditableSelection.DefaultRecordingRect(bounds);

    /// <summary>
    /// Confines a selection rectangle to the display that has the largest geometric overlap.
    /// Clamps the rectangle to stay entirely inside that target display's bounds.
    /// </summary>
    public static Rect ConfineToDisplayWithLargestOverlap(Rect rect, IEnumerable<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var display = DisplayMath.FindLargestOverlap(displays, rect);
        if (display == null) return rect.Standardized;

        return ConfineToDisplay(rect, display.Bounds);
    }

    /// <summary>
    /// Confines a rectangle to the given display bounds, ensuring it does not exceed bounds.
    /// </summary>
    public static Rect ConfineToDisplay(Rect rect, Rect displayBounds)
    {
        var s = rect.Standardized;
        var b = displayBounds.Standardized;

        double w = Math.Min(s.Width, b.Width);
        double h = Math.Min(s.Height, b.Height);
        double x = Math.Clamp(s.MinX, b.MinX, Math.Max(b.MinX, b.MaxX - w));
        double y = Math.Clamp(s.MinY, b.MinY, Math.Max(b.MinY, b.MaxY - h));

        return new Rect(x, y, w, h);
    }

    /// <summary>
    /// Performs an arrow key nudge: 1 px default, or 10 px when largeStep (Shift held) is true.
    /// </summary>
    public static void Nudge(ref EditableSelection selection, double dx, double dy, bool largeStep = false)
    {
        double step = largeStep ? 10.0 : 1.0;
        selection.Nudge(dx * step, dy * step);
    }

    /// <summary>
    /// Sets the aspect ratio lock on an EditableSelection.
    /// </summary>
    public static void SetRatio(ref EditableSelection selection, AspectRatio ratio)
    {
        selection.SetRatio(ratio);
    }

    /// <summary>
    /// Sets a typed width in pixels, respecting the aspect ratio lock if active.
    /// </summary>
    public static void SetWidth(ref EditableSelection selection, double width)
    {
        selection.SetWidth(width);
    }

    /// <summary>
    /// Sets a typed height in pixels, respecting the aspect ratio lock if active.
    /// </summary>
    public static void SetHeight(ref EditableSelection selection, double height)
    {
        selection.SetHeight(height);
    }

    /// <summary>
    /// Sets typed width and height dimensions.
    /// </summary>
    public static void SetSize(ref EditableSelection selection, double width, double height)
    {
        if (selection.Ratio.Value() is { } lockRatio)
        {
            double w = Math.Max(EditableSelection.MinimumSide, width);
            selection.SetWidth(w);
        }
        else
        {
            selection.SetWidth(width);
            selection.SetHeight(height);
        }
    }

    /// <summary>
    /// Snaps the selection to a picked window frame. A picked window records its area.
    /// </summary>
    public static CaptureRegion.WindowRegion PickWindow(uint windowId, Rect frame) =>
        new(windowId, frame);

    /// <summary>
    /// Snaps the selection to a picked window. A picked window records its area.
    /// </summary>
    public static CaptureRegion.WindowRegion PickWindow(FrozenWindow window) =>
        new(window.Id, window.Frame);
}
