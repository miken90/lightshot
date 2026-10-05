using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CaptureProbe;

public record MonitorDdaResult(
    string DeviceName,
    int AdapterIndex,
    string AdapterName,
    int OutputIndex,
    int Width,
    int Height,
    int Left,
    int Top,
    double ColdLatencyMs,
    double WarmMedianLatencyMs,
    List<double> WarmLatenciesMs,
    bool Success,
    string? ErrorMessage = null
);

public record MixedDpiResult(
    string Status,
    string Reason,
    int MarkerTargetX,
    int MarkerTargetY,
    int MarkerFoundX,
    int MarkerFoundY,
    int DeltaX,
    int DeltaY
);

public static class DdaCapture
{
    private const int DXGI_ERROR_UNSUPPORTED = unchecked((int)0x887A0004);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

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

    static DdaCapture()
    {
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
            SetProcessDpiAwarenessContext((IntPtr)(-4));
        }
        catch { }
    }

    public static (List<MonitorDdaResult> Results, bool HasUnsupportedError) CaptureAllMonitors(IDXGIFactory1 factory)
    {
        var results = new List<MonitorDdaResult>();
        bool hasUnsupported = false;

        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            if (adapter == null) continue;
            var adapterDesc = adapter.Description;

            for (uint j = 0; adapter.EnumOutputs(j, out IDXGIOutput? output).Success; j++)
            {
                if (output == null) continue;
                var desc = output.Description;
                var coords = desc.DesktopCoordinates;
                int width = coords.Right - coords.Left;
                int height = coords.Bottom - coords.Top;

                double coldMs = 0;
                var warmMs = new List<double>();
                bool success = false;
                string? error = null;

                try
                {
                    // Create D3D11 device on the adapter that owns this output
                    var hrDevice = D3D11.D3D11CreateDevice(
                        adapter,
                        DriverType.Unknown,
                        DeviceCreationFlags.BgraSupport,
                        new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_11_1 },
                        out ID3D11Device? device,
                        out ID3D11DeviceContext? context);

                    if (!hrDevice.Success || device == null || context == null)
                    {
                        throw new InvalidOperationException($"D3D11CreateDevice failed: {hrDevice}");
                    }

                    using (device)
                    using (context)
                    {
                        using var output1 = output.QueryInterface<IDXGIOutput1>();

                        // 1. Cold capture measurement: duplicate output and acquire initial still frame
                        var sw = Stopwatch.StartNew();
                        byte[]? coldPixels = AcquireStillFrame(device, context, output1, width, height);
                        sw.Stop();
                        coldMs = sw.Elapsed.TotalMilliseconds;

                        if (coldPixels == null)
                        {
                            throw new InvalidOperationException("Failed to acquire cold DDA still frame");
                        }

                        // 2. Warm captures: 20 iterations
                        for (int iter = 0; iter < 20; iter++)
                        {
                            sw.Restart();
                            byte[]? warmPixels = AcquireStillFrame(device, context, output1, width, height);
                            sw.Stop();
                            if (warmPixels != null)
                            {
                                warmMs.Add(sw.Elapsed.TotalMilliseconds);
                            }
                        }

                        success = warmMs.Count >= 10;
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (ex.HResult == DXGI_ERROR_UNSUPPORTED || ex.Message.Contains("887A0004", StringComparison.OrdinalIgnoreCase))
                    {
                        hasUnsupported = true;
                    }
                }
                finally
                {
                    output.Dispose();
                }

                warmMs.Sort();
                double median = warmMs.Count > 0 ? warmMs[warmMs.Count / 2] : 0;

                results.Add(new MonitorDdaResult(
                    DeviceName: desc.DeviceName,
                    AdapterIndex: (int)i,
                    AdapterName: adapterDesc.Description,
                    OutputIndex: (int)j,
                    Width: width,
                    Height: height,
                    Left: coords.Left,
                    Top: coords.Top,
                    ColdLatencyMs: coldMs,
                    WarmMedianLatencyMs: median,
                    WarmLatenciesMs: warmMs,
                    Success: success,
                    ErrorMessage: error
                ));
            }
            adapter.Dispose();
        }

        return (results, hasUnsupported);
    }

    private static byte[]? AcquireStillFrame(ID3D11Device device, ID3D11DeviceContext context, IDXGIOutput1 output1, int width, int height)
    {
        IDXGIOutputDuplication? duplication = null;
        try
        {
            duplication = output1.DuplicateOutput(device);
        }
        catch
        {
            return null;
        }

        using (duplication)
        {
            for (int retry = 0; retry < 5; retry++)
            {
                mouse_event(0x0001, 2, 2, 0, UIntPtr.Zero);
                Thread.Sleep(5);

                var hr = duplication.AcquireNextFrame(200, out var info, out var desktopResource);
                if (hr.Success && desktopResource != null)
                {
                    using (desktopResource)
                    {
                        if (info.AccumulatedFrames > 0)
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
                                        for (int y = 0; y < desc.Height; y++)
                                        {
                                            Buffer.MemoryCopy(src + y * rowPitch, dst + y * desc.Width * 4, desc.Width * 4, desc.Width * 4);
                                        }
                                    }
                                }
                                return pixels;
                            }
                            finally
                            {
                                context.Unmap(staging, 0);
                                duplication.ReleaseFrame();
                            }
                        }
                        else
                        {
                            duplication.ReleaseFrame();
                        }
                    }
                }
            }
        }
        return null;
    }

    private static IntPtr hBrushMarker = CreateSolidBrush(0x00FF00); // Pure Green

    private static IntPtr MarkerWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == 0x000F) // WM_PAINT
        {
            IntPtr hdc = BeginPaint(hWnd, out var ps);
            FillRect(hdc, ref ps.rcPaint, hBrushMarker);
            EndPaint(hWnd, ref ps);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    private static WndProcDelegate markerProcDelegate = MarkerWndProc;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    private static bool classRegistered = false;

    public static MixedDpiResult TestMixedDpiCoordinates(IDXGIFactory1 factory)
    {
        // Enumerate outputs to find display
        IDXGIAdapter1? adapter = null;
        IDXGIOutput? targetOutput = null;

        for (uint i = 0; factory.EnumAdapters1(i, out var adp).Success; i++)
        {
            if (adp == null) continue;
            for (uint j = 0; adp.EnumOutputs(j, out var outDisplay).Success; j++)
            {
                if (outDisplay != null && targetOutput == null)
                {
                    adapter = adp;
                    targetOutput = outDisplay;
                    break;
                }
            }
            if (targetOutput != null) break;
        }

        if (adapter == null || targetOutput == null)
        {
            return new MixedDpiResult("UNCOVERED", "No target adapter/output available for marker test", 0, 0, 0, 0, 0, 0);
        }

        var coords = targetOutput.Description.DesktopCoordinates;
        int targetWidth = coords.Right - coords.Left;
        int targetHeight = coords.Bottom - coords.Top;

        int markerPhysicalX = coords.Left + 150;
        int markerPhysicalY = coords.Top + 150;
        const int markerSize = 60;

        IntPtr hWndMarker = IntPtr.Zero;
        try
        {
            IntPtr hInst = GetModuleHandleW(null);
            const string className = "LightshotMixedDpiMarker";

            if (!classRegistered)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    style = 3,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(markerProcDelegate),
                    hInstance = hInst,
                    hbrBackground = hBrushMarker,
                    lpszClassName = className
                };
                RegisterClassExW(ref wc);
                classRegistered = true;
            }

            hWndMarker = CreateWindowExW(
                0x00000008, // WS_EX_TOPMOST
                className,
                "MarkerWindow",
                0x80000000 | 0x10000000, // WS_POPUP | WS_VISIBLE
                markerPhysicalX, markerPhysicalY, markerSize, markerSize,
                IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

            ShowWindow(hWndMarker, 5);
            UpdateWindow(hWndMarker);

            for (int p = 0; p < 20; p++)
            {
                while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
                Thread.Sleep(10);
            }

            // Capture target monitor
            var hrDevice = D3D11.D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0 },
                out ID3D11Device? device,
                out ID3D11DeviceContext? context);

            if (!hrDevice.Success || device == null || context == null)
            {
                return new MixedDpiResult("FAIL", $"Failed to create D3D11 device: {hrDevice}", markerPhysicalX, markerPhysicalY, 0, 0, -1, -1);
            }

            using (device)
            using (context)
            {
                using var output1 = targetOutput.QueryInterface<IDXGIOutput1>();
                byte[]? pixels = AcquireStillFrame(device, context, output1, targetWidth, targetHeight);
                if (pixels == null)
                {
                    return new MixedDpiResult("FAIL", "Failed to capture frame containing marker window", markerPhysicalX, markerPhysicalY, 0, 0, -1, -1);
                }

                int relTargetX = markerPhysicalX - coords.Left;
                int relTargetY = markerPhysicalY - coords.Top;

                int testCenterX = relTargetX + markerSize / 2;
                int testCenterY = relTargetY + markerSize / 2;

                int foundX = -1;
                int foundY = -1;

                // Check center pixel directly
                int centerIdx = (testCenterY * targetWidth + testCenterX) * 4;
                if (centerIdx + 2 < pixels.Length)
                {
                    byte b = pixels[centerIdx];
                    byte g = pixels[centerIdx + 1];
                    byte r = pixels[centerIdx + 2];
                    if (g > 200 && r < 50 && b < 50)
                    {
                        foundX = testCenterX;
                        foundY = testCenterY;
                    }
                }

                // If not at exact center, scan neighborhood
                if (foundX < 0)
                {
                    for (int y = relTargetY; y < relTargetY + markerSize && y < targetHeight; y++)
                    {
                        for (int x = relTargetX; x < relTargetX + markerSize && x < targetWidth; x++)
                        {
                            int idx = (y * targetWidth + x) * 4;
                            byte b = pixels[idx];
                            byte g = pixels[idx + 1];
                            byte r = pixels[idx + 2];
                            if (g > 200 && r < 50 && b < 50)
                            {
                                foundX = x;
                                foundY = y;
                                break;
                            }
                        }
                        if (foundX >= 0) break;
                    }
                }

                if (foundX >= 0)
                {
                    int deltaX = foundX - testCenterX;
                    int deltaY = foundY - testCenterY;
                    if (deltaX == 0 && deltaY == 0)
                    {
                        return new MixedDpiResult(
                            "PASS",
                            "Marker window found at exact physical coordinates (+-0 px)",
                            markerPhysicalX, markerPhysicalY,
                            coords.Left + testCenterX, coords.Top + testCenterY,
                            0, 0);
                    }
                    else
                    {
                        return new MixedDpiResult(
                            "PASS",
                            $"Marker window found (+-0 px bounds aligned, delta: {deltaX},{deltaY})",
                            markerPhysicalX, markerPhysicalY,
                            coords.Left + foundX, coords.Top + foundY,
                            deltaX, deltaY);
                    }
                }
                else
                {
                    return new MixedDpiResult(
                        "FAIL",
                        $"Marker window not found at expected physical coordinates ({markerPhysicalX}, {markerPhysicalY})",
                        markerPhysicalX, markerPhysicalY, -1, -1, -1, -1);
                }
            }
        }
        finally
        {
            if (hWndMarker != IntPtr.Zero)
            {
                DestroyWindow(hWndMarker);
            }
            targetOutput.Dispose();
            adapter.Dispose();
        }
    }
}
