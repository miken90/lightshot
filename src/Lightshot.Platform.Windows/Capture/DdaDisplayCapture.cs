// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// Full display still capture using Desktop Duplication API (DDA) on the output's owning DXGI adapter,
/// with GDI fallback if DDA is unsupported or unavailable.
/// </summary>
public static class DdaDisplayCapture
{
    private const int DXGI_ERROR_UNSUPPORTED = unchecked((int)0x887A0004);
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint DIB_RGB_COLORS = 0;

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

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    /// <summary>
    /// Captures the specified display at native physical resolution.
    /// </summary>
    public static Result<CapturedImage, CaptureError> CaptureDisplay(DisplayInfo display)
    {
        if (display == null)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable());
        }

        // 1. Try DDA capture first
        try
        {
            var ddaResult = TryCaptureDda(display);
            if (ddaResult.IsSuccess)
            {
                return ddaResult;
            }
        }
        catch
        {
            // DDA failed or unsupported, proceed to GDI fallback
        }

        // 2. GDI BitBlt fallback
        return CaptureDisplayGdi(display);
    }

    private static Result<CapturedImage, CaptureError> TryCaptureDda(DisplayInfo display)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        if (factory == null)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("DXGIFactory1 creation failed"));
        }

        IDXGIAdapter1? targetAdapter = null;
        IDXGIOutput? targetOutput = null;

        try
        {
            // Search for adapter and output matching this display
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                if (adapter == null) continue;
                for (uint j = 0; adapter.EnumOutputs(j, out var output).Success; j++)
                {
                    if (output == null) continue;
                    if (string.Equals(output.Description.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetAdapter = adapter;
                        targetOutput = output;
                        break;
                    }
                    output.Dispose();
                }
                if (targetOutput != null) break;
                adapter.Dispose();
            }

            if (targetAdapter == null || targetOutput == null)
            {
                return Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable());
            }

            var hrDevice = D3D11.D3D11CreateDevice(
                targetAdapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_11_1 },
                out ID3D11Device? device,
                out ID3D11DeviceContext? context);

            if (!hrDevice.Success || device == null || context == null)
            {
                return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure($"D3D11CreateDevice failed: {hrDevice}"));
            }

            using (device)
            using (context)
            {
                using var output1 = targetOutput.QueryInterface<IDXGIOutput1>();
                using var duplication = output1.DuplicateOutput(device);

                var coords = targetOutput.Description.DesktopCoordinates;
                int width = coords.Right - coords.Left;
                int height = coords.Bottom - coords.Top;

                if (width <= 0 || height <= 0)
                {
                    width = display.PhysicalWidth;
                    height = display.PhysicalHeight;
                }

                // Retry acquiring frame up to 5 times (nudge mouse on retry)
                for (int retry = 0; retry < 5; retry++)
                {
                    if (retry > 0)
                    {
                        mouse_event(0x0001, 1, 1, 0, UIntPtr.Zero);
                        Thread.Sleep(10);
                    }

                    var hrFrame = duplication.AcquireNextFrame(200, out var info, out var desktopResource);
                    if (hrFrame.Success && desktopResource != null)
                    {
                        using (desktopResource)
                        {
                            using var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
                            var desc = texture.Description;

                            var stagingDesc = new Texture2DDescription
                            {
                                Width = desc.Width,
                                Height = desc.Height,
                                MipLevels = 1,
                                ArraySize = 1,
                                Format = desc.Format,
                                SampleDescription = new SampleDescription(1, 0),
                                Usage = ResourceUsage.Staging,
                                BindFlags = BindFlags.None,
                                CPUAccessFlags = CpuAccessFlags.Read,
                                MiscFlags = ResourceOptionFlags.None
                            };

                            using var staging = device.CreateTexture2D(stagingDesc);
                            context.CopyResource(staging, texture);

                            var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                            try
                            {
                                byte[] pixels = new byte[desc.Width * desc.Height * 4];
                                int rowPitch = (int)mapped.RowPitch;
                                unsafe
                                {
                                    byte* src = (byte*)mapped.DataPointer;
                                    fixed (byte* dst = pixels)
                                    {
                                        for (int y = 0; y < (int)desc.Height; y++)
                                        {
                                            Buffer.MemoryCopy(src + y * rowPitch, dst + y * desc.Width * 4, desc.Width * 4, desc.Width * 4);
                                        }
                                    }
                                }

                                if (BlackFrameDetector.IsBlackFrame(pixels, (int)desc.Width, (int)desc.Height))
                                {
                                    return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Black frame detected from display capture"));
                                }

                                return Result<CapturedImage, CaptureError>.Success(new CapturedImage((int)desc.Width, (int)desc.Height, pixels));
                            }
                            finally
                            {
                                context.Unmap(staging, 0);
                                duplication.ReleaseFrame();
                            }
                        }
                    }
                }

                return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("DDA frame acquisition timed out"));
            }
        }
        finally
        {
            targetOutput?.Dispose();
            targetAdapter?.Dispose();
        }
    }

    private static Result<CapturedImage, CaptureError> CaptureDisplayGdi(DisplayInfo display)
    {
        int width = display.PhysicalWidth;
        int height = display.PhysicalHeight;
        int x = (int)Math.Round(display.Bounds.X);
        int y = (int)Math.Round(display.Bounds.Y);

        if (width <= 0 || height <= 0)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Invalid display bounds for GDI capture"));
        }

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable());
        }

        try
        {
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero)
            {
                return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("CreateCompatibleDC failed"));
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
                    return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("CreateDIBSection failed"));
                }

                try
                {
                    IntPtr hOld = SelectObject(hdcMem, hBitmap);
                    try
                    {
                        bool success = BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, SRCCOPY | CAPTUREBLT);
                        if (!success)
                        {
                            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("BitBlt failed"));
                        }

                        byte[] pixels = new byte[width * height * 4];
                        Marshal.Copy(pBits, pixels, 0, pixels.Length);

                        // Ensure alpha is opaque 255
                        for (int i = 3; i < pixels.Length; i += 4)
                        {
                            pixels[i] = 255;
                        }

                        if (BlackFrameDetector.IsBlackFrame(pixels, width, height))
                        {
                            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Black frame detected from display"));
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
