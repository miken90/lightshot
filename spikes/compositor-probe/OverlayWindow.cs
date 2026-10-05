using System.Runtime.InteropServices;

namespace CompositorProbe;

/// <summary>
/// Airspace probe: a top-level layered popup, owned by the WPF window, over the HwndHost. It lives on its own
/// thread with its own message loop (the plan's shell-thread model) and paints one opaque marker colour.
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    public const int MarkerW = 360, MarkerH = 96;
    // Opaque magenta, B8G8R8A8 premultiplied (alpha 255 so premultiplied == straight).
    public const byte R = 255, G = 0, B = 255;

    private readonly IntPtr _owner;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _hwnd;
    private uint _threadId;
    private static Native.WndProc? s_proc;
    private static int s_classSeq;

    public IntPtr Hwnd => _hwnd;
    public volatile bool Visible;
    // Screen rect of the marker; read by the DDA thread.
    private RECT _rect;
    private readonly object _rectLock = new();
    public RECT ScreenRect { get { lock (_rectLock) return _rect; } }

    public OverlayWindow(IntPtr owner)
    {
        _owner = owner;
        _thread = new Thread(Run) { IsBackground = true, Name = "overlay-shell", ApartmentState = ApartmentState.STA };
        _thread.Start();
        _ready.Wait();
    }

    private void Run()
    {
        s_proc ??= (h, m, w, l) => { if (m == Native.WM_USER_STOP) { Native.PostQuitMessage(0); return IntPtr.Zero; } return Native.DefWindowProc(h, m, w, l); };
        string cls = "CompositorProbeOverlay" + Interlocked.Increment(ref s_classSeq);
        var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_proc), hInstance = Native.GetModuleHandle(null), lpszClassName = cls };
        Native.RegisterClassEx(ref wc);
        _hwnd = Native.CreateWindowEx(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE, cls, "overlay", Native.WS_POPUP,
            0, 0, MarkerW, MarkerH, _owner, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        _threadId = Native.GetCurrentThreadId();
        Paint();
        _ready.Set();
        while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0)) { Native.TranslateMessage(ref msg); Native.DispatchMessage(ref msg); }
        Native.DestroyWindow(_hwnd);
    }

    private void Paint()
    {
        var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = MarkerW, biHeight = -MarkerH, biPlanes = 1, biBitCount = 32 };
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        IntPtr bmp = Native.CreateDIBSection(screen, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = Native.SelectObject(mem, bmp);
        var px = new int[MarkerW * MarkerH];
        // Solid marker; the outer 6 px ring is darker so an edge shift is visible in captures.
        for (int y = 0; y < MarkerH; y++)
            for (int x = 0; x < MarkerW; x++)
            {
                bool ring = x < 6 || y < 6 || x >= MarkerW - 6 || y >= MarkerH - 6;
                px[y * MarkerW + x] = ring ? unchecked((int)0xFF202020) : unchecked((int)(0xFF000000 | ((uint)R << 16) | ((uint)G << 8) | B));
            }
        Marshal.Copy(px, 0, bits, px.Length);
        var dst = new POINT { X = 0, Y = 0 }; var src = new POINT(); var size = new SIZE { cx = MarkerW, cy = MarkerH };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        Native.UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
        Native.SelectObject(mem, old); Native.DeleteObject(bmp); Native.DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, screen);
    }

    public void ShowAt(int screenX, int screenY)
    {
        lock (_rectLock) _rect = new RECT { Left = screenX, Top = screenY, Right = screenX + MarkerW, Bottom = screenY + MarkerH };
        Native.SetWindowPos(_hwnd, Native.HWND_TOP, screenX, screenY, MarkerW, MarkerH, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Visible = true;
    }

    public void Hide()
    {
        Visible = false;
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
    }

    public void Dispose()
    {
        Native.PostMessage(_hwnd, Native.WM_USER_STOP, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
