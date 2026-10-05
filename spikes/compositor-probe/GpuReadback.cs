using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CompositorProbe;

/// <summary>Staging copies of BGRA textures: GPU copy queued first, mapped later (so Present is not delayed by the map).</summary>
public sealed class GpuReadback : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private ID3D11Texture2D? _staging;
    private int _w, _h;

    public GpuReadback(ID3D11Device device, ID3D11DeviceContext context) { _device = device; _context = context; }

    /// <summary>Queue a copy of the texture into the staging texture.</summary>
    public void Enqueue(ID3D11Texture2D src)
    {
        var d = src.Description;
        if (_staging == null || _w != (int)d.Width || _h != (int)d.Height)
        {
            _staging?.Dispose();
            d.Usage = ResourceUsage.Staging; d.BindFlags = BindFlags.None; d.CPUAccessFlags = CpuAccessFlags.Read; d.MiscFlags = ResourceOptionFlags.None;
            _staging = _device.CreateTexture2D(d);
            _w = (int)d.Width; _h = (int)d.Height;
        }
        _context.CopyResource(_staging, src);
    }

    /// <summary>Map the staging texture and return tightly packed BGRA bytes (blocks until the queued copy completes).</summary>
    public byte[] Fetch()
    {
        var map = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var bytes = new byte[_w * _h * 4];
            for (int y = 0; y < _h; y++)
                Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, bytes, y * _w * 4, _w * 4);
            return bytes;
        }
        finally { _context.Unmap(_staging!, 0); }
    }

    public byte[] Read(ID3D11Texture2D src) { Enqueue(src); return Fetch(); }

    public void Dispose() => _staging?.Dispose();
}
