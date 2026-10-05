using System.Runtime.InteropServices;
using System.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CompositorProbe;

/// <summary>
/// HwndHost for the compositor: a child HWND owning a flip-model swapchain on the adapter that drives the window's
/// monitor. WM_SIZE resizes the swapchain synchronously on the UI thread (under RenderLock) and asks the player to
/// repaint before the message returns, so DWM never shows a stretched or empty buffer for longer than the resize.
/// </summary>
public sealed class D3dHost : HwndHost, IPresentHost
{
    private const string ClassName = "CompositorProbeHost";
    private static Native.WndProc? s_proc;
    private static bool s_registered;
    private IntPtr _hwnd;

    public object RenderLock { get; } = new();
    public ID3D11Device Device { get; private set; } = null!;
    public ID3D11DeviceContext Context { get; private set; } = null!;
    public IDXGISwapChain2 SwapChain { get; private set; } = null!;
    public ProbeCompositor Compositor { get; private set; } = null!;
    public IntPtr FrameLatencyHandle { get; private set; }
    public IDXGIAdapter1 Adapter { get; private set; } = null!;
    public string AdapterName { get; private set; } = "";
    public string OutputName { get; private set; } = "";
    public IntPtr Monitor { get; private set; }
    public int BufferWidth { get; private set; }
    public int BufferHeight { get; private set; }
    public int ResizeCount { get; private set; }
    public int ResizeFailures { get; private set; }
    public string? LastResizeError { get; private set; }
    public IntPtr Hwnd => _hwnd;
    public string Kind => "HwndHost";
    public IntPtr ClientHwnd => _hwnd;
    public long LastPresentCount => SwapChain.LastPresentCount;

    public bool WaitForFrame(int ms) => Native.WaitForSingleObject(FrameLatencyHandle, (uint)ms) == 0;
    public ID3D11Texture2D AcquireTarget() => SwapChain.GetBuffer<ID3D11Texture2D>(0);

    public long Present(int syncInterval)
    {
        var hr = SwapChain.Present((uint)syncInterval, PresentFlags.None);
        if (hr.Failure) throw new InvalidOperationException("Present failed " + hr);
        return SwapChain.LastPresentCount;
    }

    public bool TryGetStatistics(out FrameStats stats, out string error)
    {
        var hr = SwapChain.GetFrameStatistics(out FrameStatistics fs);
        stats = new FrameStats(fs.PresentCount, fs.PresentRefreshCount, fs.SyncRefreshCount, fs.SyncQPCTime);
        error = hr.Failure ? "0x" + ((uint)hr.Code).ToString("X8") : "";
        return hr.Success;
    }

    /// <summary>Called under RenderLock right after the buffers were resized.</summary>
    public Action? Resized { get; set; }

    private const SwapChainFlags Flags = SwapChainFlags.FrameLatencyWaitableObject;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        if (!s_registered)
        {
            s_proc = HostProc;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_proc),
                hInstance = Native.GetModuleHandle(null), lpszClassName = ClassName
            };
            if (Native.RegisterClassEx(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx failed " + Marshal.GetLastWin32Error());
            s_registered = true;
        }
        _hwnd = Native.CreateWindowEx(0, ClassName, null, Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN | Native.WS_CLIPSIBLINGS,
            0, 0, 640, 360, hwndParent.Handle, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed " + Marshal.GetLastWin32Error());
        CreateDeviceAndSwapChain(hwndParent.Handle);
        return new HandleRef(this, _hwnd);
    }

    private void CreateDeviceAndSwapChain(IntPtr parent)
    {
        Monitor = Native.MonitorFromWindow(parent, Native.MONITOR_DEFAULTTONEAREST);
        Adapter = AdapterPicker.Pick(Monitor, out string output);
        OutputName = output;
        AdapterName = Adapter.Description1.Description;

        D3D11.D3D11CreateDevice(Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out ID3D11Device? dev, out ID3D11DeviceContext? ctx).CheckError();
        Device = dev!; Context = ctx!;
        using (var mt = Device.QueryInterfaceOrNull<ID3D11Multithread>()) mt?.SetMultithreadProtected(true);

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        Native.GetClientRect(_hwnd, out var rc);
        var desc = new SwapChainDescription1
        {
            Width = (uint)rc.Width, Height = (uint)rc.Height, Format = Format.B8G8R8A8_UNorm,
            Stereo = false, SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput, BufferCount = 3, Scaling = Scaling.None,
            SwapEffect = SwapEffect.FlipDiscard, AlphaMode = Vortice.DXGI.AlphaMode.Ignore, Flags = Flags
        };
        using var sc1 = factory.CreateSwapChainForHwnd(Device, _hwnd, desc, null, null);
        SwapChain = sc1.QueryInterface<IDXGISwapChain2>();
        SwapChain.MaximumFrameLatency = 1;
        FrameLatencyHandle = SwapChain.FrameLatencyWaitableObject;
        factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAll);
        BufferWidth = rc.Width; BufferHeight = rc.Height;
        Compositor = new ProbeCompositor(Device);
    }

    private IntPtr HostProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Native.WM_ERASEBKGND) return (IntPtr)1;
        if (msg == Native.WM_SIZE && SwapChain != null) HandleSize(Native.LoWord(lParam) & 0xFFFF, (int)(((long)lParam >> 16) & 0xFFFF));
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleSize(int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        lock (RenderLock)
        {
            if (w == BufferWidth && h == BufferHeight) return;
            Compositor.ReleaseTargets();
            var hr = SwapChain.ResizeBuffers(0, (uint)w, (uint)h, Format.Unknown, Flags);
            if (hr.Failure) { ResizeFailures++; LastResizeError = hr.ToString(); return; }
            BufferWidth = w; BufferHeight = h; ResizeCount++;
            Resized?.Invoke();
        }
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        lock (RenderLock)
        {
            Compositor?.Dispose(); SwapChain?.Dispose(); Context?.ClearState(); Context?.Flush();
            Context?.Dispose(); Device?.Dispose(); Adapter?.Dispose();
        }
        Native.DestroyWindow(hwnd.Handle);
    }
}
