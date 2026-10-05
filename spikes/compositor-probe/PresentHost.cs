using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CompositorProbe;

public readonly record struct FrameStats(long PresentCount, uint PresentRefreshCount, uint SyncRefreshCount, long SyncQpc);

/// <summary>
/// The surface the compositor presents to: either a flip-model swapchain in an HwndHost or a D3DImage fed from shared
/// textures. PreviewPlayer and Runner only talk to this, so every criterion runs the same code on both hosts.
/// </summary>
public interface IPresentHost
{
    string Kind { get; }
    object RenderLock { get; }
    ID3D11Device Device { get; }
    ID3D11DeviceContext Context { get; }
    ProbeCompositor Compositor { get; }
    IDXGIAdapter1 Adapter { get; }
    string AdapterName { get; }
    string OutputName { get; }
    IntPtr Monitor { get; }
    /// <summary>Window whose client area shows the canvas with its top-left at the canvas origin.</summary>
    IntPtr ClientHwnd { get; }
    int BufferWidth { get; }
    int BufferHeight { get; }
    int ResizeCount { get; }
    int ResizeFailures { get; }
    string? LastResizeError { get; }
    /// <summary>Called under RenderLock right after the render target was recreated for a new size.</summary>
    Action? Resized { get; set; }
    /// <summary>Blocks until the host can take another frame (frame-latency waitable, or the next WPF render pass).</summary>
    bool WaitForFrame(int ms);
    /// <summary>A fresh reference to the texture to render into; the caller disposes it.</summary>
    ID3D11Texture2D AcquireTarget();
    long Present(int syncInterval);
    /// <summary>DXGI frame statistics; false when the host has none (D3DImage).</summary>
    bool TryGetStatistics(out FrameStats stats, out string error);
}

public static class AdapterPicker
{
    /// <summary>The adapter whose output drives the monitor (first adapter when none matches), plus that output's device name.</summary>
    public static IDXGIAdapter1 Pick(IntPtr monitor, out string outputName)
    {
        outputName = "";
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        IDXGIAdapter1? chosen = null;
        for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? ad).Success; a++)
        {
            for (uint o = 0; ad!.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
            {
                using (output) if (output!.Description.Monitor == monitor) { chosen ??= ad; outputName = output.Description.DeviceName; }
            }
            if (chosen != ad) ad.Dispose();
        }
        if (chosen == null) factory.EnumAdapters1(0, out chosen).CheckError();
        return chosen!;
    }
}
