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
    int YellowPixelsBorderOff,
    int YellowPixelsBorderOn,
    int BandSizePx,
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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

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
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

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

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr hBrushBlack = CreateSolidBrush(0x00000000); // Black
    private static readonly IntPtr hBrushBlue = CreateSolidBrush(0x00FF0000);  // Blue (COLORREF 0x00bbggrr -> B=255, G=0, R=0)

    private static IntPtr WindowProcBackdrop(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == 0x000F) // WM_PAINT
        {
            IntPtr hdc = BeginPaint(hWnd, out var ps);
            FillRect(hdc, ref ps.rcPaint, hBrushBlack);
            EndPaint(hWnd, ref ps);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    private static IntPtr WindowProcTarget(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == 0x000F) // WM_PAINT
        {
            IntPtr hdc = BeginPaint(hWnd, out var ps);
            FillRect(hdc, ref ps.rcPaint, hBrushBlue);
            EndPaint(hWnd, ref ps);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    private static readonly WndProcDelegate procBackdrop = WindowProcBackdrop;
    private static readonly WndProcDelegate procTarget = WindowProcTarget;

    private static void PumpMessages(int iterations)
    {
        for (int p = 0; p < iterations; p++)
        {
            while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            Thread.Sleep(5);
        }
    }

    public static WgcCaptureResult ProbeWgcAndPrintWindow()
    {
        bool borderSupported = false;
        string borderStatus = "FAIL";
        string borderReason = "";
        bool wgcWindowSuccess = false;
        bool printWindowSuccess = false;
        int yellowOff = 0;
        int yellowOn = 0;
        const int bandSize = 5;
        string details = "";

        if (!GraphicsCaptureSession.IsSupported())
        {
            return new WgcCaptureResult(
                BorderStatus: "UNCOVERED",
                BorderReason: "GraphicsCaptureSession is not supported on this Windows build",
                IsBorderRequiredSupported: false,
                WgcWindowCaptureSuccess: false,
                PrintWindowSuccess: false,
                YellowPixelsBorderOff: 0,
                YellowPixelsBorderOn: 0,
                BandSizePx: bandSize,
                Details: "Windows.Graphics.Capture API unsupported"
            );
        }

        IntPtr hInst = GetModuleHandleW(null);
        string clsBackdrop = $"LightshotWgcBackdrop_{Guid.NewGuid():N}";
        string clsTarget = $"LightshotWgcTarget_{Guid.NewGuid():N}";

        var wcBackdrop = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 3,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procBackdrop),
            hInstance = hInst,
            hbrBackground = hBrushBlack,
            lpszClassName = clsBackdrop
        };
        RegisterClassExW(ref wcBackdrop);

        var wcTarget = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 3,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(procTarget),
            hInstance = hInst,
            hbrBackground = hBrushBlue,
            lpszClassName = clsTarget
        };
        RegisterClassExW(ref wcTarget);

        const int targetX = 350;
        const int targetY = 350;
        const int targetW = 300;
        const int targetH = 200;

        const int margin = 30;
        const int backdropX = targetX - margin;
        const int backdropY = targetY - margin;
        const int backdropW = targetW + margin * 2;
        const int backdropH = targetH + margin * 2;

        IntPtr hWndBackdrop = IntPtr.Zero;
        IntPtr hWndTarget = IntPtr.Zero;

        try
        {
            hWndBackdrop = CreateWindowExW(
                0x00000008 | 0x00000080, // WS_EX_TOPMOST | WS_EX_TOOLWINDOW
                clsBackdrop, "WgcBackdrop",
                0x80000000 | 0x10000000, // WS_POPUP | WS_VISIBLE
                backdropX, backdropY, backdropW, backdropH,
                IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

            hWndTarget = CreateWindowExW(
                0x00000008 | 0x00000080, // WS_EX_TOPMOST | WS_EX_TOOLWINDOW
                clsTarget, "WgcTarget",
                0x80000000 | 0x10000000, // WS_POPUP | WS_VISIBLE
                targetX, targetY, targetW, targetH,
                IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

            ShowWindow(hWndBackdrop, 5); // SW_SHOW
            UpdateWindow(hWndBackdrop);
            ShowWindow(hWndTarget, 5); // SW_SHOW
            UpdateWindow(hWndTarget);
            SetForegroundWindow(hWndTarget);

            PumpMessages(10);

            // Test PrintWindow with PW_RENDERFULLCONTENT on solid blue target
            printWindowSuccess = TestPrintWindow(hWndTarget, targetW, targetH);

            var hrDevice = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0 },
                out ID3D11Device? d3dDevice,
                out ID3D11DeviceContext? d3dContext);

            if (hrDevice.Success && d3dDevice != null && d3dContext != null)
            {
                using (d3dDevice)
                using (d3dContext)
                {
                    using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                    factory.EnumAdapters1(0, out var adapter);
                    adapter!.EnumOutputs(0, out var output);
                    using var output1 = output!.QueryInterface<IDXGIOutput1>();
                    using var dup = output1.DuplicateOutput(d3dDevice);

                    var coords = output.Description.DesktopCoordinates;
                    int outW = coords.Right - coords.Left;
                    int outH = coords.Bottom - coords.Top;

                    var sDesc = new Texture2DDescription
                    {
                        Width = (uint)outW,
                        Height = (uint)outH,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None
                    };
                    using var staging = d3dDevice.CreateTexture2D(sDesc);

                    using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
                    int hrWinrt = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pInspectable);
                    if (hrWinrt == 0 && pInspectable != IntPtr.Zero)
                    {
                        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pInspectable);
                        var item = CreateItemForWindow(hWndTarget);

                        if (item != null)
                        {
                            // Drain initial frames from DDA
                            while (dup.AcquireNextFrame(0, out _, out var drainRes).Success)
                            {
                                drainRes?.Dispose();
                                dup.ReleaseFrame();
                            }

                            // 1. Positive Control: IsBorderRequired = true
                            using (var framePoolOn = Direct3D11CaptureFramePool.CreateFreeThreaded(
                                winrtDevice,
                                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                                2,
                                item.Size))
                            using (var sessionOn = framePoolOn.CreateCaptureSession(item))
                            {
                                sessionOn.IsBorderRequired = true;
                                var frameArrivedOn = new ManualResetEventSlim(false);
                                framePoolOn.FrameArrived += (s, a) => frameArrivedOn.Set();
                                sessionOn.StartCapture();
                                bool gotOn = frameArrivedOn.Wait(2000);
                                PumpMessages(5);
                                Thread.Sleep(80);

                                yellowOn = CountYellowPixelsInBand(dup, d3dContext, staging, targetX, targetY, targetX + targetW, targetY + targetH, bandSize);
                            }

                            PumpMessages(5);
                            Thread.Sleep(80);

                            // Drain DDA queue
                            while (dup.AcquireNextFrame(0, out _, out var drainRes).Success)
                            {
                                drainRes?.Dispose();
                                dup.ReleaseFrame();
                            }

                            // 2. Test Case: IsBorderRequired = false
                            using (var framePoolOff = Direct3D11CaptureFramePool.CreateFreeThreaded(
                                winrtDevice,
                                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                                2,
                                item.Size))
                            using (var sessionOff = framePoolOff.CreateCaptureSession(item))
                            {
                                try
                                {
                                    sessionOff.IsBorderRequired = false;
                                    borderSupported = !sessionOff.IsBorderRequired;
                                }
                                catch
                                {
                                    borderSupported = false;
                                }

                                var frameArrivedOff = new ManualResetEventSlim(false);
                                framePoolOff.FrameArrived += (s, a) => frameArrivedOff.Set();
                                sessionOff.StartCapture();
                                wgcWindowSuccess = frameArrivedOff.Wait(2000);
                                PumpMessages(5);
                                Thread.Sleep(80);

                                yellowOff = CountYellowPixelsInBand(dup, d3dContext, staging, targetX, targetY, targetX + targetW, targetY + targetH, bandSize);
                            }

                            // Evaluate criteria per rule:
                            // "the control must detect the border, or the detector is invalid and the criterion is UNCOVERED (never PASS)"
                            if (yellowOn == 0)
                            {
                                borderStatus = "UNCOVERED";
                                borderReason = $"Positive control (IsBorderRequired=true) detected 0 yellow pixels in {bandSize}px band; border detector invalid or DWM border not rendered in this environment";
                            }
                            else if (yellowOff == 0)
                            {
                                borderStatus = "PASS";
                                borderReason = $"Pixel check verified: 0 yellow border pixels with IsBorderRequired=false vs {yellowOn} yellow pixels with IsBorderRequired=true ({bandSize}px band)";
                            }
                            else
                            {
                                borderStatus = "FAIL";
                                borderReason = $"Yellow border detected despite IsBorderRequired=false ({yellowOff} yellow pixels in {bandSize}px band, control had {yellowOn})";
                            }

                            details = $"WGC border check: border-off yellow pixels={yellowOff}, positive control yellow pixels={yellowOn}, band={bandSize}px, IsBorderRequiredSupported={borderSupported}, WGC window capture={wgcWindowSuccess}";
                        }
                    }
                    output.Dispose();
                    adapter.Dispose();
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
        finally
        {
            if (hWndTarget != IntPtr.Zero) DestroyWindow(hWndTarget);
            if (hWndBackdrop != IntPtr.Zero) DestroyWindow(hWndBackdrop);
        }

        return new WgcCaptureResult(
            BorderStatus: borderStatus,
            BorderReason: borderReason,
            IsBorderRequiredSupported: borderSupported,
            WgcWindowCaptureSuccess: wgcWindowSuccess,
            PrintWindowSuccess: printWindowSuccess,
            YellowPixelsBorderOff: yellowOff,
            YellowPixelsBorderOn: yellowOn,
            BandSizePx: bandSize,
            Details: details
        );
    }

    private static int CountYellowPixelsInBand(
        IDXGIOutputDuplication dup,
        ID3D11DeviceContext context,
        ID3D11Texture2D staging,
        int left, int top, int right, int bottom,
        int bandSize)
    {
        int maxCount = 0;
        for (int retry = 0; retry < 5; retry++)
        {
            mouse_event(0x0001, 2, 2, 0, UIntPtr.Zero);
            var hr = dup.AcquireNextFrame(200, out var info, out var res);
            if (hr.Success && res != null)
            {
                using (res)
                {
                    using var tex = res.QueryInterface<ID3D11Texture2D>();
                    context.CopyResource(staging, tex);
                    var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    int count = 0;
                    try
                    {
                        unsafe
                        {
                            byte* ptr = (byte*)mapped.DataPointer;
                            int rowPitch = (int)mapped.RowPitch;

                            int yMin = Math.Max(0, top - bandSize);
                            int yMax = bottom + bandSize;
                            int xMin = Math.Max(0, left - bandSize);
                            int xMax = right + bandSize;

                            int innerLeft = left + bandSize;
                            int innerRight = right - bandSize;
                            int innerTop = top + bandSize;
                            int innerBottom = bottom - bandSize;

                            for (int y = yMin; y <= yMax; y++)
                            {
                                for (int x = xMin; x <= xMax; x++)
                                {
                                    // Skip interior of window
                                    if (x > innerLeft && x < innerRight && y > innerTop && y < innerBottom)
                                    {
                                        continue;
                                    }

                                    int idx = y * rowPitch + x * 4;
                                    byte b = ptr[idx];
                                    byte g = ptr[idx + 1];
                                    byte r = ptr[idx + 2];

                                    // Yellow / gold capture border:
                                    // High red, high green, low blue, distinct from black or blue window content
                                    if (r > 180 && g > 150 && b < 100 && r > (b + 50) && g > (b + 40))
                                    {
                                        count++;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        context.Unmap(staging, 0);
                        dup.ReleaseFrame();
                    }

                    maxCount = Math.Max(maxCount, count);
                }
            }
            Thread.Sleep(20);
        }
        return maxCount;
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
