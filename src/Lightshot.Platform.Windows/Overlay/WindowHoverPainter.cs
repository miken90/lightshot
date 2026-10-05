using System;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Renders the window-picker overlay: dimmed backdrop, crisp highlight frame
/// around the candidate window currently hovered by the pointer, and a dimension pill.
/// </summary>
public static class WindowHoverPainter
{
    private const uint COLOR_PURPLE = 0xE22B8A;
    private const uint COLOR_WHITE = 0xFFFFFF;
    private const uint SRCCOPY = 0x00CC0020;
    private const int TRANSPARENT = 1;
    private const uint DT_CENTER = 0x0001;
    private const uint DT_VCENTER = 0x0004;
    private const uint DT_SINGLELINE = 0x0020;

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

    public static void Paint(
        IntPtr hdc,
        int clientWidth,
        int clientHeight,
        Rect screenBounds,
        FrozenWindow? hoveredWindow,
        CapturedImage? backdrop)
    {
        if (hdc == IntPtr.Zero || clientWidth <= 0 || clientHeight <= 0) return;

        IntPtr memDc = CreateCompatibleDC(hdc);
        IntPtr memBmp = CreateCompatibleBitmap(hdc, clientWidth, clientHeight);
        IntPtr oldBmp = SelectObject(memDc, memBmp);

        IntPtr dimDc = CreateCompatibleDC(hdc);
        IntPtr dimBmp = CreateCompatibleBitmap(hdc, clientWidth, clientHeight);
        IntPtr oldDimBmp = SelectObject(dimDc, dimBmp);

        IntPtr hBrushDim = CreateSolidBrush(0x101018);
        IntPtr hBrushPurple = CreateSolidBrush(COLOR_PURPLE);
        IntPtr hBrushWhite = CreateSolidBrush(COLOR_WHITE);
        IntPtr hBrushBadge = CreateSolidBrush(0x222222);

        IntPtr hPenDark = CreatePen(0, 1, 0x333333);

        try
        {
            if (backdrop.HasValue && backdrop.Value.Data.Length > 0)
            {
                DrawBackdrop(memDc, clientWidth, clientHeight, backdrop.Value);
            }
            else
            {
                var bgRc = new Win32Window.RECT { Left = 0, Top = 0, Right = clientWidth, Bottom = clientHeight };
                FillRect(memDc, ref bgRc, hBrushDim);
            }

            var wholeRc = new Win32Window.RECT { Left = 0, Top = 0, Right = clientWidth, Bottom = clientHeight };
            FillRect(dimDc, ref wholeRc, hBrushDim);

            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 110,
                AlphaFormat = 0
            };

            Win32Window.RECT? localWin = null;
            if (hoveredWindow != null)
            {
                var f = hoveredWindow.Frame.Standardized;
                int lx = (int)Math.Round(f.MinX - screenBounds.MinX);
                int ly = (int)Math.Round(f.MinY - screenBounds.MinY);
                int lw = (int)Math.Round(f.Width);
                int lh = (int)Math.Round(f.Height);

                localWin = new Win32Window.RECT
                {
                    Left = lx,
                    Top = ly,
                    Right = lx + lw,
                    Bottom = ly + lh
                };
            }

            if (localWin.HasValue)
            {
                var win = localWin.Value;

                // Dim surrounding areas
                if (win.Top > 0)
                {
                    AlphaBlend(memDc, 0, 0, clientWidth, Math.Min(win.Top, clientHeight), dimDc, 0, 0, clientWidth, Math.Min(win.Top, clientHeight), blend);
                }
                if (win.Bottom < clientHeight)
                {
                    int bTop = Math.Max(0, win.Bottom);
                    int bHeight = clientHeight - bTop;
                    AlphaBlend(memDc, 0, bTop, clientWidth, bHeight, dimDc, 0, bTop, clientWidth, bHeight, blend);
                }
                if (win.Left > 0)
                {
                    int mTop = Math.Max(0, win.Top);
                    int mHeight = Math.Min(win.Bottom, clientHeight) - mTop;
                    if (mHeight > 0)
                    {
                        AlphaBlend(memDc, 0, mTop, Math.Min(win.Left, clientWidth), mHeight, dimDc, 0, mTop, Math.Min(win.Left, clientWidth), mHeight, blend);
                    }
                }
                if (win.Right < clientWidth)
                {
                    int mTop = Math.Max(0, win.Top);
                    int mHeight = Math.Min(win.Bottom, clientHeight) - mTop;
                    int rLeft = Math.Max(0, win.Right);
                    int rWidth = clientWidth - rLeft;
                    if (mHeight > 0 && rWidth > 0)
                    {
                        AlphaBlend(memDc, rLeft, mTop, rWidth, mHeight, dimDc, rLeft, mTop, rWidth, mHeight, blend);
                    }
                }

                // Window highlight border (3px)
                var b1 = win;
                FrameRect(memDc, ref b1, hBrushPurple);
                b1.Left += 1; b1.Top += 1; b1.Right -= 1; b1.Bottom -= 1;
                FrameRect(memDc, ref b1, hBrushPurple);
                b1.Left += 1; b1.Top += 1; b1.Right -= 1; b1.Bottom -= 1;
                FrameRect(memDc, ref b1, hBrushWhite);

                // Window size badge
                string badgeText = $"{win.Width} × {win.Height} px";
                int badgeW = 110;
                int badgeH = 24;
                int badgeX = Math.Max(win.Left, 10);
                int badgeY = Math.Max(win.Top - badgeH - 6, 10);

                IntPtr oldBrush = SelectObject(memDc, hBrushBadge);
                IntPtr oldPen = SelectObject(memDc, hPenDark);
                Rectangle(memDc, badgeX, badgeY, badgeX + badgeW, badgeY + badgeH);
                SelectObject(memDc, oldBrush);
                SelectObject(memDc, oldPen);

                SetBkMode(memDc, TRANSPARENT);
                SetTextColor(memDc, COLOR_WHITE);
                var textRc = new Win32Window.RECT
                {
                    Left = badgeX,
                    Top = badgeY,
                    Right = badgeX + badgeW,
                    Bottom = badgeY + badgeH
                };
                DrawTextW(memDc, badgeText, badgeText.Length, ref textRc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            }
            else
            {
                AlphaBlend(memDc, 0, 0, clientWidth, clientHeight, dimDc, 0, 0, clientWidth, clientHeight, blend);
            }

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
            DeleteObject(hPenDark);
        }
    }

    private static void DrawBackdrop(IntPtr hdc, int clientWidth, int clientHeight, CapturedImage backdrop)
    {
        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = backdrop.PixelWidth;
        bmi.bmiHeader.biHeight = -backdrop.PixelHeight;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        StretchDIBits(
            hdc,
            0, 0, clientWidth, clientHeight,
            0, 0, backdrop.PixelWidth, backdrop.PixelHeight,
            backdrop.Data.ToArray(),
            ref bmi,
            0,
            SRCCOPY
        );
    }
}
