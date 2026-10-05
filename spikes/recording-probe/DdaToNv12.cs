using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecordingProbe;

public sealed class DdaToNv12 : IDisposable
{
    private readonly IDXGIFactory1 _factory;
    private readonly IDXGIAdapter1 _adapter;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly IDXGIOutput1 _output1;
    private readonly IDXGIOutputDuplication _dda;

    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _videoProcessor;
    // Ring of NV12 targets: the encoder MFT may still read a texture while the next frame is blitted.
    private const int RingSize = 8;
    private readonly ID3D11Texture2D[] _nv12Ring = new ID3D11Texture2D[RingSize];
    private readonly ID3D11VideoProcessorOutputView[] _outputViews = new ID3D11VideoProcessorOutputView[RingSize];
    private int _ringIndex;
    private readonly ID3D11Query _blitDone;

    private ID3D11Texture2D? _lastDesktopTexture;
    private bool _disposed;

    public ID3D11Device Device => _device;
    public ID3D11Texture2D Nv12Texture => _nv12Ring[_ringIndex];
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public string AdapterName { get; }
    public string OutputName { get; }
    /// <summary>Desktop updates DWM coalesced into a single acquire (updates the capture loop never saw).</summary>
    public int AccumulatedExtraFrames { get; private set; }
    public int AcquiredFrames { get; private set; }
    public int PresentedFrames { get; private set; }

    // Control tap: the counter blocks decoded from every acquired desktop frame before any scaling or encoding.
    // Counters missing here were never delivered by the display path; counters present here but absent from the
    // file were lost by the pipeline.
    public List<int> AcquiredCounters { get; } = new();
    /// <summary>QPC of each acquire and whether the flash panel was white in it.</summary>
    public List<(long qpc, bool white)> AcquiredFlash { get; } = new();
    public double CounterProbeDpi { get; set; }
    private ID3D11Texture2D? _staging;

