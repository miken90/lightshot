using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CompositorProbe;

/// <summary>
/// Independent observer: DXGI Desktop Duplication of the output the window is on. For every desktop frame DWM
/// delivers it reads (a) the overlay marker region and (b) the top/bottom present-sequence strips of the client
/// area, so overlay presence and tearing are judged from what the compositor really displayed.
/// </summary>
public sealed class DdaMonitor : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _dup;
    private readonly RECT _desktop;
    private readonly Thread _thread;
    private volatile bool _stop;
    private ID3D11Texture2D? _stageA, _stageB, _stageR;
    

    public Func<(int w, int h)>? BufferSize;   // swapchain size, to tell a stale-size frame from a torn one
    public int StripSizeTransient;
    public string? LastError;
    public string? MissDumpPath;
    public Func<string>? MissContext;
    public string? MissContextText;
    private bool _missDumped;
    public Func<RECT>? OverlayRect;           // null or empty width -> overlay check disabled
    public Func<bool>? OverlayVisible;
    public IntPtr StripWindow;                 // non-zero -> strip tear check enabled on that window's client area
    public volatile bool Enabled = true;

    // Counters (written by the DDA thread, read after Stop).
    public long Frames, UpdatedFrames, Timeouts, Errors;
    public long OverlayChecked, OverlayMissing;
    public List<double> OverlayMissingAtSec { get; } = new();
    public long StripChecked, StripTorn, StripUnreadable, StripUnstable;
    public List<string> StripSamples { get; } = new();
    private readonly long _t0 = Stopwatch.GetTimestamp();

    public DdaMonitor(IDXGIAdapter1 adapter, IntPtr monitor)
    {
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out ID3D11Device? dev, out ID3D11DeviceContext? ctx).CheckError();
        _device = dev!; _context = ctx!;
        IDXGIOutput1? found = null;
        for (uint o = 0; adapter.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
        {
            if (output!.Description.Monitor == monitor) { found = output.QueryInterface<IDXGIOutput1>(); output.Dispose(); break; }
            output.Dispose();
        }
        if (found == null) throw new InvalidOperationException("no DXGI output for the window's monitor on this adapter");
        var d = found.Description.DesktopCoordinates;
        _desktop = new RECT { Left = d.Left, Top = d.Top, Right = d.Right, Bottom = d.Bottom };
        _dup = found.DuplicateOutput(_device);
        found.Dispose();
        _thread = new Thread(Run) { IsBackground = true, Name = "dda" };
    }

    public void Start() => _thread.Start();

    public void Stop()
    {
        _stop = true;
        _thread.Join(3000);
    }

    private ID3D11Texture2D Stage(int w, int h)
    {
        var t = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read
        });
        return t;
    }

    private void Run()
    {
        _stageA = Stage(OverlayWindow.MarkerW, OverlayWindow.MarkerH);
        _stageB = Stage(FrameStamp.StripCells * FrameStamp.StripCellW, FrameStamp.StripH);
        var stageC = Stage(FrameStamp.StripCells * FrameStamp.StripCellW, FrameStamp.StripH);
        _stageR = Stage(FrameStamp.StripRulerW, (int)_desktop.Height);
        while (!_stop)
        {
            var hr = _dup.AcquireNextFrame(100, out OutduplFrameInfo info, out IDXGIResource? res);
            if (hr.Failure)
            {
                if ((uint)hr.Code == 0x887A0027) { Timeouts++; continue; } // DXGI_ERROR_WAIT_TIMEOUT
                Errors++; Thread.Sleep(5); continue;
            }
            try
            {
                Frames++;
                if (info.LastPresentTime == 0 || !Enabled) continue; // mouse-only update or paused
                UpdatedFrames++;
                using var tex = res!.QueryInterface<ID3D11Texture2D>();
                double atSec = (Stopwatch.GetTimestamp() - _t0) / (double)Stopwatch.Frequency;
                CheckOverlay(tex, atSec);
                CheckStrips(tex, stageC);
            }
            catch (Exception ex) { Errors++; LastError = ex.GetType().Name + ": " + ex.Message; }
            finally { res?.Dispose(); _dup.ReleaseFrame(); }
        }
        _stageA.Dispose(); _stageB.Dispose(); stageC.Dispose(); _stageR.Dispose();
    }

    private void CheckOverlay(ID3D11Texture2D desktop, double atSec)
    {
        if (OverlayRect == null || OverlayVisible == null || !OverlayVisible()) return;
        var r = OverlayRect();
        if (r.Width != OverlayWindow.MarkerW) return;
        int x = r.Left - _desktop.Left, y = r.Top - _desktop.Top;
        var dd = desktop.Description;
        if (x < 0 || y < 0 || x + r.Width > dd.Width || y + r.Height > dd.Height) return; // not on this output
        _context.CopySubresourceRegion(_stageA!, 0, 0, 0, 0, desktop, 0, new Vortice.Mathematics.Box(x, y, 0, x + r.Width, y + r.Height, 1));
        var map = _context.Map(_stageA!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        int bad = 0;
        try
        {
            // 5 x 3 grid of sample points strictly inside the marker (clear of the dark 6 px ring).
            for (int gy = 0; gy < 3; gy++)
                for (int gx = 0; gx < 5; gx++)
                {
                    int px = 30 + gx * (OverlayWindow.MarkerW - 60) / 4, py = 20 + gy * (OverlayWindow.MarkerH - 40) / 2;
                    int off = py * (int)map.RowPitch + px * 4;
                    byte b = Marshal.ReadByte(map.DataPointer, off), g = Marshal.ReadByte(map.DataPointer, off + 1), rr = Marshal.ReadByte(map.DataPointer, off + 2);
                    if (Math.Abs(rr - OverlayWindow.R) > 8 || Math.Abs(g - OverlayWindow.G) > 8 || Math.Abs(b - OverlayWindow.B) > 8) bad++;
                }
        }
        finally { _context.Unmap(_stageA!, 0); }
        if (bad > 0 && MissDumpPath != null && !_missDumped)
        {
            // Keep the first frame that failed so the cause (another window, a hidden overlay, a blank region) can be looked at.
            _missDumped = true;
            try { MissContextText = MissContext?.Invoke(); } catch (Exception e) { MissContextText = e.Message; }
            var m2 = _context.Map(_stageA!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var raw = new byte[OverlayWindow.MarkerW * OverlayWindow.MarkerH * 4];
                for (int yy = 0; yy < OverlayWindow.MarkerH; yy++) Marshal.Copy(m2.DataPointer + yy * (int)m2.RowPitch, raw, yy * OverlayWindow.MarkerW * 4, OverlayWindow.MarkerW * 4);
                File.WriteAllBytes(MissDumpPath, raw);
            }
            finally { _context.Unmap(_stageA!, 0); }
        }
        OverlayChecked++;
        if (bad > 0) { OverlayMissing++; if (OverlayMissingAtSec.Count < 200) OverlayMissingAtSec.Add(Math.Round(atSec, 3)); }
    }

    private int ReadStrip(ID3D11Texture2D desktop, ID3D11Texture2D stage, int x, int y, out bool readable)
    {
        int w = FrameStamp.StripCells * FrameStamp.StripCellW;
        _context.CopySubresourceRegion(stage, 0, 0, 0, 0, desktop, 0, new Vortice.Mathematics.Box(x, y, 0, x + w, y + FrameStamp.StripH, 1));
        var map = _context.Map(stage, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            readable = true;
            int v = 0;
            for (int b = 0; b < FrameStamp.StripCells; b++)
            {
                int off = (FrameStamp.StripH / 2) * (int)map.RowPitch + (b * FrameStamp.StripCellW + FrameStamp.StripCellW / 2) * 4;
                byte g = Marshal.ReadByte(map.DataPointer, off + 1);
                // A cell is only readable if clearly black or white; anything between means resampled or foreign pixels.
                bool white = g > 200;
                if (!white && g > 55) readable = false;
                if (b < FrameStamp.StripBits) { if (white) v |= 1 << b; }
                else if (white != (b == FrameStamp.StripBits)) readable = false; // guard cells: white then black
            }
            return v;
        }
        finally { _context.Unmap(stage, 0); }
    }

    /// <summary>Rows of cyan ruler directly below the top strip, i.e. the presented client height minus both strips.</summary>
    private int RulerRows(ID3D11Texture2D desktop, int x, int yTop, int maxRows)
    {
        int rows = Math.Min(maxRows, (int)_desktop.Height - yTop);
        if (rows <= FrameStamp.StripH + 2) return 0;
        _context.CopySubresourceRegion(_stageR!, 0, 0, 0, 0, desktop, 0, new Vortice.Mathematics.Box(x, yTop, 0, x + FrameStamp.StripRulerW, yTop + rows, 1));
        var map = _context.Map(_stageR!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int n = 0;
            for (int y = FrameStamp.StripH; y < rows; y++)
            {
                int off = y * (int)map.RowPitch + 2 * 4;
                byte b = Marshal.ReadByte(map.DataPointer, off), g = Marshal.ReadByte(map.DataPointer, off + 1), r = Marshal.ReadByte(map.DataPointer, off + 2);
                if (b > 200 && g > 200 && r < 60) n++; else break;
            }
            return n;
        }
        finally { _context.Unmap(_stageR!, 0); }
    }

    private void CheckStrips(ID3D11Texture2D desktop, ID3D11Texture2D stageC)
    {
        if (StripWindow == IntPtr.Zero) return;
        var before = Native.ClientScreenRect(StripWindow);
        int w = FrameStamp.StripCells * FrameStamp.StripCellW;
        var dd = desktop.Description;
        int x = before.Left - _desktop.Left, yTop = before.Top - _desktop.Top;
        if (x < 0 || yTop < 0 || x + w > dd.Width || yTop + FrameStamp.StripH * 3 > dd.Height || before.Width < w) { StripUnstable++; return; }
        // The bottom row is located from the ruler in this same captured frame, never from the current window rectangle.
        int ruler = RulerRows(desktop, x, yTop, 4000);
        int top = ReadStrip(desktop, _stageB!, x, yTop, out bool okTop);
        int yBot = yTop + FrameStamp.StripH + ruler;
        int bot = 0; bool okBot = false;
        if (ruler > 0 && yBot + FrameStamp.StripH <= dd.Height) bot = ReadStrip(desktop, stageC, x, yBot, out okBot);
        var after = Native.ClientScreenRect(StripWindow);
        if (after.Left != before.Left || after.Top != before.Top) { StripUnstable++; return; }
        StripChecked++;
        if (ruler + 2 * FrameStamp.StripH != before.Height) StripSizeTransient++; // presented size differs from the window size now
        if (!okTop || !okBot) { StripUnreadable++; if (StripSamples.Count < 40) StripSamples.Add($"unreadable top={okTop} bottom={okBot} ruler={ruler} client={before.Width}x{before.Height}"); return; }
        if (top != bot) { StripTorn++; if (StripSamples.Count < 40) StripSamples.Add($"torn top={top} bottom={bot} ruler={ruler} client={before.Width}x{before.Height}"); }
    }

    public void Dispose()
    {
        if (!_stop) Stop();
        _dup.Dispose(); _context.ClearState(); _context.Dispose(); _device.Dispose();
    }
}
