using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectComposition;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Manages DirectComposition device, target, and visual for an overlay window.
/// </summary>
public sealed class DCompSurface : IDisposable
{
    private IDCompositionDevice? _device;
    private IDCompositionTarget? _target;
    private IDCompositionVisual? _rootVisual;
    private bool _disposed;

    public bool IsSupported => _device != null;
    internal IDCompositionDevice? Device => _device;
    internal IDCompositionTarget? Target => _target;
    internal IDCompositionVisual? RootVisual => _rootVisual;

    public DCompSurface(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        Initialize(hWnd);
    }

    private unsafe void Initialize(IntPtr hWnd)
    {
        try
        {
            Guid iid = typeof(IDCompositionDevice).GUID;
            var hr = PInvoke.DCompositionCreateDevice(null, &iid, out object pDevObj);
            if (hr.Value == 0 && pDevObj is IDCompositionDevice dev)
            {
                _device = dev;
                _device.CreateTargetForHwnd((HWND)hWnd, true, out var target);
                _device.CreateVisual(out var visual);
                if (target != null && visual != null)
                {
                    target.SetRoot(visual);
                    _target = target;
                    _rootVisual = visual;
                    _device.Commit();
                }
            }
        }
        catch
        {
            // DirectComposition unavailable on this hardware/context
        }
    }

    public void Commit()
    {
        if (_device != null && !_disposed)
        {
            try
            {
                _device.Commit();
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_rootVisual != null)
        {
            try { Marshal.ReleaseComObject(_rootVisual); } catch { }
            _rootVisual = null;
        }

        if (_target != null)
        {
            try { Marshal.ReleaseComObject(_target); } catch { }
            _target = null;
        }

        if (_device != null)
        {
            try { Marshal.ReleaseComObject(_device); } catch { }
            _device = null;
        }
    }
}