    private void TapCounter(ID3D11Texture2D desktop, long qpc)
    {
        if (CounterProbeDpi <= 0) return;
        if (_staging == null)
        {
            var d = desktop.Description;
            d.Width = 640; d.Height = 256; d.MipLevels = 1; d.ArraySize = 1;
            d.Usage = ResourceUsage.Staging; d.BindFlags = BindFlags.None;
            d.CPUAccessFlags = CpuAccessFlags.Read; d.MiscFlags = ResourceOptionFlags.None;
            _staging = _device.CreateTexture2D(d);
        }
        _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, desktop, 0, new Vortice.Mathematics.Box(0, 0, 0, 640, 256, 1));
        var map = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int gray = 0;
            int cy = (int)((CounterWindow.BitY0 + CounterWindow.BitSize / 2) * CounterProbeDpi);
            for (int i = 0; i < CounterWindow.Bits; i++)
            {
                int cx = (int)((CounterWindow.BitX0 + i * CounterWindow.BitPitch + CounterWindow.BitSize / 2) * CounterProbeDpi);
                byte g = Marshal.ReadByte(map.DataPointer, cy * (int)map.RowPitch + cx * 4 + 1);
                if (g > 126) gray |= 1 << i;
            }
            AcquiredCounters.Add(CounterWindow.FromGray(gray));
            int fx = (int)((CounterWindow.FlashX + CounterWindow.FlashW / 2) * CounterProbeDpi);
            int fy = (int)((CounterWindow.FlashY + CounterWindow.FlashH / 2) * CounterProbeDpi);
            AcquiredFlash.Add((qpc, Marshal.ReadByte(map.DataPointer, fy * (int)map.RowPitch + fx * 4 + 1) > 126));
        }
        finally { _context.Unmap(_staging, 0); }
    }

    public DdaToNv12(int outputWidth = 2560, int outputHeight = 1440)
    {
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;

        _factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        _factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).CheckError();
        _adapter = adapter!;
        AdapterName = _adapter.Description.Description;

        D3D11.D3D11CreateDevice(
            _adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device? device,
            out ID3D11DeviceContext? context).CheckError();

        _device = device!;
        _context = context!;

        using var multithread = _device.QueryInterfaceOrNull<ID3D11Multithread>();
        multithread?.SetMultithreadProtected(true);

        _videoDevice = _device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = _context.QueryInterface<ID3D11VideoContext>();

        _adapter.EnumOutputs(0, out IDXGIOutput? output).CheckError();
        _output1 = output!.QueryInterface<IDXGIOutput1>();

        var desc = _output1.Description;
        OutputName = desc.DeviceName;
        ScreenWidth = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
        ScreenHeight = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

        _dda = _output1.DuplicateOutput(_device);
        _blitDone = _device.CreateQuery(QueryType.Event);

        // Target NV12 texture
        var nv12Desc = new Texture2DDescription
        {
            Width = (uint)OutputWidth,
            Height = (uint)OutputHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        // GPU video processor configuration (BGRA -> NV12 + scale)
        var contentDesc = new VideoProcessorContentDescription
        {
            InputWidth = (uint)ScreenWidth,
            InputHeight = (uint)ScreenHeight,
            OutputWidth = (uint)OutputWidth,
            OutputHeight = (uint)OutputHeight,
            InputFrameRate = new Rational(60, 1),
            OutputFrameRate = new Rational(60, 1),
            Usage = VideoUsage.PlaybackNormal
        };

        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(contentDesc);
        _videoProcessor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

        var outViewDesc = new VideoProcessorOutputViewDescription
        {
            ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 }
        };
        for (int i = 0; i < RingSize; i++)
        {
            _nv12Ring[i] = _device.CreateTexture2D(nv12Desc);
            _outputViews[i] = _videoDevice.CreateVideoProcessorOutputView(_nv12Ring[i], _enumerator, outViewDesc);
        }
    }

    /// <summary>
    /// Waits for the next desktop frame, converts it into the next ring texture and stamps it with the QPC
    /// taken after the acquire returned. On timeout the cached last frame is converted again (duplicate).
    /// </summary>
    public bool AcquireAndProcessFrame(int timeoutMs, out long qpcTime, out bool duplicate)
    {
        duplicate = false;
        var hr = _dda.AcquireNextFrame((uint)timeoutMs, out var frameInfo, out var desktopResource);
        qpcTime = Stopwatch.GetTimestamp();
        if (hr.Success && desktopResource != null)
        {
            AcquiredFrames++;
            if (frameInfo.AccumulatedFrames > 1) AccumulatedExtraFrames += (int)frameInfo.AccumulatedFrames - 1;
            if (frameInfo.LastPresentTime != 0) PresentedFrames++;
            using var rawTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
            desktopResource.Dispose();

            // Cache a copy for timeouts on a static screen
            if (_lastDesktopTexture == null)
            {
                var copyDesc = rawTexture.Description;
                copyDesc.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget;
                copyDesc.Usage = ResourceUsage.Default;
                copyDesc.MiscFlags = ResourceOptionFlags.None;
                _lastDesktopTexture = _device.CreateTexture2D(copyDesc);
            }
            _context.CopyResource(_lastDesktopTexture, rawTexture);

            // The acquired texture is only valid until ReleaseFrame, so blit before releasing.
            TapCounter(rawTexture, qpcTime);
            ConvertTextureToNv12(rawTexture);
            _dda.ReleaseFrame();
            return true;
        }

        if (_lastDesktopTexture != null)
        {
            duplicate = true;
            ConvertTextureToNv12(_lastDesktopTexture);
            return true;
        }

        return false;
    }

    private void ConvertTextureToNv12(ID3D11Texture2D sourceTexture)
    {
        var inViewDesc = new VideoProcessorInputViewDescription
        {
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0 }
        };

        using var inputView = _videoDevice.CreateVideoProcessorInputView(sourceTexture, _enumerator, inViewDesc);
        var stream = new VideoProcessorStream
        {
            Enable = true,
            InputSurface = inputView
        };

        _ringIndex = (_ringIndex + 1) % RingSize;
        _videoContext.VideoProcessorBlt(_videoProcessor, _outputViews[_ringIndex], 0, new[] { stream });

        // The encoder MFT reads this texture on its own queue and does not wait for the blit: without this wait it
        // encoded the slot's previous content (a frame ring-size updates old).
        _context.End(_blitDone);
        _context.Flush();
        var spin = new SpinWait();
        while (!_context.GetData(_blitDone, out int _)) spin.SpinOnce(-1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _blitDone.Dispose();
        _staging?.Dispose();
        _lastDesktopTexture?.Dispose();
        for (int i = 0; i < RingSize; i++)
        {
            _outputViews[i].Dispose();
            _nv12Ring[i].Dispose();
        }
        _videoProcessor.Dispose();
        _enumerator.Dispose();
        _dda.Dispose();
        _output1.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
        _context.Dispose();
        _device.Dispose();
        _adapter.Dispose();
        _factory.Dispose();
    }
}
