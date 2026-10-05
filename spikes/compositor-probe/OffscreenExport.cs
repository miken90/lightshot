using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CompositorProbe;

public sealed record CompareResult(int Frame, double T, int MaxDelta, long OverTwo, long Pixels, bool Zoomed, string Worst);

/// <summary>
/// Export path: the same ProbeCompositor.Render on a WARP (software) D3D11 device into an offscreen BGRA texture.
/// The BGRA source it renders is the exact byte image the preview rendered from.
/// </summary>
public sealed class OffscreenExport : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ProbeCompositor _compositor;
    private readonly GpuReadback _readback;
    public string AdapterName { get; }

    public OffscreenExport()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 }, out ID3D11Device? dev, out ID3D11DeviceContext? ctx).CheckError();
        _device = dev!; _context = ctx!;
        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        using var ad = dxgi.GetAdapter();
        AdapterName = ad.Description.Description;
        _compositor = new ProbeCompositor(_device);
        _readback = new GpuReadback(_device, _context);
    }

    public byte[] Render(in RenderState state, double t, byte[] sourceBgra, int srcW, int srcH, int canvasW, int canvasH)
    {
        using var src = CreateSource(sourceBgra, srcW, srcH);
        using var target = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)canvasW, Height = (uint)canvasH, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
        });
        _compositor.Render(state, t, src, target);
        var bytes = _readback.Read(target);
        _compositor.ReleaseTargets();
        return bytes;
    }

    private unsafe ID3D11Texture2D CreateSource(byte[] bgra, int w, int h)
    {
        fixed (byte* p = bgra)
        {
            return _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource
            }, new SubresourceData((IntPtr)p, (uint)(w * 4)));
        }
    }

    public CompareResult Compare(CompareFrame preview)
    {
        var exported = Render(preview.State, preview.T, preview.SourcePixels, preview.SourceW, preview.SourceH, preview.Width, preview.Height);
        int max = 0; long over = 0; string worst = "";
        var a = preview.PreviewPixels;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Abs(a[i] - exported[i]);
            if (d > 2) over++;
            if (d > max) { max = d; int px = i / 4; worst = $"x={px % preview.Width} y={px / preview.Width} ch={"BGRA"[i % 4]} preview={a[i]} export={exported[i]}"; }
        }
        return new CompareResult(preview.Frame, preview.T, max, over, a.Length / 4, preview.State.Zoom && ProbeCompositor.ZoomAt(preview.T) > 1.001f, worst);
    }

    public void Dispose()
    {
        _readback.Dispose(); _compositor.Dispose();
        _context.ClearState(); _context.Flush(); _context.Dispose(); _device.Dispose();
    }
}
