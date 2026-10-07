using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Interop;
using Vortice.DXGI;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Discovers all active monitors and coordinates a set of OverlayWindow instances
/// ensuring a unified selection state across multi-monitor setups.
/// </summary>
public sealed class OverlayHost : IDisposable
{
    public record MonitorInfo(uint Id, Rect Bounds, bool IsPrimary);

    private readonly List<OverlayWindow> _windows = new();
    private TaskCompletionSource<CaptureRegion?>? _completionTcs;
    private bool _disposed;

    public IReadOnlyList<OverlayWindow> Windows => _windows;
    public IntPtr PrimaryHwnd => _windows.Count > 0 ? _windows[0].Handle : IntPtr.Zero;

    public OverlayHost(FrozenScreen? frozen = null)
    {
        var monitors = DiscoverMonitors();
        uint id = 1;
        foreach (var mon in monitors)
        {
            var win = new OverlayWindow(id++, mon.Bounds);

            // Match frozen display backdrop if available
            if (frozen != null)
            {
                var disp = frozen.Displays.FirstOrDefault(d =>
                    Math.Abs(d.Frame.MinX - mon.Bounds.MinX) < 2 &&
                    Math.Abs(d.Frame.MinY - mon.Bounds.MinY) < 2);
                if (disp != null)
                {
                    win.Backdrop = disp.Image;
                }
            }

            win.DragBeganOnThisWindow = OnDragBegan;
            win.SelectionCompleted = OnSelectionCompleted;
            _windows.Add(win);
        }
    }

    public Task<CaptureRegion?> RunSelectionAsync(bool adjustable, int magnifierZoom = 4)
    {
        _completionTcs = new TaskCompletionSource<CaptureRegion?>(TaskCreationOptions.RunContinuationsAsynchronously);

        foreach (var win in _windows)
        {
            win.Mode = OverlayMode.Region;
            win.IsAdjustable = adjustable;
            win.MagnifierZoom = magnifierZoom;
            win.ClearSelection();
            win.Show();
        }

        // Grant foreground to primary window
        if (PrimaryHwnd != IntPtr.Zero)
        {
            ForegroundGrant.GrantForeground(PrimaryHwnd);
        }

        return _completionTcs.Task;
    }

    public Task<CaptureRegion?> RunWindowPickerAsync(IReadOnlyList<FrozenWindow> windows)
    {
        _completionTcs = new TaskCompletionSource<CaptureRegion?>(TaskCreationOptions.RunContinuationsAsynchronously);

        foreach (var win in _windows)
        {
            win.Mode = OverlayMode.Window;
            win.CandidateWindows = windows;
            win.Show();
        }

        if (PrimaryHwnd != IntPtr.Zero)
        {
            ForegroundGrant.GrantForeground(PrimaryHwnd);
        }

        return _completionTcs.Task;
    }

    private void OnDragBegan(OverlayWindow activeWindow)
    {
        // Only one selection exists across monitors: clear others
        foreach (var win in _windows)
        {
            if (!ReferenceEquals(win, activeWindow))
            {
                win.ClearSelection();
            }
        }
    }

    private void OnSelectionCompleted(CaptureRegion? result)
    {
        HideAllWindows();
        _completionTcs?.TrySetResult(result);
    }

    public void HideAllWindows()
    {
        foreach (var win in _windows)
        {
            win.Hide();
        }
    }

    public static List<MonitorInfo> DiscoverMonitors()
    {
        var result = new List<MonitorInfo>();

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            uint monId = 1;

            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                if (adapter == null) continue;
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    if (output == null) continue;
                    var c = output.Description.DesktopCoordinates;
                    int w = c.Right - c.Left;
                    int h = c.Bottom - c.Top;

                    if (w > 0 && h > 0)
                    {
                        bool isPrimary = (c.Left == 0 && c.Top == 0);
                        result.Add(new MonitorInfo(monId++, new Rect(c.Left, c.Top, w, h), isPrimary));
                    }
                    output.Dispose();
                }
                adapter.Dispose();
            }
        }
        catch
        {
            // DXGI enumeration fallback via GDI EnumDisplayMonitors
        }

        if (result.Count == 0)
        {
            // Fallback: Primary monitor via GetSystemMetrics
            int w = GetSystemMetrics(0 /* SM_CXSCREEN */);
            int h = GetSystemMetrics(1 /* SM_CYSCREEN */);
            result.Add(new MonitorInfo(1, new Rect(0, 0, Math.Max(w, 800), Math.Max(h, 600)), true));
        }

        // Primary first
        result.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
        return result;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var win in _windows)
        {
            win.Dispose();
        }
        _windows.Clear();
    }
}
