// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;

namespace Lightshot.Platform.Windows.DesktopCover;

/// <summary>
/// Manages per-monitor borderless wallpaper cover windows that sit above the desktop icon
/// list view and below application windows to hide desktop icons in captures.
/// Fully click-through and torn down on cancel, completion, or error.
/// </summary>
public sealed class DesktopCover : IDisposable
{
    private readonly List<DesktopCoverWindow> _windows = [];
    private bool _disposed;

    public IReadOnlyList<IntPtr> WindowHandles
    {
        get
        {
            lock (_windows)
            {
                return _windows.Select(w => w.Handle).Where(h => h != IntPtr.Zero).ToList();
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_windows)
            {
                return !_disposed && _windows.Count > 0;
            }
        }
    }

    /// <summary>
    /// Shows DesktopCover if the app.hideDesktopIcons setting is enabled.
    /// </summary>
    public static DesktopCover? CreateIfEnabled(ISettingsStore settings)
    {
        if (!settings.HideDesktopIcons)
        {
            return null;
        }
        return Show();
    }

    /// <summary>
    /// Unconditionally creates and shows cover windows for all monitors.
    /// </summary>
    public static DesktopCover Show()
    {
        var cover = new DesktopCover();
        cover.Initialize();
        return cover;
    }

    private void Initialize()
    {
        var wallpapers = WallpaperProvider.GetWallpapers();
        if (wallpapers.Count == 0)
        {
            var displays = DisplayTopology.GetDisplays();
            foreach (var d in displays)
            {
                var win = new DesktopCoverWindow((int)d.Bounds.X, (int)d.Bounds.Y, (int)d.Bounds.Width, (int)d.Bounds.Height, null, DesktopWallpaperPosition.Fill, 0);
                _windows.Add(win);
            }
        }
        else
        {
            foreach (var wp in wallpapers)
            {
                var win = new DesktopCoverWindow(wp.X, wp.Y, wp.Width, wp.Height, wp.ImagePath, wp.Position, wp.BackgroundColor);
                _windows.Add(win);
            }
        }

        foreach (var w in _windows)
        {
            w.Show();
        }

        // Wait for two DWM flushes to guarantee presentation without tearing (spec risk 99)
        FlushDwm();
    }

    private static void FlushDwm()
    {
        try
        {
            global::Windows.Win32.PInvoke.DwmFlush();
            global::Windows.Win32.PInvoke.DwmFlush();
        }
        catch
        {
            // Non-fatal if DWM flush fails in headless/test environments
        }
    }

    /// <summary>
    /// Explicit teardown method matching spec nomenclature.
    /// </summary>
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        lock (_windows)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var w in _windows)
            {
                try
                {
                    w.Dispose();
                }
                catch
                {
                    // Ignore disposal errors on individual windows
                }
            }
            _windows.Clear();
        }
    }
}

/// <summary>
/// A Win32 borderless, click-through, non-activating window displaying monitor wallpaper.
/// </summary>
internal sealed class DesktopCoverWindow : IDisposable
{
    private const string ClassName = "LightshotDesktopCoverWindow";
    private static bool _classRegistered;
    private static WndProcDelegate? _staticWndProc;

    private readonly int _x;
    private readonly int _y;
    private readonly int _width;
    private readonly int _height;
    private readonly string? _imagePath;
    private readonly DesktopWallpaperPosition _position;
    private readonly uint _backgroundColor;

    private IntPtr _hwnd = IntPtr.Zero;
    private SKBitmap? _wallpaperBitmap;
    private byte[]? _pixelBuffer;
    private bool _disposed;

    public IntPtr Handle => _hwnd;

    public DesktopCoverWindow(int x, int y, int width, int height, string? imagePath, DesktopWallpaperPosition position, uint backgroundColor)
    {
        _x = x;
        _y = y;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _imagePath = imagePath;
        _position = position;
        _backgroundColor = backgroundColor;

        LoadBitmap();
        CreateHwnd();
    }

    private void LoadBitmap()
    {
        if (!string.IsNullOrEmpty(_imagePath) && File.Exists(_imagePath))
        {
            try
            {
                _wallpaperBitmap = SKBitmap.Decode(_imagePath);
            }
            catch
            {
                _wallpaperBitmap = null;
            }
        }
    }

    private void CreateHwnd()
    {
        EnsureClassRegistered();

        // WS_POPUP | WS_VISIBLE
        uint style = 0x80000000 | 0x10000000;
        // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
        uint exStyle = 0x00000080 | 0x08000000 | 0x00000020;

        _hwnd = CreateWindowExW(
            exStyle,
            ClassName,
            "Lightshot Desktop Cover",
            style,
            _x,
            _y,
            _width,
            _height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero
        );
    }

    public void Show()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Position window just above desktop icons (SHELLDLL_DefView) or at HWND_BOTTOM
        IntPtr hInsertAfter = FindDesktopListView() ?? (IntPtr)1 /* HWND_BOTTOM */;

