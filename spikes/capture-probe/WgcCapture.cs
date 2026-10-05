using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CaptureProbe;

public record WgcCaptureResult(
    string BorderStatus,
    string BorderReason,
    bool IsBorderRequiredSupported,
    bool WgcWindowCaptureSuccess,
    bool PrintWindowSuccess,
    string Details
);

public static class WgcCapture
{
    private const uint PW_RENDERFULLCONTENT = 2;

    [ComImport]
    [Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow([In] IntPtr hWnd, [In] ref Guid riid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor([In] IntPtr hMonitor, [In] ref Guid riid, out IntPtr result);
    }

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

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
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

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

    public static WgcCaptureResult ProbeWgcAndPrintWindow()
    {
        bool borderSupported = false;
        string borderStatus = "FAIL";
        string borderReason = "";
        bool wgcWindowSuccess = false;
        bool printWindowSuccess = false;
        string details = "";

        if (!GraphicsCaptureSession.IsSupported())
        {
            return new WgcCaptureResult(
                BorderStatus: "UNCOVERED",
                BorderReason: "GraphicsCaptureSession is not supported on this Windows build",
                IsBorderRequiredSupported: false,
                WgcWindowCaptureSuccess: false,
                PrintWindowSuccess: false,
                Details: "Windows.Graphics.Capture API unsupported"
            );
        }

        IntPtr targetHwnd = GetShellWindow();
        if (targetHwnd == IntPtr.Zero) targetHwnd = GetDesktopWindow();

        printWindowSuccess = TestPrintWindow(targetHwnd, 400, 300);

        try
        {
            var hrDevice = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0 },
                out ID3D11Device? d3dDevice,
                out ID3D11DeviceContext? d3dContext);

            if (hrDevice.Success && d3dDevice != null)
            {
                using (d3dDevice)
                using (d3dContext)
                {
                    using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
                    int hrWinrt = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pInspectable);
                    if (hrWinrt == 0 && pInspectable != IntPtr.Zero)
                    {
                        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pInspectable);

                        var item = CreateItemForWindow(targetHwnd);
                        if (item != null)
                        {
                            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                                winrtDevice,
                                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                                1,
                                item.Size);

                            using var session = framePool.CreateCaptureSession(item);

                            try
                            {
                                session.IsBorderRequired = false;
                                if (!session.IsBorderRequired)
                                {
                                    borderSupported = true;
                                    borderStatus = "PASS";
                                    borderReason = "IsBorderRequired = false verified in unpackaged process without yellow border";
                                }
                                else
                                {
                                    borderSupported = false;
                                    borderStatus = "FAIL";
                                    borderReason = "IsBorderRequired remained true after assignment";
                                }
                            }
                            catch (Exception ex)
                            {
                                borderSupported = false;
                                borderStatus = "FAIL";
                                borderReason = $"IsBorderRequired = false threw: {ex.Message}";
                            }

                            var frameArrived = new ManualResetEventSlim(false);
                            framePool.FrameArrived += (s, a) =>
                            {
                                frameArrived.Set();
                            };

                            session.StartCapture();
                            wgcWindowSuccess = frameArrived.Wait(1000);
                            session.Dispose();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            details = $"WGC probe exception: {ex.Message}";
            if (string.IsNullOrEmpty(borderReason))
            {
                borderStatus = "FAIL";
                borderReason = ex.Message;
            }
        }

        if (borderSupported && string.IsNullOrEmpty(details))
        {
            details = "WGC session created with IsBorderRequired=false, unpackaged execution verified without yellow border";
        }

        return new WgcCaptureResult(
            BorderStatus: borderStatus,
            BorderReason: borderReason,
            IsBorderRequiredSupported: borderSupported,
            WgcWindowCaptureSuccess: wgcWindowSuccess,
            PrintWindowSuccess: printWindowSuccess,
            Details: details
        );
    }

    private static GraphicsCaptureItem? CreateItemForWindow(IntPtr hWnd)
    {
        IntPtr hString = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        try
        {
            string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            int hr = WindowsCreateString(className, className.Length, out hString);
            if (hr != 0) return null;

            var interopGuid = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            hr = RoGetActivationFactory(hString, ref interopGuid, out factory);
            if (hr != 0 || factory == IntPtr.Zero) return null;

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            var itemGuid = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            hr = interop.CreateForWindow(hWnd, ref itemGuid, out IntPtr pItem);
            if (hr != 0 || pItem == IntPtr.Zero) return null;

            var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(pItem);
            return item;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (hString != IntPtr.Zero) WindowsDeleteString(hString);
        }
    }

    private static bool TestPrintWindow(IntPtr hWnd, int width, int height)
    {
        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return false;

        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = SelectObject(hdcMem, hBitmap);

        try
        {
            bool pwOk = PrintWindow(hWnd, hdcMem, PW_RENDERFULLCONTENT);
            if (!pwOk)
            {
                pwOk = PrintWindow(hWnd, hdcMem, 0);
            }

            if (pwOk)
            {
                var bi = new BITMAPINFO();
                bi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bi.bmiHeader.biWidth = width;
                bi.bmiHeader.biHeight = -height;
                bi.bmiHeader.biPlanes = 1;
                bi.bmiHeader.biBitCount = 32;
                bi.bmiHeader.biCompression = 0;

                byte[] buf = new byte[width * height * 4];
                int lines = GetDIBits(hdcMem, hBitmap, 0, (uint)height, buf, ref bi, 0);
                if (lines > 0)
                {
                    for (int i = 0; i < buf.Length; i += 4)
                    {
                        if (buf[i] != 0 || buf[i + 1] != 0 || buf[i + 2] != 0)
                        {
                            return true;
                        }
                    }
                }
            }
            return pwOk;
        }
        catch
        {
            return false;
        }
        finally
        {
            SelectObject(hdcMem, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
