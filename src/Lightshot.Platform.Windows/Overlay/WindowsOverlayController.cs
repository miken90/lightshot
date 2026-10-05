using System;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Shell;

namespace Lightshot.Platform.Windows.Overlay;

/// <summary>
/// Windows implementation of IOverlayController managing pre-capture selection
/// overlays on a dedicated STA shell thread.
/// </summary>
public sealed class WindowsOverlayController : IOverlayController, IDisposable
{
    private readonly ShellThread _shellThread;
    private readonly bool _ownsShellThread;
    private bool _disposed;

    public WindowsOverlayController(ShellThread? shellThread = null)
    {
        if (shellThread != null)
        {
            _shellThread = shellThread;
            _ownsShellThread = false;
        }
        else
        {
            _shellThread = new ShellThread("LightshotOverlayThread");
            _ownsShellThread = true;
        }
    }

    public async Task<CaptureRegion?> SelectRegionAsync(FrozenScreen? frozen, bool adjustable)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await _shellThread.InvokeAsync(async () =>
        {
            using var host = new OverlayHost(frozen);
            return await host.RunSelectionAsync(adjustable);
        }).Unwrap();
    }

    public async Task<CaptureRegion?> SelectWindowAsync(FrozenScreen? frozen)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await _shellThread.InvokeAsync(async () =>
        {
            using var host = new OverlayHost(frozen);
            var windows = frozen?.WindowList ?? [];
            return await host.RunWindowPickerAsync(windows);
        }).Unwrap();
    }

    public Task<RecordingChoice?> SelectRecordingAsync(CaptureRegion? initial, RecordingDefaults defaults)
    {
        // Recording selection overlay belongs to Phase 7
        return Task.FromResult<RecordingChoice?>(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsShellThread)
        {
            _shellThread.Dispose();
        }
    }
}
