using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D9Format = Vortice.Direct3D9.Format;
using D3D9Usage = Vortice.Direct3D9.Usage;
using D3D9PresentParameters = Vortice.Direct3D9.PresentParameters;
using D3D9SwapEffect = Vortice.Direct3D9.SwapEffect;
using D3D9 = Vortice.Direct3D9.D3D9;
using IDirect3D9Ex = Vortice.Direct3D9.IDirect3D9Ex;
using IDirect3DDevice9Ex = Vortice.Direct3D9.IDirect3DDevice9Ex;
using IDirect3DTexture9 = Vortice.Direct3D9.IDirect3DTexture9;
using IDirect3DSurface9 = Vortice.Direct3D9.IDirect3DSurface9;
using Rect = System.Windows.Rect;

namespace CompositorProbe;

/// <summary>
/// Fallback host: no child HWND. The compositor renders into one of two shared D3D11 textures; the matching D3D9Ex
/// surface is handed to a WPF D3DImage on the UI thread. WPF composes it like any other element, so there is no airspace.
/// </summary>
public sealed class D3dImageHost : FrameworkElement, IPresentHost
{
    private readonly D3DImage _image = new(96, 96);
    private readonly AutoResetEvent _frameTick = new(false);
    private IDirect3D9Ex _d3d9 = null!;
    private IDirect3DDevice9Ex _dev9 = null!;
    private readonly ID3D11Texture2D?[] _tex = new ID3D11Texture2D?[2];
    private readonly IDirect3DTexture9?[] _tex9 = new IDirect3DTexture9?[2];
    private readonly IDirect3DSurface9?[] _surf9 = new IDirect3DSurface9?[2];
    private int _back;                // buffer the compositor renders into next
    private int _pending = -1;        // buffer waiting to be committed to the D3DImage
    private int _commitQueued;
    private long _presentCount;
    private double _dpiScale = 1.0;
    private ID3D11Query? _query;
    private volatile bool _stopPacer;

    public string Kind => "D3DImage";
    public object RenderLock { get; } = new();
    public ID3D11Device Device { get; private set; } = null!;
    public ID3D11DeviceContext Context { get; private set; } = null!;
    public ProbeCompositor Compositor { get; private set; } = null!;
    public IDXGIAdapter1 Adapter { get; private set; } = null!;
    public string AdapterName { get; private set; } = "";
    public string OutputName { get; private set; } = "";
    public IntPtr Monitor { get; private set; }
    public int BufferWidth { get; private set; }
    public int BufferHeight { get; private set; }
    public int ResizeCount { get; private set; }
    public int ResizeFailures { get; private set; }
    public string? LastResizeError { get; private set; }
    public Action? Resized { get; set; }
    public bool FrontBufferAvailable => _image.IsFrontBufferAvailable;
    public int FrontBufferLostEvents;
    /// <summary>Presents whose surface was overwritten by a newer present before the UI thread committed it to the D3DImage.</summary>
    public long ReplacedBeforeCommit;
    public long Commits;
    private volatile IntPtr _clientHwnd;   // resolved on the UI thread; readable from any thread
    public IntPtr ClientHwnd => _clientHwnd;