        // SWP_NOACTIVATE (0x0010) | SWP_SHOWWINDOW (0x0040)
        SetWindowPos(_hwnd, hInsertAfter, _x, _y, _width, _height, 0x0010 | 0x0040);
        InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    private static IntPtr? FindDesktopListView()
    {
        try
        {
            IntPtr progman = FindWindowW("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr defView = FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero) return defView;
            }

            // In Windows 10/11, SHELLDLL_DefView is often in a WorkerW sibling
            IntPtr workerW = IntPtr.Zero;
            while ((workerW = FindWindowExW(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero)
            {
                IntPtr defView = FindWindowExW(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero) return defView;
            }
        }
        catch
        {
            // Fall back to HWND_BOTTOM
        }
        return null;
    }

    internal void OnPaint(IntPtr hWnd)
    {
        PAINTSTRUCT ps;
        IntPtr hdc = BeginPaint(hWnd, out ps);
        if (hdc == IntPtr.Zero) return;

        try
        {
            int w = _width;
            int h = _height;

            var imageInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            int stride = imageInfo.RowBytes;
            int bufferSize = stride * h;

            if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize)
            {
                _pixelBuffer = new byte[bufferSize];
            }

            unsafe
            {
                fixed (byte* pPixels = _pixelBuffer)
                {
                    using (var surface = SKSurface.Create(imageInfo, (IntPtr)pPixels, stride))
                    {
                        if (surface != null)
                        {
                            RenderWallpaper(surface.Canvas, w, h, _wallpaperBitmap, _position, _backgroundColor);
                        }
                    }

                    BITMAPINFO bmi = default;
                    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                    bmi.bmiHeader.biWidth = w;
                    bmi.bmiHeader.biHeight = -h; // Top-down
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = 0; // BI_RGB

                    SetDIBitsToDevice(
                        hdc,
                        0, 0, (uint)w, (uint)h,
                        0, 0, 0, (uint)h,
                        (IntPtr)pPixels,
                        ref bmi,
                        0 // DIB_RGB_COLORS
                    );
                }
            }
        }
        finally
        {
            EndPaint(hWnd, ref ps);
        }
    }

    /// <summary>
    /// Pure rendering logic for drawing wallpaper with fit, fill, stretch, tile, or center alignment.
    /// </summary>
    public static void RenderWallpaper(SKCanvas canvas, int width, int height, SKBitmap? bitmap, DesktopWallpaperPosition position, uint backgroundColor)
    {
        // Decode COLORREF (0x00bbggrr) to Skia color
        byte r = (byte)(backgroundColor & 0xFF);
        byte g = (byte)((backgroundColor >> 8) & 0xFF);
        byte b = (byte)((backgroundColor >> 16) & 0xFF);
        canvas.Clear(new SKColor(r, g, b, 255));

        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return;
        }

        switch (position)
        {
            case DesktopWallpaperPosition.Center:
            {
                float x = (width - bitmap.Width) / 2f;
                float y = (height - bitmap.Height) / 2f;
                canvas.DrawBitmap(bitmap, x, y);
                break;
            }
            case DesktopWallpaperPosition.Stretch:
            {
                canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height));
                break;
            }
            case DesktopWallpaperPosition.Tile:
            {
                using var shader = SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(0, 0, width, height, paint);
                break;
            }
            case DesktopWallpaperPosition.Fit:
            {
                float scale = Math.Min((float)width / bitmap.Width, (float)height / bitmap.Height);
                float destW = bitmap.Width * scale;
                float destH = bitmap.Height * scale;
                float destX = (width - destW) / 2f;
                float destY = (height - destH) / 2f;
                canvas.DrawBitmap(bitmap, new SKRect(destX, destY, destX + destW, destY + destH));
                break;
            }
            case DesktopWallpaperPosition.Fill:
            case DesktopWallpaperPosition.Span:
            default:
            {
                float scale = Math.Max((float)width / bitmap.Width, (float)height / bitmap.Height);
                float destW = bitmap.Width * scale;
                float destH = bitmap.Height * scale;
                float destX = (width - destW) / 2f;
                float destY = (height - destH) / 2f;
                canvas.DrawBitmap(bitmap, new SKRect(destX, destY, destX + destW, destY + destH));
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _wallpaperBitmap?.Dispose();
        _wallpaperBitmap = null;
        _pixelBuffer = null;
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        _staticWndProc = WndProc;
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 0x0002 | 0x0001, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_staticWndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = ClassName
        };

        RegisterClassExW(ref wc);
        _classRegistered = true;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_PAINT = 0x000F;
        const uint WM_ERASEBKGND = 0x0014;
        const uint WM_NCHITTEST = 0x0084;
        const IntPtr HTTRANSPARENT = (IntPtr)(-1);

        switch (msg)
        {
            case WM_ERASEBKGND:
                return (IntPtr)1; // Suppress default erase

            case WM_PAINT:
                // Find instance or paint default
                PAINTSTRUCT ps;
                IntPtr hdc = BeginPaint(hWnd, out ps);
                EndPaint(hWnd, ref ps);
                return IntPtr.Zero;

            case WM_NCHITTEST:
                return HTTRANSPARENT; // Click-through
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X, int Y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetDIBitsToDevice(
        IntPtr hdc,
        int xDest, int yDest, uint w, uint h,
        int xSrc, int ySrc, uint uStartScan, uint cScanLines,
        IntPtr lpvBits,
        ref BITMAPINFO lpbmi,
        uint fuColorUse);
}
