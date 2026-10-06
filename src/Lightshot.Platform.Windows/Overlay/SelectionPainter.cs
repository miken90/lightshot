using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Renders the capture area selection: dimmed backdrop cutout, selection border,
/// 8-point resize handles (in adjustable mode), and floating pixel readout badge.
/// </summary>
public static class SelectionPainter
{
    private const int HANDLE_SIZE = 8;
    private const int HANDLE_HALF = HANDLE_SIZE / 2;
    private const uint COLOR_PURPLE = 0x8A2BE2;
    private const uint COLOR_WHITE = 0xFFFFFF;
    private const uint COLOR_DARK = 0x1A1A1A;

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

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromDC(IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetTextExtentPoint32W(IntPtr hdc, string lpString, int c, out SIZE lpSize);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int nHeight, int nWidth, int nEscapement, int nOrientation,
        int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut,
        uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision,
        uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

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

    public static void Paint(
        IntPtr hdc,
        int clientWidth,
        int clientHeight,
        Rect screenBounds,
        Rect? globalSelection,
        bool showsControls,
        CapturedImage? backdrop,
        IntPtr hwnd = default)
    {
        if (hdc == IntPtr.Zero || clientWidth <= 0 || clientHeight <= 0) return;

        if (hwnd == IntPtr.Zero)
        {
            hwnd = WindowFromDC(hdc);
        }
        double scale = hwnd != IntPtr.Zero ? Dpi.GetWindowDpi(hwnd) / 96.0 : 1.0;
        if (scale <= 0) scale = 1.0;

        IntPtr memDc = CreateCompatibleDC(hdc);
        IntPtr memBmp = CreateCompatibleBitmap(hdc, clientWidth, clientHeight);
        IntPtr oldBmp = SelectObject(memDc, memBmp);

        IntPtr dimDc = CreateCompatibleDC(hdc);
        IntPtr dimBmp = CreateCompatibleBitmap(hdc, clientWidth, clientHeight);
        IntPtr oldDimBmp = SelectObject(dimDc, dimBmp);

        IntPtr hBrushDim = CreateSolidBrush(0x101018); // Dark tint in BGR (0x181010)
        IntPtr hBrushPurple = CreateSolidBrush(0xE22B8A); // Lightshot Purple in BGR (0xE22B8A)
        IntPtr hBrushWhite = CreateSolidBrush(0xFFFFFF);
        IntPtr hBrushBadge = CreateSolidBrush(0x222222);

        IntPtr hPenPurple = CreatePen(0, 2, 0xE22B8A);
        IntPtr hPenWhite = CreatePen(0, 1, 0xFFFFFF);
        IntPtr hPenDark = CreatePen(0, 1, 0x333333);

        try
        {
            // 1. Draw backdrop if available, otherwise clear with dark background
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
                // Top strip: (0, 0, width, sel.Top)
                if (sel.Top > 0)
                {
                    AlphaBlend(memDc, 0, 0, clientWidth, Math.Min(sel.Top, clientHeight), dimDc, 0, 0, clientWidth, Math.Min(sel.Top, clientHeight), blend);
                }
                // Bottom strip: (0, sel.Bottom, width, clientHeight - sel.Bottom)
                if (sel.Bottom < clientHeight)
                {
                    int bTop = Math.Max(0, sel.Bottom);
                    int bHeight = clientHeight - bTop;
                    AlphaBlend(memDc, 0, bTop, clientWidth, bHeight, dimDc, 0, bTop, clientWidth, bHeight, blend);
                }
                // Left strip: (0, sel.Top, sel.Left, sel.Height)
                if (sel.Left > 0)
                {
                    int mTop = Math.Max(0, sel.Top);
                    int mHeight = Math.Min(sel.Bottom, clientHeight) - mTop;
                    if (mHeight > 0)
                    {
                        AlphaBlend(memDc, 0, mTop, Math.Min(sel.Left, clientWidth), mHeight, dimDc, 0, mTop, Math.Min(sel.Left, clientWidth), mHeight, blend);
                    }
                }
                // Right strip: (sel.Right, sel.Top, clientWidth - sel.Right, sel.Height)
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

                // 4. Draw selection border
                var borderRc = sel;
                FrameRect(memDc, ref borderRc, hBrushPurple);
                borderRc.Left += 1; borderRc.Top += 1; borderRc.Right -= 1; borderRc.Bottom -= 1;
                FrameRect(memDc, ref borderRc, hBrushWhite);

                // 5. Draw resize handles when settled in adjustable mode
                if (showsControls)
                {
                    DrawHandles(memDc, sel, hBrushWhite, hPenDark, scale);
                }

                // 6. Draw floating dimension badge
                DrawDimensionBadge(memDc, sel, clientWidth, clientHeight, hBrushBadge, hPenDark, scale);
            }
            else
            {
                // Full dimming
                AlphaBlend(memDc, 0, 0, clientWidth, clientHeight, dimDc, 0, 0, clientWidth, clientHeight, blend);
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
            DeleteObject(hBrushPurple);
            DeleteObject(hBrushWhite);
            DeleteObject(hBrushBadge);
            DeleteObject(hPenPurple);
            DeleteObject(hPenWhite);
            DeleteObject(hPenDark);
        }
    }

    private static void DrawHandles(IntPtr hdc, Win32Window.RECT sel, IntPtr hBrush, IntPtr hPen, double scale)
    {
        IntPtr oldBrush = SelectObject(hdc, hBrush);
        IntPtr oldPen = SelectObject(hdc, hPen);

        int handleSize = Math.Max(4, (int)Math.Round(HANDLE_SIZE * scale));
        int handleHalf = handleSize / 2;

        int midX = sel.Left + sel.Width / 2;
        int midY = sel.Top + sel.Height / 2;

        var points = new[]
        {
            new Point(sel.Left, sel.Top),
            new Point(midX, sel.Top),
            new Point(sel.Right, sel.Top),
            new Point(sel.Left, midY),
            new Point(sel.Right, midY),
            new Point(sel.Left, sel.Bottom),
            new Point(midX, sel.Bottom),
            new Point(sel.Right, sel.Bottom),
        };

        foreach (var pt in points)
        {
            int x = (int)pt.X - handleHalf;
            int y = (int)pt.Y - handleHalf;
            Rectangle(hdc, x, y, x + handleSize, y + handleSize);
        }

        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);
    }

