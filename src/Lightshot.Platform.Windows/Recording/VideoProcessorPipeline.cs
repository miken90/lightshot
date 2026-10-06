// Ported for Lightshot Windows Port (Phase 7 R2)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// GPU video processing pipeline that crops, scales, and converts BGRA frames into NV12 textures
/// for hardware/software Media Foundation sink writers.
/// Prefers ID3D11VideoProcessor hardware acceleration, falling back to shader/CPU conversion on WARP.
/// </summary>
public sealed class VideoProcessorPipeline : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly int _outputWidth;
    private readonly int _outputHeight;

    private readonly ID3D11Texture2D _nv12Texture;
    private readonly ID3D11Texture2D? _stagingBgra;
    private readonly ID3D11Texture2D? _stagingNv12;

    private ID3D11VideoDevice? _videoDevice;
    private ID3D11VideoContext? _videoContext;
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _videoProcessor;
    private bool _hardwareProcessorReady;
    private bool _disposed;

    public int InputWidth => _inputWidth;
    public int InputHeight => _inputHeight;
    public int OutputWidth => _outputWidth;
    public int OutputHeight => _outputHeight;
    public ID3D11Texture2D OutputTexture => _nv12Texture;

    public VideoProcessorPipeline(
        ID3D11Device device,
        ID3D11DeviceContext context,
        int inputWidth,
        int inputHeight,
        int outputWidth,
        int outputHeight,
        int fps = 30)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _inputWidth = Math.Max(2, (inputWidth / 2) * 2);
        _inputHeight = Math.Max(2, (inputHeight / 2) * 2);
        _outputWidth = Math.Max(2, (outputWidth / 2) * 2);
        _outputHeight = Math.Max(2, (outputHeight / 2) * 2);

        // 1. Create NV12 destination texture
        var nv12Desc = new Texture2DDescription
        {
            Width = (uint)_outputWidth,
            Height = (uint)_outputHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };
        _nv12Texture = _device.CreateTexture2D(nv12Desc);

        // 2. Try initializing hardware ID3D11VideoProcessor
        try
        {
            _videoDevice = _device.QueryInterfaceOrNull<ID3D11VideoDevice>();
            _videoContext = _context.QueryInterfaceOrNull<ID3D11VideoContext>();

            if (_videoDevice != null && _videoContext != null)
            {
                var contentDesc = new VideoProcessorContentDescription
                {
                    InputWidth = (uint)_inputWidth,
                    InputHeight = (uint)_inputHeight,
                    InputFrameFormat = VideoFrameFormat.Progressive,
                    OutputWidth = (uint)_outputWidth,
                    OutputHeight = (uint)_outputHeight,
                    OutputFrameRate = new Rational((uint)fps, 1),
                    InputFrameRate = new Rational((uint)fps, 1),
                    Usage = VideoUsage.PlaybackNormal
                };

                var hrEnum = _videoDevice.CreateVideoProcessorEnumerator(ref contentDesc, out _enumerator);
                if (hrEnum.Success && _enumerator != null)
                {
                    var hrProc = _videoDevice.CreateVideoProcessor(_enumerator, 0, out _videoProcessor);
                    if (hrProc.Success && _videoProcessor != null)
                    {
                        _hardwareProcessorReady = true;
                    }
                }
            }
        }
        catch
        {
            _hardwareProcessorReady = false;
        }

        // 3. If hardware processor is not available, allocate staging textures for fallback conversion
        if (!_hardwareProcessorReady)
        {
            _stagingBgra = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_inputWidth,
                Height = (uint)_inputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read,
                MiscFlags = ResourceOptionFlags.None
            });

            _stagingNv12 = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_outputWidth,
                Height = (uint)_outputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Write,
                MiscFlags = ResourceOptionFlags.None
            });
        }
    }

    /// <summary>
    /// Creates a compatible intermediate BGRA texture sized to this pipeline's input dimensions,
    /// suitable for Direct2D render targets and compositor burn-in.
    /// </summary>
    public ID3D11Texture2D CreateBgraIntermediateTexture()
    {
        return _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_inputWidth,
            Height = (uint)_inputHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        });
    }

    /// <summary>
    /// Processes the source BGRA texture into the destination NV12 texture.
    /// </summary>
    public void Process(ID3D11Texture2D bgraSource, Rect? sourceCrop = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bgraSource);

        if (_hardwareProcessorReady && _videoDevice != null && _videoContext != null && _enumerator != null && _videoProcessor != null)
        {
            try
            {
                var inputViewDesc = new VideoProcessorInputViewDescription
                {
                    FourCC = 0,
                    ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 }
                };

                var outputViewDesc = new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 }
                };

                if (_videoDevice.CreateVideoProcessorInputView(bgraSource, _enumerator, inputViewDesc, out var inputView).Success &&
                    _videoDevice.CreateVideoProcessorOutputView(_nv12Texture, _enumerator, outputViewDesc, out var outputView).Success &&
                    inputView != null && outputView != null)
                {
                    using (inputView)
                    using (outputView)
                    {
                        var stream = new VideoProcessorStream
                        {
                            Enable = true,
                            OutputIndex = 0,
                            InputFrameOrField = 0,
                            PastFrames = 0,
                            FutureFrames = 0,
                            InputSurface = inputView
                        };

                        if (sourceCrop.HasValue)
                        {
                            var sc = sourceCrop.Value;
                            var srcRect = new Vortice.RawRect((int)sc.X, (int)sc.Y, (int)(sc.X + sc.Width), (int)(sc.Y + sc.Height));
                            _videoContext.VideoProcessorSetStreamSourceRect(_videoProcessor, 0, true, srcRect);
                        }

                        var dstRect = new Vortice.RawRect(0, 0, _outputWidth, _outputHeight);
                        _videoContext.VideoProcessorSetStreamDestRect(_videoProcessor, 0, true, dstRect);
                        _videoContext.VideoProcessorSetOutputTargetRect(_videoProcessor, true, dstRect);

                        _videoContext.VideoProcessorBlt(_videoProcessor, outputView, 0, 1, new[] { stream });
                        return;
                    }
                }
            }
            catch
            {
                // Fall through to fallback path if hardware Blt threw
            }
        }

        // Fallback conversion path
        ProcessFallback(bgraSource, sourceCrop);
    }

    private void ProcessFallback(ID3D11Texture2D bgraSource, Rect? sourceCrop)
    {
        if (_stagingBgra == null || _stagingNv12 == null) return;

        // Copy source to staging
        _context.CopyResource(_stagingBgra, bgraSource);

        var mappedSrc = _context.Map(_stagingBgra, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var mappedDst = _context.Map(_stagingNv12, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);

        try
        {
            int srcPitch = (int)mappedSrc.RowPitch;
            int dstPitch = (int)mappedDst.RowPitch;

            int cropX = sourceCrop.HasValue ? Math.Max(0, (int)sourceCrop.Value.X) : 0;
            int cropY = sourceCrop.HasValue ? Math.Max(0, (int)sourceCrop.Value.Y) : 0;
            int cropW = sourceCrop.HasValue ? Math.Min(_inputWidth - cropX, (int)sourceCrop.Value.Width) : _inputWidth;
            int cropH = sourceCrop.HasValue ? Math.Min(_inputHeight - cropY, (int)sourceCrop.Value.Height) : _inputHeight;

            unsafe
            {
                byte* pSrc = (byte*)mappedSrc.DataPointer;
                byte* pDstY = (byte*)mappedDst.DataPointer;
                byte* pDstUV = pDstY + (dstPitch * _outputHeight);

                double scaleX = (double)cropW / _outputWidth;
                double scaleY = (double)cropH / _outputHeight;

                for (int y = 0; y < _outputHeight; y++)
                {
                    int srcY = cropY + (int)Math.Min(cropH - 1, y * scaleY);
                    byte* srcRow = pSrc + (srcY * srcPitch);
                    byte* dstRowY = pDstY + (y * dstPitch);

                    for (int x = 0; x < _outputWidth; x++)
                    {
                        int srcX = cropX + (int)Math.Min(cropW - 1, x * scaleX);
                        byte* srcPixel = srcRow + (srcX * 4);

                        byte b = srcPixel[0];
                        byte g = srcPixel[1];
                        byte r = srcPixel[2];

                        // BT.601 RGB to Y
                        int yVal = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                        dstRowY[x] = (byte)Math.Clamp(yVal, 16, 235);
                    }
                }

                // UV plane: 2x2 subsampling
                for (int y = 0; y < _outputHeight / 2; y++)
                {
                    int srcY = cropY + (int)Math.Min(cropH - 1, (y * 2) * scaleY);
                    byte* srcRow = pSrc + (srcY * srcPitch);
                    byte* dstRowUV = pDstUV + (y * dstPitch);

                    for (int x = 0; x < _outputWidth / 2; x++)
                    {
                        int srcX = cropX + (int)Math.Min(cropW - 1, (x * 2) * scaleX);
                        byte* srcPixel = srcRow + (srcX * 4);

                        byte b = srcPixel[0];
                        byte g = srcPixel[1];
                        byte r = srcPixel[2];

                        // BT.601 RGB to U/V
                        int uVal = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                        int vVal = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

                        dstRowUV[x * 2] = (byte)Math.Clamp(uVal, 16, 240);
                        dstRowUV[x * 2 + 1] = (byte)Math.Clamp(vVal, 16, 240);
                    }
                }
            }
        }
        finally
        {
            _context.Unmap(_stagingNv12, 0);
            _context.Unmap(_stagingBgra, 0);
        }

        // Copy converted staging to default GPU texture
        _context.CopyResource(_nv12Texture, _stagingNv12);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _videoProcessor?.Dispose();
        _enumerator?.Dispose();
        _videoContext?.Dispose();
        _videoDevice?.Dispose();

        _stagingNv12?.Dispose();
        _stagingBgra?.Dispose();
        _nv12Texture.Dispose();
    }
}
