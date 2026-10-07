using System;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Capture;
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
    private readonly Func<Task<FrozenScreen?>> _freezeForRecording;
    private bool _disposed;

    /// <param name="freezeForRecording">Captures the still the recording selection dims over;
    /// defaults to a DDA freeze of every display.</param>
    public WindowsOverlayController(ShellThread? shellThread = null, Func<Task<FrozenScreen?>>? freezeForRecording = null)
    {
        _freezeForRecording = freezeForRecording ?? FreezeAllDisplaysAsync;

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

    public async Task<RecordingChoice?> SelectRecordingAsync(CaptureRegion? initial, RecordingDefaults defaults)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Without a still underneath, the painter tints plain black and the screen disappears.
        // Freeze before the overlay windows exist, as the screenshot selection does.
        var frozen = await _freezeForRecording();

        return await _shellThread.InvokeAsync(async () =>
        {
            using var coordinator = new RecordingChromeCoordinator();
            return await coordinator.ShowSelectionAsync(initial, defaults, frozen);
        }).Unwrap();
    }

    private static async Task<FrozenScreen?> FreezeAllDisplaysAsync()
    {
        var outcome = await new WindowsCaptureService().FreezeScreenAsync();
        return outcome.IsSuccess ? outcome.Value : null;
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
