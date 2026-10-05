using System;
using System.Diagnostics;
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
    private readonly ID3D11Texture2D _nv12Texture;
    private readonly ID3D11VideoProcessorOutputView _outputView;

    private ID3D11Texture2D? _lastDesktopTexture;
    private bool _disposed;

    public ID3D11Device Device => _device;
    public ID3D11Texture2D Nv12Texture => _nv12Texture;
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public string AdapterName { get; }

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
        ScreenWidth = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
        ScreenHeight = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

        _dda = _output1.DuplicateOutput(_device);

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
        _nv12Texture = _device.CreateTexture2D(nv12Desc);

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
        _outputView = _videoDevice.CreateVideoProcessorOutputView(_nv12Texture, _enumerator, outViewDesc);
    }

    public bool AcquireAndProcessFrame(int timeoutMs, out long qpcTime)
    {
        qpcTime = Stopwatch.GetTimestamp();

        var hr = _dda.AcquireNextFrame((uint)timeoutMs, out _, out var desktopResource);
        if (hr.Success && desktopResource != null)
        {
            using var rawTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
            _dda.ReleaseFrame();

            // Cache a copy in case of subsequent timeouts (static screen)
            if (_lastDesktopTexture == null)
            {
                var copyDesc = rawTexture.Description;
                copyDesc.BindFlags = BindFlags.None;
                copyDesc.Usage = ResourceUsage.Default;
                _lastDesktopTexture = _device.CreateTexture2D(copyDesc);
            }
            _context.CopyResource(_lastDesktopTexture, rawTexture);

            ConvertTextureToNv12(rawTexture);
            return true;
        }

        // On timeout, blit last cached frame if present
        if (_lastDesktopTexture != null)
        {
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

        _videoContext.VideoProcessorBlt(_videoProcessor, _outputView, 0, new[] { stream });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lastDesktopTexture?.Dispose();
        _outputView.Dispose();
        _nv12Texture.Dispose();
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
