// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// Captures a still image of a specific window using PrintWindow with PW_RENDERFULLCONTENT.
/// Used for candidate windows in the window picker and occluded window stills.
/// </summary>
public static class PrintWindowCapture
{
    private const uint PW_CLIENTONLY = 1;
    private const uint PW_RENDERFULLCONTENT = 2;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

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
    /// Captures a still frame of the specified window.
    /// </summary>
    public static Result<CapturedImage, CaptureError> CaptureWindow(IntPtr hWnd, Rect? expectedBounds = null)
    {
        if (hWnd == IntPtr.Zero)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Invalid window handle"));
        }

        int width, height;
        if (expectedBounds.HasValue && expectedBounds.Value.Width > 0 && expectedBounds.Value.Height > 0)
        {
            width = (int)Math.Round(expectedBounds.Value.Width);
            height = (int)Math.Round(expectedBounds.Value.Height);
        }
        else
        {
            RECT rect;
            int hr = DwmGetWindowAttribute(hWnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out rect, Marshal.SizeOf<RECT>());
            if (hr != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            {
                if (!GetWindowRect(hWnd, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                {
                    return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Unable to determine window bounds"));
                }
            }
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        if (width <= 0 || height <= 0)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure($"Invalid window dimensions ({width}x{height})"));
        }

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Failed to acquire screen DC"));
        }

        try
        {
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
            {
                return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Failed to create compatible memory DC"));
            }

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
                if (hBitmap == IntPtr.Zero || pBits == IntPtr.Zero)
                {
                    return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Failed to create DIB section for window capture"));
                }

                try
                {
                    IntPtr hOld = SelectObject(hdcMem, hBitmap);
                    try
                    {
                        // 1. Try PW_RENDERFULLCONTENT first
                        bool success = PrintWindow(hWnd, hdcMem, PW_RENDERFULLCONTENT);
                        if (!success)
                        {
                            // 2. Fallback to default PrintWindow
                            success = PrintWindow(hWnd, hdcMem, 0);
                        }

                        if (!success)
                        {
                            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("PrintWindow failed on target window"));
                        }

                        byte[] pixels = new byte[width * height * 4];
                        Marshal.Copy(pBits, pixels, 0, pixels.Length);

                        // GDI PrintWindow often leaves the alpha channel 0. If all alphas are 0, set them to 255.
                        bool hasAlpha = false;
                        for (int i = 3; i < pixels.Length; i += 4)
                        {
                            if (pixels[i] != 0)
                            {
                                hasAlpha = true;
                                break;
                            }
                        }

                        if (!hasAlpha)
                        {
                            for (int i = 3; i < pixels.Length; i += 4)
                            {
                                pixels[i] = 255;
                            }
                        }

                        // Check for black (DRM / protected) frame
                        if (BlackFrameDetector.IsBlackFrame(pixels, width, height))
                        {
                            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Protected content or blank window frame detected"));
                        }

                        return Result<CapturedImage, CaptureError>.Success(new CapturedImage(width, height, pixels));
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
            finally
            {
                DeleteDC(hdcMem);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
