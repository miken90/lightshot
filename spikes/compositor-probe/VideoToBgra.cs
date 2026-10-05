using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CompositorProbe;

/// <summary>D3D11 video processor: decoder NV12 (BT.709, limited range) to a BGRA full-range texture the compositor samples.</summary>
public sealed class VideoToBgra : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11VideoProcessorOutputView _outputView;
    private readonly Dictionary<(IntPtr, uint), ID3D11VideoProcessorInputView> _inputViews = new();
    public ID3D11Texture2D Output { get; }

    public VideoToBgra(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        _device = device;
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();

        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = (uint)width, InputHeight = (uint)height,
            OutputWidth = (uint)width, OutputHeight = (uint)height,
            Usage = VideoUsage.PlaybackNormal
        };
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(content);
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

        Output = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
        });
        _outputView = _videoDevice.CreateVideoProcessorOutputView(Output, _enumerator,
            new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });

        // YCbCr_Matrix 1 = BT.709, Nominal_Range 1 = 16-235; RGB_Range 0 = full.
        _videoContext.VideoProcessorSetStreamColorSpace(_processor, 0,
            new VideoProcessorColorSpace { Usage = 0, RGB_Range = 0, YCbCr_Matrix = 1, YCbCr_xvYCC = 0, Nominal_Range = 1 });
        _videoContext.VideoProcessorSetOutputColorSpace(_processor, new VideoProcessorColorSpace { Usage = 0, RGB_Range = 0, YCbCr_Matrix = 1, Nominal_Range = 2 });
    }

    public void Convert(DecodedFrame frame)
    {
        var key = (frame.Texture.NativePointer, frame.Slice);
        if (!_inputViews.TryGetValue(key, out var view))
        {
            view = _videoDevice.CreateVideoProcessorInputView(frame.Texture, _enumerator, new VideoProcessorInputViewDescription
            {
                FourCC = 0, ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = frame.Slice }
            });
            _inputViews[key] = view;
        }
        var stream = new VideoProcessorStream { Enable = true, InputSurface = view };
        _videoContext.VideoProcessorBlt(_processor, _outputView, 0, 1, new[] { stream }).CheckError();
    }

    public void Dispose()
    {
        foreach (var v in _inputViews.Values) v.Dispose();
        _outputView.Dispose(); Output.Dispose(); _processor.Dispose(); _enumerator.Dispose();
        _videoContext.Dispose(); _videoDevice.Dispose();
    }
}
