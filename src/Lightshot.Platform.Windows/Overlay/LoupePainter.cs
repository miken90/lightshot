using System;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Draws the capture loupe on the overlay's memory DC: a magnified patch of the frozen backdrop
/// around the pointer, a pixel grid, a centre crosshair, and an "X, Y  #RRGGBB" readout.
/// It paints only onto the overlay surface, which is excluded from screen capture.
/// </summary>
public static class LoupePainter
{
    /// <summary>Source pixels per side in the sampled patch; odd so one pixel sits at the centre.</summary>
    public const int PatchSize = 21;

    private const uint SRCCOPY = 0x00CC0020;
    private const int TRANSPARENT = 1;
    private const int COLORONCOLOR = 3;
    private const uint DT_LEFT = 0x0000;
    private const uint DT_VCENTER = 0x0004;
    private const uint DT_SINGLELINE = 0x0020;

    private const uint COLOR_PANEL = 0x222222;
    private const uint COLOR_BORDER = 0xE22B8A; // Lightshot purple, BGR
    private const uint COLOR_GRID = 0x555555;
    private const uint COLOR_WHITE = 0xFFFFFF;
    private const uint COLOR_BLACK = 0x000000;

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int iBkMode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint crColor);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref Win32Window.RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern int FrameRect(IntPtr hDC, ref Win32Window.RECT lprc, IntPtr hbr);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int nHeight, int nWidth, int nEscapement, int nOrientation,
        int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut,
        uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision,
        uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hDC, string lpchText, int nCount, ref Win32Window.RECT lpRect, uint uFormat);

    [DllImport("gdi32.dll")]
    private static extern int StretchDIBits(
        IntPtr hdc, int XDest, int YDest, int nDestWidth, int nDestHeight,
        int XSrc, int YSrc, int nSrcWidth, int nSrcHeight,
        byte[] lpBits, ref BITMAPINFO lpBitsInfo, uint iUsage, uint dwRop);

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

    /// <param name="hdc">The overlay's double-buffer DC, sized clientWidth x clientHeight.</param>
    /// <param name="monitorBounds">Global bounds of the monitor this overlay covers.</param>
    /// <param name="pointer">Pointer position in global pixel coordinates.</param>
    /// <param name="zoom">2, 4 or 8: device pixels per source pixel at 100% scale.</param>
    public static void Paint(
        IntPtr hdc,
        int clientWidth,
        int clientHeight,
        Rect monitorBounds,
        Point pointer,
        CapturedImage backdrop,
        int zoom,
        double scale)
    {
        if (hdc == IntPtr.Zero || backdrop.Data.Length == 0 || backdrop.PixelWidth <= 0 || backdrop.PixelHeight <= 0)
            return;
        if (scale <= 0) scale = 1.0;
        if (zoom <= 0) zoom = 4;

        // The backdrop is stretched over the whole client area, so map the pointer through that ratio.
        double localX = pointer.X - monitorBounds.MinX;
        double localY = pointer.Y - monitorBounds.MinY;
        int sx = (int)Math.Floor(localX * backdrop.PixelWidth / Math.Max(1, clientWidth));
        int sy = (int)Math.Floor(localY * backdrop.PixelHeight / Math.Max(1, clientHeight));

        int cell = Math.Max(1, (int)Math.Round(zoom * scale));
        int gridPx = PatchSize * cell;
        int pad = (int)Math.Round(6 * scale);
        int readoutH = (int)Math.Round(26 * scale);
        int innerW = Math.Max(gridPx, (int)Math.Round(170 * scale));
        int boxW = innerW + pad * 2;
        int boxH = gridPx + readoutH + pad * 3;

        var layout = LoupeGeometry.Calculate(pointer, monitorBounds, new Size(boxW, boxH), scale);
        int boxX = (int)Math.Round(layout.Bounds.MinX - monitorBounds.MinX);
        int boxY = (int)Math.Round(layout.Bounds.MinY - monitorBounds.MinY);

        string hex = LoupeColour.SampleHex(backdrop, sx, sy) ?? "#------";
        string text = $"{(int)Math.Round(pointer.X)}, {(int)Math.Round(pointer.Y)}   {hex}";

        IntPtr panelBrush = CreateSolidBrush(COLOR_PANEL);
        IntPtr borderBrush = CreateSolidBrush(COLOR_BORDER);
        IntPtr gridBrush = CreateSolidBrush(COLOR_GRID);
        IntPtr whiteBrush = CreateSolidBrush(COLOR_WHITE);
        IntPtr blackBrush = CreateSolidBrush(COLOR_BLACK);
        IntPtr swatchBrush = IntPtr.Zero;
        IntPtr hFont = IntPtr.Zero;
        IntPtr oldFont = IntPtr.Zero;
        try
        {
            var box = new Win32Window.RECT { Left = boxX, Top = boxY, Right = boxX + boxW, Bottom = boxY + boxH };
            FillRect(hdc, ref box, panelBrush);

            int gridX = boxX + pad + (innerW - gridPx) / 2;
            int gridY = boxY + pad;
            DrawPatch(hdc, backdrop, sx, sy, gridX, gridY, cell);

            if (cell >= 4)
            {
                DrawGrid(hdc, gridX, gridY, cell, gridBrush);
            }

            // Crosshair: frame the centre pixel in black over white so it reads on any colour.
            int centre = PatchSize / 2;
            var centreRc = new Win32Window.RECT
            {
                Left = gridX + centre * cell,
                Top = gridY + centre * cell,
                Right = gridX + (centre + 1) * cell,
                Bottom = gridY + (centre + 1) * cell,
            };
            FrameRect(hdc, ref centreRc, blackBrush);
            var inner = new Win32Window.RECT
            {
                Left = centreRc.Left - 1, Top = centreRc.Top - 1, Right = centreRc.Right + 1, Bottom = centreRc.Bottom + 1,
            };
            FrameRect(hdc, ref inner, whiteBrush);

            var gridRc = new Win32Window.RECT { Left = gridX, Top = gridY, Right = gridX + gridPx, Bottom = gridY + gridPx };
            FrameRect(hdc, ref gridRc, borderBrush);

            // Readout: colour swatch chip, then "X, Y   #RRGGBB".
            int rowTop = gridY + gridPx + pad;
            int chip = readoutH - pad;
            var chipRc = new Win32Window.RECT { Left = boxX + pad, Top = rowTop, Right = boxX + pad + chip, Bottom = rowTop + chip };
            swatchBrush = CreateSolidBrush(SwatchColour(backdrop, sx, sy));
            FillRect(hdc, ref chipRc, swatchBrush);
            FrameRect(hdc, ref chipRc, whiteBrush);

            hFont = CreateFontW(
                -(int)Math.Round(12 * scale), 0, 0, 0, 400, 0, 0, 0, 1,
                0, 0, 5 /* CLEARTYPE_QUALITY */, 0, "Segoe UI");
            oldFont = SelectObject(hdc, hFont);
            SetBkMode(hdc, TRANSPARENT);
            SetTextColor(hdc, COLOR_WHITE);
            var textRc = new Win32Window.RECT
            {
                Left = chipRc.Right + pad, Top = rowTop, Right = boxX + boxW - pad, Bottom = rowTop + chip,
            };
            DrawTextW(hdc, text, text.Length, ref textRc, DT_LEFT | DT_VCENTER | DT_SINGLELINE);

            var outline = box;
            FrameRect(hdc, ref outline, borderBrush);
        }
        finally
        {
            if (oldFont != IntPtr.Zero) SelectObject(hdc, oldFont);
            if (hFont != IntPtr.Zero) DeleteObject(hFont);
            if (swatchBrush != IntPtr.Zero) DeleteObject(swatchBrush);
            DeleteObject(panelBrush);
            DeleteObject(borderBrush);
            DeleteObject(gridBrush);
            DeleteObject(whiteBrush);
            DeleteObject(blackBrush);
        }
    }

    /// <summary>
    /// Copies the PatchSize x PatchSize pixels centred on (cx, cy) into a small DIB and stretches
    /// it up by pixel replication. Pixels off the image read as dark grey.
    /// </summary>
    private static void DrawPatch(IntPtr hdc, CapturedImage backdrop, int cx, int cy, int destX, int destY, int cell)
    {
        var patch = new byte[PatchSize * PatchSize * 4];
        var src = backdrop.Data.Span;
        int half = PatchSize / 2;
        for (int py = 0; py < PatchSize; py++)
        {
            for (int px = 0; px < PatchSize; px++)
            {
                int ix = cx - half + px;
                int iy = cy - half + py;
                int o = (py * PatchSize + px) * 4;
                long s = ((long)iy * backdrop.PixelWidth + ix) * 4;
                if (ix >= 0 && iy >= 0 && ix < backdrop.PixelWidth && iy < backdrop.PixelHeight && s + 4 <= src.Length)
                {
                    patch[o] = src[(int)s];
                    patch[o + 1] = src[(int)s + 1];
                    patch[o + 2] = src[(int)s + 2];
                }
                else
                {
                    patch[o] = patch[o + 1] = patch[o + 2] = 0x30;
                }
                patch[o + 3] = 0xFF;
            }
        }

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = PatchSize;
        bmi.bmiHeader.biHeight = -PatchSize; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        SetStretchBltMode(hdc, COLORONCOLOR);
        StretchDIBits(
            hdc, destX, destY, PatchSize * cell, PatchSize * cell,
            0, 0, PatchSize, PatchSize, patch, ref bmi, 0, SRCCOPY);
    }

    private static void DrawGrid(IntPtr hdc, int gridX, int gridY, int cell, IntPtr brush)
    {
        int total = PatchSize * cell;
        for (int i = 1; i < PatchSize; i++)
        {
            int offset = i * cell;
            var vertical = new Win32Window.RECT { Left = gridX + offset, Top = gridY, Right = gridX + offset + 1, Bottom = gridY + total };
            FillRect(hdc, ref vertical, brush);
            var horizontal = new Win32Window.RECT { Left = gridX, Top = gridY + offset, Right = gridX + total, Bottom = gridY + offset + 1 };
            FillRect(hdc, ref horizontal, brush);
        }
    }

    /// <summary>COLORREF (0x00BBGGRR) of the pixel at (x, y), or panel grey when off the image.</summary>
    private static uint SwatchColour(CapturedImage image, int x, int y)
    {
        if (x < 0 || y < 0 || x >= image.PixelWidth || y >= image.PixelHeight) return COLOR_PANEL;
        long o = ((long)y * image.PixelWidth + x) * 4;
        var px = image.Data.Span;
        if (o + 4 > px.Length) return COLOR_PANEL;
        return (uint)(px[(int)o + 2] | (px[(int)o + 1] << 8) | (px[(int)o] << 16));
    }
}