    public D3dImageHost()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SnapsToDevicePixels = true;
        // The probe window is created at (0,0) on the primary monitor; pick the adapter that drives it.
        Monitor = Native.MonitorFromPoint(new POINT { X = 1, Y = 1 }, Native.MONITOR_DEFAULTTONEAREST);
        Adapter = AdapterPicker.Pick(Monitor, out string output);
        OutputName = output;
        AdapterName = Adapter.Description1.Description;
        D3D11.D3D11CreateDevice(Adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out ID3D11Device? dev, out ID3D11DeviceContext? ctx).CheckError();
        Device = dev!; Context = ctx!;
        using (var mt = Device.QueryInterfaceOrNull<ID3D11Multithread>()) mt?.SetMultithreadProtected(true);
        _query = Device.CreateQuery(new QueryDescription(Vortice.Direct3D11.QueryType.Event));
        Compositor = new ProbeCompositor(Device);
        CreateD3D9();
        _image.IsFrontBufferAvailableChanged += (_, _) => { if (!_image.IsFrontBufferAvailable) Interlocked.Increment(ref FrontBufferLostEvents); };
        // Paced by DWM: one tick per desktop composition, which is when WPF's composed D3DImage can reach the screen.
        // (CompositionTarget.Rendering re-fires after every AddDirtyRect, so it is not a display clock.)
        new Thread(() => { while (!_stopPacer) { Native.DwmFlush(); _frameTick.Set(); } }) { IsBackground = true, Name = "dwm-pacer" }.Start();
    }

    private void CreateD3D9()
    {
        _d3d9 = D3D9.Direct3DCreate9Ex();
        long luid = (long)Adapter.Description1.Luid;
        uint ordinal = uint.MaxValue;
        uint count = (uint)_d3d9.AdapterCount;
        for (uint i = 0; i < count; i++)
        {
            if ((long)_d3d9.GetAdapterLuid(i) == luid) { ordinal = i; break; }
        }
        if (ordinal == uint.MaxValue) throw new InvalidOperationException("no D3D9 adapter with the D3D11 adapter's LUID");
        var pp = new D3D9PresentParameters
        {
            Windowed = true, SwapEffect = D3D9SwapEffect.Discard, BackBufferFormat = D3D9Format.Unknown, BackBufferWidth = 1, BackBufferHeight = 1,
            DeviceWindowHandle = Native.GetDesktopWindow(), PresentationInterval = Vortice.Direct3D9.PresentInterval.Default
        };
        _dev9 = _d3d9.CreateDeviceEx(ordinal, Vortice.Direct3D9.DeviceType.Hardware, Native.GetDesktopWindow(),
            Vortice.Direct3D9.CreateFlags.HardwareVertexProcessing | Vortice.Direct3D9.CreateFlags.Multithreaded | Vortice.Direct3D9.CreateFlags.FpuPreserve, pp);
    }

    // ---------------------------------------------------------------- buffers
    private void CreateBuffers(int w, int h)
    {
        for (int i = 0; i < 2; i++)
        {
            var tex = Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource, MiscFlags = ResourceOptionFlags.Shared
            });
            IntPtr handle;
            using (var res = tex.QueryInterface<IDXGIResource>()) handle = res.SharedHandle;
            var t9 = _dev9.CreateTexture((uint)w, (uint)h, 1, D3D9Usage.RenderTarget, D3D9Format.A8R8G8B8, Vortice.Direct3D9.Pool.Default, ref handle);
            _tex[i] = tex; _tex9[i] = t9; _surf9[i] = t9.GetSurfaceLevel(0);
        }
        BufferWidth = w; BufferHeight = h; _back = 0; _pending = -1;
    }

    private void DestroyBuffers()
    {
        for (int i = 0; i < 2; i++)
        {
            _surf9[i]?.Dispose(); _surf9[i] = null;
            _tex9[i]?.Dispose(); _tex9[i] = null;
            _tex[i]?.Dispose(); _tex[i] = null;
        }
    }

    private void Resize(int w, int h)
    {
        if (w <= 0 || h <= 0 || (w == BufferWidth && h == BufferHeight)) return;
        lock (RenderLock)
        {
            try
            {
                Compositor.ReleaseTargets();
                _image.Lock(); _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); _image.Unlock();
                DestroyBuffers();
                Context.Flush();
                CreateBuffers(w, h);
                ResizeCount++;
                Resized?.Invoke();
            }
            catch (Exception ex) { ResizeFailures++; LastResizeError = ex.GetType().Name + ": " + ex.Message; }
        }
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_clientHwnd == IntPtr.Zero && PresentationSource.FromVisual(this) is HwndSource src) _clientHwnd = src.Handle;
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Resize((int)Math.Round(info.NewSize.Width * _dpiScale), (int)Math.Round(info.NewSize.Height * _dpiScale));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpiScale = newDpi.DpiScaleX;
        Resize((int)Math.Round(ActualWidth * _dpiScale), (int)Math.Round(ActualHeight * _dpiScale));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (BufferWidth > 0) dc.DrawImage(_image, new Rect(0, 0, BufferWidth * 96.0 / (_dpiScale * 96.0) , BufferHeight * 96.0 / (_dpiScale * 96.0)));
    }

    // ---------------------------------------------------------------- IPresentHost
    public bool WaitForFrame(int ms) => _frameTick.WaitOne(ms);

    public ID3D11Texture2D AcquireTarget() => _tex[_back]!.QueryInterface<ID3D11Texture2D>();

    public long Present(int syncInterval)
    {
        // The D3D9 side must not read the texture before the D3D11 work that fills it has finished.
        Context.End(_query!);
        Context.Flush();
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency;
        IntPtr data = Marshal.AllocHGlobal(4);
        try
        {
            while (System.Diagnostics.Stopwatch.GetTimestamp() < deadline)
            {
                var hr = Context.GetData(_query!, data, 4, AsyncGetDataFlags.None);
                if (hr.Code == 0) break;
                Thread.SpinWait(50);
            }
        }
        finally { Marshal.FreeHGlobal(data); }
        int idx = _back;
        _back ^= 1;
        long pc = Interlocked.Increment(ref _presentCount);
        if (Interlocked.Exchange(ref _pending, idx) >= 0) Interlocked.Increment(ref ReplacedBeforeCommit);
        if (Interlocked.Exchange(ref _commitQueued, 1) == 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => { Interlocked.Exchange(ref _commitQueued, 0); lock (RenderLock) { int i = Interlocked.Exchange(ref _pending, -1); if (i >= 0) { Commit(i); Commits++; } } });
        return pc;
    }

    // Under RenderLock on the UI thread: hand the latest rendered surface to WPF.
    private void Commit(int idx)
    {
        if (idx < 0 || _surf9[idx] == null) return;
        _image.Lock();
        try
        {
            _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surf9[idx]!.NativePointer);
            _image.AddDirtyRect(new Int32Rect(0, 0, BufferWidth, BufferHeight));
        }
        finally { _image.Unlock(); }
    }

    public bool TryGetStatistics(out FrameStats stats, out string error)
    {
        stats = default; error = "n/a";
        return false;
    }

    public void ReleaseAll()
    {
        _stopPacer = true;
        lock (RenderLock)
        {
            Compositor?.Dispose();
            try { _image.Lock(); _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); _image.Unlock(); } catch { }
            DestroyBuffers();
            _query?.Dispose(); _dev9?.Dispose(); _d3d9?.Dispose();
            Context?.ClearState(); Context?.Flush(); Context?.Dispose(); Device?.Dispose(); Adapter?.Dispose();
        }
    }
}
