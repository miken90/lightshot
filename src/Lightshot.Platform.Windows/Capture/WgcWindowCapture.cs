// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Lightshot.Core;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// Window capture using Windows Graphics Capture (WGC) with IsBorderRequired = false,
/// falling back to PrintWindowCapture if WGC is unavailable or unsupported.
/// </summary>
public static class WgcWindowCapture
{
    [ComImport]
    [Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow([In] IntPtr hWnd, [In] ref Guid riid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor([In] IntPtr hMonitor, [In] ref Guid riid, out IntPtr result);
    }

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static Result<CapturedImage, CaptureError> CaptureWindow(IntPtr hWnd, Rect? expectedBounds = null)
    {
        if (hWnd == IntPtr.Zero)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Invalid window handle"));
        }

        try
        {
            if (GraphicsCaptureSession.IsSupported())
            {
                var wgcResult = TryCaptureWgc(hWnd);
                if (wgcResult.IsSuccess)
                {
                    return wgcResult;
                }
            }
        }
        catch
        {
            // Fallback to PrintWindow
        }

        return PrintWindowCapture.CaptureWindow(hWnd, expectedBounds);
    }

    private static Result<CapturedImage, CaptureError> TryCaptureWgc(IntPtr hWnd)
    {
        var item = CreateItemForWindow(hWnd);
        if (item == null)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Failed to create GraphicsCaptureItem"));
        }

        int width = item.Size.Width;
        int height = item.Size.Height;
        if (width <= 0 || height <= 0)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Zero window size in WGC item"));
        }

        var hrDevice = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 },
            out ID3D11Device? d3dDevice,
            out ID3D11DeviceContext? d3dContext);

        if (!hrDevice.Success || d3dDevice == null || d3dContext == null)
        {
            return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Failed to create D3D11 device for WGC"));
        }

        using (d3dDevice)
        using (d3dContext)
        {
            using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
            int hrWinrt = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pInspectable);
            if (hrWinrt != 0 || pInspectable == IntPtr.Zero)
            {
                return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("CreateDirect3D11DeviceFromDXGIDevice failed"));
            }

            var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pInspectable);
            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);

            using var session = framePool.CreateCaptureSession(item);
            try
            {
                session.IsBorderRequired = false;
            }
            catch
            {
                // IsBorderRequired is best effort on older builds
            }

            var frameArrived = new ManualResetEventSlim(false);
            Direct3D11CaptureFrame? acquiredFrame = null;
            var frameLock = new object();

            framePool.FrameArrived += (s, a) =>
            {
                lock (frameLock)
                {
                    if (acquiredFrame == null)
                    {
                        acquiredFrame = framePool.TryGetNextFrame();
                        if (acquiredFrame != null)
                        {
                            frameArrived.Set();
                        }
                    }
                }
            };

            session.StartCapture();
            bool gotFrame = frameArrived.Wait(1000);

            if (!gotFrame || acquiredFrame == null)
            {
                return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("WGC timed out waiting for frame"));
            }

            using (acquiredFrame)
            {
                var surface = acquiredFrame.Surface;
                var interop = surface.As<IDirect3DDxgiInterfaceAccess>();
                var surfaceGuid = typeof(ID3D11Texture2D).GUID;
                interop.GetInterface(surfaceGuid, out IntPtr pSurface);
                if (pSurface == IntPtr.Zero)
                {
                    return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Failed to get ID3D11Texture2D from WGC surface"));
                }

                using var texture = new ID3D11Texture2D(pSurface);
                var desc = texture.Description;

                var stagingDesc = new Texture2DDescription
                {
                    Width = desc.Width,
                    Height = desc.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = desc.Format,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    MiscFlags = ResourceOptionFlags.None
                };

                using var staging = d3dDevice.CreateTexture2D(stagingDesc);
                d3dContext.CopyResource(staging, texture);

                var mapped = d3dContext.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    byte[] pixels = new byte[desc.Width * desc.Height * 4];
                    int rowPitch = (int)mapped.RowPitch;
                    unsafe
                    {
                        byte* src = (byte*)mapped.DataPointer;
                        fixed (byte* dst = pixels)
                        {
                            for (int y = 0; y < (int)desc.Height; y++)
                            {
                                Buffer.MemoryCopy(src + y * rowPitch, dst + y * desc.Width * 4, desc.Width * 4, desc.Width * 4);
                            }
                        }
                    }

                    if (BlackFrameDetector.IsBlackFrame(pixels, (int)desc.Width, (int)desc.Height))
                    {
                        return Result<CapturedImage, CaptureError>.Failure(new CaptureError.SystemFailure("Black frame detected in WGC window capture"));
                    }

                    return Result<CapturedImage, CaptureError>.Success(new CapturedImage((int)desc.Width, (int)desc.Height, pixels));
                }
                finally
                {
                    d3dContext.Unmap(staging, 0);
                }
            }
        }
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface([In] ref Guid iid, out IntPtr p);
    }

    private static GraphicsCaptureItem? CreateItemForWindow(IntPtr hWnd)
    {
        IntPtr hString = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        try
        {
            string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            int hr = WindowsCreateString(className, className.Length, out hString);
            if (hr != 0) return null;

            var interopGuid = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            hr = RoGetActivationFactory(hString, ref interopGuid, out factory);
            if (hr != 0 || factory == IntPtr.Zero) return null;

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            var itemGuid = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            hr = interop.CreateForWindow(hWnd, ref itemGuid, out IntPtr pItem);
            if (hr != 0 || pItem == IntPtr.Zero) return null;

            return MarshalInterface<GraphicsCaptureItem>.FromAbi(pItem);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (hString != IntPtr.Zero) WindowsDeleteString(hString);
        }
    }
}
