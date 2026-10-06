// Ported for Lightshot Windows Port (Phase 7 R2)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using Lightshot.Platform.Windows.Displays;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Lightshot.Platform.Windows.Recording;

public enum FrameAcquireStatus
{
    Success,
    Duplicate,
    Timeout,
    ResolutionChanged,
    Failed
}

/// <summary>
/// DXGI Desktop Duplication API (DDA) frame source.
/// Runs on the target display's DXGI adapter, returns cached frames on timeout (idle screen),
/// recreates duplication on DXGI_ERROR_ACCESS_LOST, and detects resolution changes mid-take.
/// </summary>
public sealed class DdaFrameSource : IDisposable
{
    private const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
    private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    private const int DXGI_ERROR_MODE_CHANGE_IN_PROGRESS = unchecked((int)0x887A0025);

    private readonly DisplayInfo _display;
    private readonly int _initialWidth;
    private readonly int _initialHeight;

    private IDXGIFactory1? _factory;
    private IDXGIAdapter1? _adapter;
    private IDXGIOutput1? _output1;
    private IDXGIOutputDuplication? _duplication;

    private ID3D11Device _device;
    private ID3D11DeviceContext _context;
    private readonly bool _ownsDevice;

    private ID3D11Texture2D? _lastDesktopTexture;
    private bool _disposed;

    public ID3D11Device Device => _device;
    public ID3D11DeviceContext Context => _context;
    public DisplayInfo Display => _display;
    public int Width => _initialWidth;
    public int Height => _initialHeight;
    public ID3D11Texture2D? LastFrame => _lastDesktopTexture;

    public DdaFrameSource(DisplayInfo display, ID3D11Device? existingDevice = null, ID3D11DeviceContext? existingContext = null)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _initialWidth = display.PhysicalWidth > 0 ? display.PhysicalWidth : (int)display.Bounds.Width;
        _initialHeight = display.PhysicalHeight > 0 ? display.PhysicalHeight : (int)display.Bounds.Height;

