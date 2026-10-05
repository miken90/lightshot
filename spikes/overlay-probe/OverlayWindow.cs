using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectComposition;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OverlayProbe;

public record OverlayLatencyResult(
    string Status,
    double MedianLatencyMs,
    double MinLatencyMs,
    double MaxLatencyMs,
    List<double> IterationLatenciesMs,
    string Details
);

public record FocusAndInputResult(
    string FocusStatus,
    bool ForegroundObtained,
    bool FocusObtained,
    int KeysDelivered,
    int KeysExpected,
    string Details
);

public record CaptureExclusionResult(
    string Status,
    int ExcludedFramesCount,
    int TotalFramesCount,
    string Details
);

public class OverlayWindow : IDisposable
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const uint WM_PAINT = 0x000F;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

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

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

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

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private readonly List<IntPtr> _overlayHwnds = new();
    private readonly List<IDCompositionTarget> _dcompTargets = new();
    private readonly List<IDCompositionVisual> _dcompVisuals = new();
    private IDCompositionDevice? _dcompDevice;
    private readonly IntPtr _hBrushMagenta;
    private readonly WndProcDelegate _wndProc;
    private readonly string _className;

    private readonly List<int> _receivedKeys = new();
    public bool CanaryPainted { get; private set; }

    public IntPtr PrimaryHwnd => _overlayHwnds.Count > 0 ? _overlayHwnds[0] : IntPtr.Zero;

    public OverlayWindow()
    {
        try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }

        _hBrushMagenta = CreateSolidBrush(0xFF00FF); // Magenta in BGR (0xFF00FF: R=255, G=0, B=255)
        _wndProc = WndProc;
        _className = $"LightshotOverlayWindow_{Guid.NewGuid():N}";

        IntPtr hInst = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 3,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInst,
            hbrBackground = _hBrushMagenta,
            lpszClassName = _className
        };
        RegisterClassExW(ref wc);

        InitDComp();
    }

    private unsafe void InitDComp()
    {
        Guid iid = typeof(IDCompositionDevice).GUID;
        var hr = PInvoke.DCompositionCreateDevice(null, &iid, out object pDevObj);
        if (hr.Value == 0 && pDevObj is IDCompositionDevice dev)
        {
            _dcompDevice = dev;
        }
    }

    public void CreateWindowsForAllMonitors(IDXGIFactory1 factory)
    {
        IntPtr hInst = GetModuleHandleW(null);

        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            if (adapter == null) continue;
            for (uint j = 0; adapter.EnumOutputs(j, out var output).Success; j++)
            {
                if (output == null) continue;
                var coords = output.Description.DesktopCoordinates;
                int w = coords.Right - coords.Left;
                int h = coords.Bottom - coords.Top;

                IntPtr hWnd = CreateWindowExW(
                    WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
                    _className,
                    "LightshotOverlay",
                    WS_POPUP,
                    coords.Left, coords.Top, w, h,
                    IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

                if (hWnd != IntPtr.Zero)
                {
                    _overlayHwnds.Add(hWnd);

                    if (_dcompDevice != null)
                    {
                        try
                        {
                            _dcompDevice.CreateTargetForHwnd((HWND)hWnd, true, out var target);
                            _dcompDevice.CreateVisual(out var visual);
                            if (target != null && visual != null)
                            {
                                target.SetRoot(visual);
                                _dcompTargets.Add(target);
                                _dcompVisuals.Add(visual);
                            }
                        }
                        catch { }
                    }
                }
                output.Dispose();
            }
            adapter.Dispose();
        }

        if (_dcompDevice != null)
        {
            try { _dcompDevice.Commit(); } catch { }
        }
    }

    public void ShowOverlays()
    {
        foreach (var hWnd in _overlayHwnds)
        {
            ShowWindow(hWnd, 5); // SW_SHOW
            UpdateWindow(hWnd);
        }

        if (_dcompDevice != null)
        {
            try { _dcompDevice.Commit(); } catch { }
        }

        PumpMessages(5);
    }

    public void HideOverlays()
    {
        foreach (var hWnd in _overlayHwnds)
        {
            ShowWindow(hWnd, 0); // SW_HIDE
        }

        if (_dcompDevice != null)
        {
            try { _dcompDevice.Commit(); } catch { }
        }

        PumpMessages(5);
    }

    public FocusAndInputResult TestFocusAndKeys()
    {
        if (PrimaryHwnd == IntPtr.Zero)
        {
            return new FocusAndInputResult("FAIL", false, false, 0, 20, "No primary overlay window created");
        }

        ShowOverlays();

        // 1. Obtain foreground and focus
        SetForegroundWindow(PrimaryHwnd);
        SetFocus(PrimaryHwnd);
        PumpMessages(5);

        bool fg = GetForegroundWindow() == PrimaryHwnd;
        bool focus = GetFocus() == PrimaryHwnd;

        if (!fg)
        {
            // Fallback: AttachThreadInput + Alt key
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            uint curThread = GetCurrentThreadId();
            if (fgThread != curThread)
            {
                AttachThreadInput(curThread, fgThread, true);
                keybd_event(0x12 /* VK_MENU */, 0, 0, UIntPtr.Zero);
                SetForegroundWindow(PrimaryHwnd);
                keybd_event(0x12, 0, 0x0002, UIntPtr.Zero);
                AttachThreadInput(curThread, fgThread, false);
            }
            PumpMessages(5);
            fg = GetForegroundWindow() == PrimaryHwnd;
        }

        // 2. Test delivering 20 keys: Esc, Return, Arrow keys (Up, Down, Left, Right)
        byte[] testKeys = new byte[]
        {
            0x1B, // VK_ESCAPE
            0x0D, // VK_RETURN
            0x25, // VK_LEFT
            0x26, // VK_UP
            0x27, // VK_RIGHT
            0x28  // VK_DOWN
        };

        _receivedKeys.Clear();
        int keysToSend = 20;

        for (int i = 0; i < keysToSend; i++)
        {
            byte vk = testKeys[i % testKeys.Length];
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 0x0002, UIntPtr.Zero);
            PumpMessages(2);
            Thread.Sleep(5);
        }

        int delivered = _receivedKeys.Count;
        bool pass = delivered >= keysToSend && fg;

        HideOverlays();

        return new FocusAndInputResult(
            FocusStatus: pass ? "PASS" : "FAIL",
            ForegroundObtained: fg,
            FocusObtained: focus || fg,
            KeysDelivered: delivered,
            KeysExpected: keysToSend,
            Details: pass
                ? $"Foreground and focus obtained from WM_HOTKEY; {delivered}/{keysToSend} keys delivered"
                : $"Failed: Foreground={fg}, Focus={focus}, Delivered={delivered}/{keysToSend}"
        );
    }

    public CaptureExclusionResult TestCaptureExclusion(IDXGIFactory1 factory)
    {
        IntPtr hInst = GetModuleHandleW(null);
        IntPtr hWndCard = CreateWindowExW(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            _className, "CardExclusion",
            WS_POPUP | WS_VISIBLE,
            300, 300, 150, 150,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        IntPtr hWndPill = CreateWindowExW(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            _className, "PillExclusion",
            WS_POPUP | WS_VISIBLE,
            500, 300, 100, 50,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        try
        {
            // Apply WDA_EXCLUDEFROMCAPTURE to both
            SetWindowDisplayAffinity(hWndCard, WDA_EXCLUDEFROMCAPTURE);
            SetWindowDisplayAffinity(hWndPill, WDA_EXCLUDEFROMCAPTURE);

            ShowWindow(hWndCard, 5);
            ShowWindow(hWndPill, 5);
            UpdateWindow(hWndCard);
            UpdateWindow(hWndPill);
            PumpMessages(10);

            // Nudge mouse to ensure DWM updates
            mouse_event(0x0001, 2, 2, 0, UIntPtr.Zero);
            Thread.Sleep(20);

            // Setup DDA capture on primary display
            factory.EnumAdapters1(0, out var adapter);
            adapter!.EnumOutputs(0, out var output);
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 }, out ID3D11Device? device, out ID3D11DeviceContext? context);
            using var output1 = output!.QueryInterface<IDXGIOutput1>();

            int excludedCount = 0;
            const int totalFrames = 20;

            using var dup = output1.DuplicateOutput(device!);
            ID3D11Texture2D? staging = null;

            try
            {
                for (int f = 0; f < totalFrames; f++)
                {
                    mouse_event(0x0001, 1, 1, 0, UIntPtr.Zero);
                    Thread.Sleep(5);

                    var hr = dup.AcquireNextFrame(200, out var info, out var res);
                    if (hr.Success && res != null)
                    {
                        using (res)
                        {
                            using var tex = res.QueryInterface<ID3D11Texture2D>();
                            if (staging == null)
                            {
                                var sDesc = tex.Description;
                                sDesc.Usage = ResourceUsage.Staging;
                                sDesc.BindFlags = BindFlags.None;
                                sDesc.CPUAccessFlags = CpuAccessFlags.Read;
                                sDesc.MiscFlags = ResourceOptionFlags.None;
                                staging = device!.CreateTexture2D(sDesc);
                            }

                            context!.CopyResource(staging, tex);
                            var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

                            bool cardVisible = false;
                            unsafe
                            {
                                byte* ptr = (byte*)mapped.DataPointer;
                                // Check center of card (375, 375)
                                int idx = 375 * (int)mapped.RowPitch + 375 * 4;
                                byte b = ptr[idx]; byte g = ptr[idx + 1]; byte r = ptr[idx + 2];
                                // Magenta brush has R>200, G<50, B>200
                                if (r > 200 && g < 50 && b > 200)
                                {
                                    cardVisible = true;
                                }
                            }
                            context.Unmap(staging, 0);
                            dup.ReleaseFrame();

                            if (!cardVisible)
                            {
                                excludedCount++;
                            }
                        }
                    }
                }
            }
            finally
            {
                staging?.Dispose();
            }

            bool pass = excludedCount == totalFrames;
            return new CaptureExclusionResult(
                Status: pass ? "PASS" : "FAIL",
                ExcludedFramesCount: excludedCount,
                TotalFramesCount: totalFrames,
                Details: pass
                    ? $"WDA_EXCLUDEFROMCAPTURE verified: card and pill absent from {excludedCount}/{totalFrames} DDA frames"
                    : $"Capture exclusion failed: excluded from {excludedCount}/{totalFrames} frames"
            );
        }
        finally
        {
            if (hWndCard != IntPtr.Zero) DestroyWindow(hWndCard);
            if (hWndPill != IntPtr.Zero) DestroyWindow(hWndPill);
        }
    }

    public OverlayLatencyResult MeasureHotkeyToOverlayLatency(IDXGIFactory1 factory, Action triggerHotkey, int iterations = 20)
    {
        var latencies = new List<double>();

        factory.EnumAdapters1(0, out var adapter);
        adapter!.EnumOutputs(0, out var output);
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 }, out ID3D11Device? device, out ID3D11DeviceContext? context);
        using var output1 = output!.QueryInterface<IDXGIOutput1>();

        IDXGIOutputDuplication? dup = null;
        ID3D11Texture2D? staging = null;

        try
        {
            dup = output1.DuplicateOutput(device!);

            for (int iter = 0; iter < iterations; iter++)
            {
                HideOverlays();
                PumpMessages(5);
                Thread.Sleep(30);

                // Drain any pending frame in DDA queue
                while (dup.AcquireNextFrame(0, out _, out var drainRes).Success)
                {
                    drainRes?.Dispose();
                    dup.ReleaseFrame();
                }

                CanaryPainted = false;
                long qpcStart = Stopwatch.GetTimestamp();

                // Trigger hotkey (sends SendInput test chord)
                triggerHotkey();

                // Measure time until first DWM-presented frame containing the overlay is captured via DDA
                double elapsedMs = 0;
                const int maxPollMs = 500;
                var pollSw = Stopwatch.StartNew();
                bool frameFound = false;

                while (pollSw.ElapsedMilliseconds < maxPollMs)
                {
                    PumpMessages(1);
                    mouse_event(0x0001, 1, 1, 0, UIntPtr.Zero);

                    var hr = dup.AcquireNextFrame(16, out var info, out var res);
                    if (hr.Success && res != null)
                    {
                        using (res)
                        {
                            long qpcCaptured = Stopwatch.GetTimestamp();
                            using var tex = res.QueryInterface<ID3D11Texture2D>();
                            if (staging == null)
                            {
                                var sDesc = tex.Description;
                                sDesc.Usage = ResourceUsage.Staging;
                                sDesc.BindFlags = BindFlags.None;
                                sDesc.CPUAccessFlags = CpuAccessFlags.Read;
                                sDesc.MiscFlags = ResourceOptionFlags.None;
                                staging = device!.CreateTexture2D(sDesc);
                            }

                            context!.CopyResource(staging, tex);
                            var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

                            bool hasOverlay = false;
                            unsafe
                            {
                                byte* ptr = (byte*)mapped.DataPointer;
                                // Canary pixel location on primary monitor: check center of overlay
                                int testX = 100;
                                int testY = 100;
                                int idx = testY * (int)mapped.RowPitch + testX * 4;
                                byte b = ptr[idx]; byte g = ptr[idx + 1]; byte r = ptr[idx + 2];
                                if (r > 200 && g < 50 && b > 200) // Magenta overlay pixel
                                {
                                    hasOverlay = true;
                                }
                            }
                            context.Unmap(staging, 0);
                            dup.ReleaseFrame();

                            if (hasOverlay)
                            {
                                elapsedMs = (qpcCaptured - qpcStart) * 1000.0 / Stopwatch.Frequency;
                                frameFound = true;
                                break;
                            }
                        }
                    }
                    else if (hr.Code == Vortice.DXGI.ResultCode.AccessLost.Code)
                    {
                        dup.Dispose();
                        dup = output1.DuplicateOutput(device!);
                    }
                }

                if (!frameFound)
                {
                    elapsedMs = pollSw.Elapsed.TotalMilliseconds;
                }
                latencies.Add(elapsedMs);
            }
        }
        finally
        {
            staging?.Dispose();
            dup?.Dispose();
            context?.Dispose();
            device?.Dispose();
            output?.Dispose();
            adapter?.Dispose();
        }

        HideOverlays();

        latencies.Sort();
        double median = latencies.Count > 0 ? latencies[latencies.Count / 2] : 0.0;
        double min = latencies.Count > 0 ? latencies.Min() : 0.0;
        double max = latencies.Count > 0 ? latencies.Max() : 0.0;

        // Pass criterion: median under 150 ms
        bool pass = median < 150.0 && latencies.Count >= iterations;

        return new OverlayLatencyResult(
            Status: pass ? "PASS" : "FAIL",
            MedianLatencyMs: Math.Round(median, 2),
            MinLatencyMs: Math.Round(min, 2),
            MaxLatencyMs: Math.Round(max, 2),
            IterationLatenciesMs: latencies.Select(l => Math.Round(l, 2)).ToList(),
            Details: pass
                ? $"Hotkey to first presented overlay pixel: {median:F2} ms median (< 150 ms bar)"
                : $"Overlay latency exceeded 150 ms bar (median: {median:F2} ms)"
        );
    }

    private void PumpMessages(int iterations)
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

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == WM_PAINT)
        {
            IntPtr hdc = BeginPaint(hWnd, out var ps);
            FillRect(hdc, ref ps.rcPaint, _hBrushMagenta);
            EndPaint(hWnd, ref ps);
            CanaryPainted = true;
            return IntPtr.Zero;
        }
        else if (uMsg == WM_KEYDOWN)
        {
            _receivedKeys.Add((int)wParam);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        HideOverlays();

        foreach (var visual in _dcompVisuals)
        {
            try { Marshal.ReleaseComObject(visual); } catch { }
        }
        _dcompVisuals.Clear();

        foreach (var target in _dcompTargets)
        {
            try { Marshal.ReleaseComObject(target); } catch { }
        }
        _dcompTargets.Clear();

        if (_dcompDevice != null)
        {
            try { Marshal.ReleaseComObject(_dcompDevice); } catch { }
            _dcompDevice = null;
        }

        foreach (var hWnd in _overlayHwnds)
        {
            if (hWnd != IntPtr.Zero)
            {
                DestroyWindow(hWnd);
            }
        }
        _overlayHwnds.Clear();

        if (_hBrushMagenta != IntPtr.Zero)
        {
            DeleteObject(_hBrushMagenta);
        }
    }
}
