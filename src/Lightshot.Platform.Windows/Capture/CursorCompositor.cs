// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// Composites the current mouse cursor onto a captured still frame using GetCursorInfo and DrawIconEx.
/// </summary>
public static class CursorCompositor
{
    private const int CURSOR_SHOWING = 0x00000001;
    private const uint DI_NORMAL = 0x0003;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(
        IntPtr hdc, int xLeft, int yTop, IntPtr hIcon,
        int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    /// <summary>
    /// Composites the active mouse cursor into the provided BGRA buffer if cursor is currently visible.
    /// </summary>
    public static void CompositeCursor(byte[] bgraPixels, int width, int height, Rect displayBounds)
    {
        if (bgraPixels == null || width <= 0 || height <= 0 || bgraPixels.Length < width * height * 4)
        {
            return;
        }

        try
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref ci) || (ci.flags & CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero)
            {
                return;
            }

            if (!GetIconInfo(ci.hCursor, out var ii))
            {
                return;
            }

            try
            {
                int cursorX = ci.ptScreenPos.X - (int)Math.Round(displayBounds.X) - ii.xHotspot;
                int cursorY = ci.ptScreenPos.Y - (int)Math.Round(displayBounds.Y) - ii.yHotspot;

                // Check if cursor touches the display bounds
                if (cursorX + 64 < 0 || cursorX >= width || cursorY + 64 < 0 || cursorY >= height)
                {
                    return;
                }

                IntPtr hdcMem = CreateCompatibleDC(IntPtr.Zero);
                if (hdcMem == IntPtr.Zero) return;

                try
                {
                    var bmi = new BITMAPINFO();
                    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                    bmi.bmiHeader.biWidth = width;
                    bmi.bmiHeader.biHeight = -height; // Top-down
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = 0; // BI_RGB

                    IntPtr hBitmap = CreateDIBSection(hdcMem, ref bmi, DIB_RGB_COLORS, out IntPtr pBits, IntPtr.Zero, 0);
                    if (hBitmap != IntPtr.Zero && pBits != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr hOld = SelectObject(hdcMem, hBitmap);
                            try
                            {
                                Marshal.Copy(bgraPixels, 0, pBits, width * height * 4);
                                DrawIconEx(hdcMem, cursorX, cursorY, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                                Marshal.Copy(pBits, bgraPixels, 0, width * height * 4);
                            }
                            finally
                            {
                                SelectObject(hdcMem, hOld);
                            }
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
                finally
                {
                    DeleteDC(hdcMem);
                }
            }
            finally
            {
                if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            }
        }
        catch
        {
            // Cursor compositing is best-effort
        }
    }
}