        if (existingDevice != null && existingContext != null)
        {
            _device = existingDevice;
            _context = existingContext;
            _ownsDevice = false;

            try
            {
                using var multithread = _device.QueryInterface<ID3D11Multithread>();
                multithread?.SetMultithreadProtected(true);
            }
            catch { }

            _factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            FindAdapterAndOutput(_factory, _display, out _adapter, out var output);

            if (output == null)
            {
                throw new InvalidOperationException($"No DXGI output found for display {_display.DeviceName}");
            }

            _output1 = output.QueryInterface<IDXGIOutput1>();
            output.Dispose();

            _duplication = _output1.DuplicateOutput(_device);
        }
        else
        {
            _ownsDevice = true;

            _factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            FindAdapterAndOutput(_factory, _display, out _adapter, out var output);

            if (_adapter == null || output == null)
            {
                throw new InvalidOperationException($"No DXGI adapter or output found for display {_display.DeviceName}");
            }

            var hrDevice = D3D11.D3D11CreateDevice(
                _adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                out ID3D11Device? dev,
                out ID3D11DeviceContext? ctx);

            if (!hrDevice.Success || dev == null || ctx == null)
            {
                output.Dispose();
                throw new InvalidOperationException($"Failed to create D3D11 device on adapter: {hrDevice}");
            }

            _device = dev;
            _context = ctx;

            try
            {
                using var multithread = _device.QueryInterface<ID3D11Multithread>();
                multithread?.SetMultithreadProtected(true);
            }
            catch { }

            _output1 = output.QueryInterface<IDXGIOutput1>();
            output.Dispose();

            _duplication = _output1.DuplicateOutput(_device);
        }
    }

    private static void FindAdapterAndOutput(
        IDXGIFactory1 factory,
        DisplayInfo display,
        out IDXGIAdapter1? matchedAdapter,
        out IDXGIOutput? matchedOutput)
    {
        matchedAdapter = null;
        matchedOutput = null;

        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            if (adapter == null) continue;
            for (uint j = 0; adapter.EnumOutputs(j, out var output).Success; j++)
            {
                if (output == null) continue;
                if (string.Equals(output.Description.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    matchedAdapter = adapter;
                    matchedOutput = output;
                    return;
                }
                output.Dispose();
            }
            if (matchedOutput != null) return;
            adapter.Dispose();
        }

        // Fallback to first available adapter and output
        if (factory.EnumAdapters1(0, out var fallbackAdapter).Success && fallbackAdapter != null)
        {
            if (fallbackAdapter.EnumOutputs(0, out var fallbackOutput).Success && fallbackOutput != null)
            {
                matchedAdapter = fallbackAdapter;
                matchedOutput = fallbackOutput;
            }
        }
    }

    /// <summary>
    /// Attempts to acquire the next desktop frame within the specified timeout.
    /// Handles duplicate frames on timeout, recreates on access lost, and detects resolution changes.
    /// </summary>
    public FrameAcquireStatus AcquireFrame(
        int timeoutMs,
        out ID3D11Texture2D? texture,
        out string? errorMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        texture = null;
        errorMessage = null;

        if (_duplication == null)
        {
            if (!TryRecreateDuplication(out errorMessage))
            {
                return FrameAcquireStatus.Failed;
            }
        }

        var hr = _duplication!.AcquireNextFrame((uint)timeoutMs, out _, out var desktopResource);

        if (hr.Success)
        {
            try
            {
                if (desktopResource != null)
                {
                    using var rawTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
                    var desc = rawTexture.Description;

                    // Check for resolution change mid-take
                    if (desc.Width != _initialWidth || desc.Height != _initialHeight)
                    {
                        errorMessage = $"Display resolution changed from {_initialWidth}x{_initialHeight} to {desc.Width}x{desc.Height}";
                        return FrameAcquireStatus.ResolutionChanged;
                    }

                    // Copy acquired texture to persistent buffer
                    if (_lastDesktopTexture == null)
                    {
                        var copyDesc = desc;
                        copyDesc.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget;
                        copyDesc.Usage = ResourceUsage.Default;
                        copyDesc.MiscFlags = ResourceOptionFlags.None;
                        _lastDesktopTexture = _device.CreateTexture2D(copyDesc);
                    }

                    _context.CopyResource(_lastDesktopTexture, rawTexture);
                    texture = _lastDesktopTexture;
                    return FrameAcquireStatus.Success;
                }
                else if (_lastDesktopTexture != null)
                {
                    texture = _lastDesktopTexture;
                    return FrameAcquireStatus.Duplicate;
                }
            }
            finally
            {
                desktopResource?.Dispose();
                try { _duplication.ReleaseFrame(); } catch { }
            }
        }

        int code = (int)hr.Code;

        // 1. Timeout / Wait Timeout (no screen changes detected by DWM)
        if (code == DXGI_ERROR_WAIT_TIMEOUT)
        {
            if (_lastDesktopTexture != null)
            {
                texture = _lastDesktopTexture;
                return FrameAcquireStatus.Duplicate;
            }
            return FrameAcquireStatus.Timeout;
        }

        // 2. Access Lost or Invalid Call (desktop switch, UAC prompt, fullscreen transition, invalid call)
        if (code == DXGI_ERROR_ACCESS_LOST || code == DXGI_ERROR_MODE_CHANGE_IN_PROGRESS || code == unchecked((int)0x887A0001))
        {
            if (TryRecreateDuplication(out errorMessage))
            {
                if (_lastDesktopTexture != null)
                {
                    texture = _lastDesktopTexture;
                    return FrameAcquireStatus.Duplicate;
                }
                return FrameAcquireStatus.Timeout;
            }

            return FrameAcquireStatus.Failed;
        }

        errorMessage = $"AcquireNextFrame failed: {hr}";
        return FrameAcquireStatus.Failed;
    }

    private bool TryRecreateDuplication(out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            _duplication?.Dispose();
            _duplication = null;

            if (_output1 == null)
            {
                if (_adapter == null || _factory == null)
                {
                    _factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                    FindAdapterAndOutput(_factory, _display, out _adapter, out var outObj);
                    if (outObj != null)
                    {
                        _output1 = outObj.QueryInterface<IDXGIOutput1>();
                        outObj.Dispose();
                    }
                }
            }

            if (_output1 == null)
            {
                errorMessage = "Failed to obtain IDXGIOutput1 for duplication recreation";
                return false;
            }

            // Verify resolution has not changed
            var coords = _output1.Description.DesktopCoordinates;
            int curW = coords.Right - coords.Left;
            int curH = coords.Bottom - coords.Top;
            if (curW > 0 && curH > 0 && (curW != _initialWidth || curH != _initialHeight))
            {
                errorMessage = $"Display resolution changed on recreation from {_initialWidth}x{_initialHeight} to {curW}x{curH}";
                return false;
            }

            _duplication = _output1.DuplicateOutput(_device);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Recreating duplication failed: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _lastDesktopTexture?.Dispose();
        _duplication?.Dispose();
        _output1?.Dispose();
        _adapter?.Dispose();
        _factory?.Dispose();

        if (_ownsDevice)
        {
            _context.Dispose();
            _device.Dispose();
        }
    }
}