    private static void DrawDimensionBadge(IntPtr hdc, Win32Window.RECT sel, int clientWidth, int clientHeight, IntPtr hBrush, IntPtr hPen, double scale)
    {
        string text = $"{sel.Width} × {sel.Height} px";
        int fontHeight = -(int)Math.Round(12 * scale);
        IntPtr hFont = CreateFontW(
            fontHeight, 0, 0, 0, 400 /* Normal */, 0, 0, 0, 1 /* DEFAULT_CHARSET */,
            0, 0, 5 /* CLEARTYPE_QUALITY */, 0, "Segoe UI");
        IntPtr oldFont = SelectObject(hdc, hFont);

        int badgeW;
        int badgeH = Math.Max(18, (int)Math.Round(24 * scale));
        if (GetTextExtentPoint32W(hdc, text, text.Length, out SIZE size))
        {
            badgeW = size.cx + (int)Math.Round(12 * scale * 2);
        }
        else
        {
            badgeW = (int)Math.Round(100 * scale);
        }

        int badgeX = sel.Left;
        int badgeY = sel.Bottom + (int)Math.Round(6 * scale);

        // Keep inside window bounds
        if (badgeY + badgeH > clientHeight)
        {
            badgeY = Math.Max(0, sel.Top - badgeH - (int)Math.Round(6 * scale));
        }
        if (badgeX + badgeW > clientWidth)
        {
            badgeX = Math.Max(0, clientWidth - badgeW - (int)Math.Round(6 * scale));
        }

        IntPtr oldBrush = SelectObject(hdc, hBrush);
        IntPtr oldPen = SelectObject(hdc, hPen);

        Rectangle(hdc, badgeX, badgeY, badgeX + badgeW, badgeY + badgeH);

        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);

        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, COLOR_WHITE);

        var textRc = new Win32Window.RECT
        {
            Left = badgeX,
            Top = badgeY,
            Right = badgeX + badgeW,
            Bottom = badgeY + badgeH
        };
        DrawTextW(hdc, text, text.Length, ref textRc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
    }

    private static void DrawBackdrop(IntPtr hdc, int clientWidth, int clientHeight, CapturedImage backdrop)
    {
        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = backdrop.PixelWidth;
        bmi.bmiHeader.biHeight = -backdrop.PixelHeight; // Top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        StretchDIBits(
            hdc,
            0, 0, clientWidth, clientHeight,
            0, 0, backdrop.PixelWidth, backdrop.PixelHeight,
            backdrop.Data.ToArray(),
            ref bmi,
            0, // DIB_RGB_COLORS
            SRCCOPY
        );
    }
}
