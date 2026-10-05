// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Lightshot.App.Views.Editor;
using Lightshot.App.Views.Notices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Capture;
using Lightshot.Platform.Windows.Clipboard;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Files;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Shell;
using Lightshot.Platform.Windows.Tray;
using Lightshot.Platform.Windows.Windows;
using Lightshot.Rendering;

namespace Lightshot.App;

/// <summary>
/// Main application controller composing Core AppCoordinator with Windows platform implementations:
/// capture service, overlay host, hotkeys, tray, window presenter, sinks, settings store, and editor.
/// </summary>
public sealed class AppController : ICaptureUI, IDisposable
{
    private readonly ShellThread _shellThread;
    private readonly DisplayTopology _topology;
    private readonly ISettingsStore _settingsStore;
    private readonly IImageCodec _codec;
    private readonly IImageRenderer _renderer;
    private readonly ClipboardImageSink _clipboardSink;
    private readonly FileImageSink _fileImageSink;
    private readonly IImageSink _imageSink;
    private readonly IImageSource _imageSource;
    private readonly ICaptureService _captureService;
    private readonly WindowsOverlayController _overlay;
    private readonly WindowsHotkeyService _hotkeys;
    private readonly TrayIcon _trayIcon;
    private readonly AppCoordinator _coordinator;

    private EditorWindow? _activeEditorWindow;
    private bool _disposed;

    public AppCoordinator Coordinator => _coordinator;
    public TrayIcon Tray => _trayIcon;
    public ISettingsStore Settings => _settingsStore;

    public AppController(
        ShellThread? shellThread = null,
        DisplayTopology? topology = null,
        ISettingsStore? settingsStore = null,
        ICaptureService? captureService = null,
        WindowsOverlayController? overlay = null,
        WindowsHotkeyService? hotkeys = null,
        IImageSink? imageSink = null,
        IImageSource? imageSource = null,
        IImageRenderer? renderer = null,
        IImageCodec? codec = null)
    {
        _shellThread = shellThread ?? new ShellThread("LightshotShellThread");
        _topology = topology ?? new DisplayTopology();
        _settingsStore = settingsStore ?? new JsonSettingsStore();
        _codec = codec ?? new SkiaImageCodec();
        _renderer = renderer ?? new DocumentRenderer();

        _clipboardSink = new ClipboardImageSink(_codec);
        _fileImageSink = new FileImageSink(_settingsStore, _codec);
        _imageSink = imageSink ?? _fileImageSink;
        _imageSource = imageSource ?? new FileImageSource(_codec, PromptOpenFile);

        _captureService = captureService ?? new WindowsCaptureService(_topology, _settingsStore);
        _overlay = overlay ?? new WindowsOverlayController(_shellThread);
        _hotkeys = hotkeys ?? new WindowsHotkeyService(_shellThread);
        _trayIcon = new TrayIcon("Lightshot");

        _coordinator = new AppCoordinator(
            _captureService,
            _overlay,
            _imageSource,
            _imageSink,
            _settingsStore,
            history: null,
            recordingService: null,
            mediaSink: null,
            gifEncoder: null,
            mediaMetadata: null,
            scratchDirectory: null,
            renderer: _renderer,
            codec: _codec,
            sleep: null,
            clock: null,
            ui: this);
    }

    public void Initialize()
    {
        var displays = DisplayTopology.GetDisplays();
        _trayIcon.Displays = displays.Select(d => new DisplayMenuItem(d.DisplayId, d.DeviceName + (d.IsPrimary ? " (Primary)" : ""))).ToList();

        _trayIcon.OnCaptureAction = action => TriggerCaptureAction(action);
        _trayIcon.OnFullscreenDisplayCapture = displayId => _ = _coordinator.CaptureFullscreenAsync(displayId);
        _trayIcon.OnOpenFile = () => _coordinator.OpenFile();
        _trayIcon.OnSettings = () => { /* Settings dialog in Phase 5 */ };
        _trayIcon.OnQuit = () =>
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Application.Current.Shutdown(0);
            }));
        };

        RegisterHotkeys();
    }

    public void RegisterHotkeys()
    {
        var bindings = _settingsStore.Hotkeys ?? HotkeyBindings.Defaults;
        var assignments = new Dictionary<CaptureAction, HotkeyBinding>(bindings.Assignments);

        // Spec step 1: Defaults PrintScreen = Area, Ctrl+PrintScreen = Fullscreen
        if (!assignments.ContainsKey(CaptureAction.Area))
        {
            assignments[CaptureAction.Area] = new HotkeyBinding(0x2C, HotkeyModifiers.None, "PrintScreen");
        }
        if (!assignments.ContainsKey(CaptureAction.Fullscreen))
        {
            assignments[CaptureAction.Fullscreen] = new HotkeyBinding(0x2C, HotkeyModifiers.Control, "Ctrl+PrintScreen");
        }

        var activeBindings = new HotkeyBindings(assignments);
        _hotkeys.Register(activeBindings, action => TriggerCaptureAction(action));
        _trayIcon.Bindings = activeBindings;
    }

    public void TriggerCaptureAction(CaptureAction action)
    {
        switch (action)
        {
            case CaptureAction.Area:
                _ = _coordinator.CaptureAreaAsync();
                break;
            case CaptureAction.Fullscreen:
                _ = _coordinator.CaptureFullscreenAsync();
                break;
            case CaptureAction.Window:
                _ = _coordinator.CaptureWindowAsync();
                break;
            case CaptureAction.RepeatLast:
                _ = _coordinator.RepeatLastCaptureAsync();
                break;
        }
    }

    public void TriggerAreaCapture() => TriggerCaptureAction(CaptureAction.Area);

    private static string? PromptOpenFile()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
            Title = "Open Image"
        };
        return ofd.ShowDialog() == true ? ofd.FileName : null;
    }

    public void OpenEditor(CapturedImage image)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => OpenEditor(image)));
            return;
        }

        var doc = new AnnotationDocument(image);
        var vm = new EditorViewModel(doc, _imageSink, _renderer, _imageSource, _settingsStore);
        var window = new EditorWindow(vm);

        window.Closed += (s, e) =>
        {
            if (_activeEditorWindow == window)
            {
                _activeEditorWindow = null;
            }
        };

        _activeEditorWindow = window;
        window.Show();

        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        WindowPresenter.Present(hwnd);
    }

    public void PresentQuickAccess(CapturedImage image)
    {
        // Spec step 12: Route PresentQuickAccess to the editor until Phase 5 provides Quick Access cards.
        OpenEditor(image);
    }

    public void PresentCaptureFailure(CaptureError error)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => PresentCaptureFailure(error)));
            return;
        }

        string details = error switch
        {
            CaptureError.SystemFailure sf => sf.Message,
            _ => error.ToString()
        };

        ErrorDialog.ShowNotice(
            _activeEditorWindow,
            "Capture Failed",
            "Lightshot was unable to complete the screen capture.",
            details);
    }

    public void PresentImageLoadFailure(ImageLoadError error)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => PresentImageLoadFailure(error)));
            return;
        }

        ErrorDialog.ShowNotice(
            _activeEditorWindow,
            "Failed to Open Image",
            "The selected file could not be opened or decoded.",
            error.ToString());
    }

    public void PresentPermissionDenied(PermissionKind kind)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => PresentPermissionDenied(kind)));
            return;
        }

        ErrorDialog.ShowNotice(
            _activeEditorWindow,
            "Permission Required",
            $"Lightshot requires {kind} permission to capture your screen.",
            "Please check Windows system permissions.");
    }

    public void PresentRecordingState(RecordingSession session) { }
    public Task<bool> RunRecordingCountdownAsync(int seconds) => Task.FromResult(true);
    public Task<bool> ConfirmRecordingRestartAsync() => Task.FromResult(true);
    public Task<bool> ConfirmRecordingDiscardAsync() => Task.FromResult(true);
    public Task<bool> ResolveMicrophoneDisconnectedAsync() => Task.FromResult(true);
    public void PresentRecordingFinished(string path) { }
    public void PresentPostRecordingOverlay(PendingRecording recording) { }
    public void OpenVideoEditor(string path, string? inputPath = null) { }
    public void PresentRecordingPreparation(Action cancel) { }
    public void UpdateRecordingPreparation(double progress) { }
    public void DismissRecordingPreparation() { }
    public void PresentGifConversion(Action cancel) { }
    public void UpdateGifConversion(double progress) { }
    public void DismissGifConversion() { }
    public Task<bool> ResolveCancelledGifConversionAsync() => Task.FromResult(true);
    public void PresentRecordingFailure(RecordingError error) { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _trayIcon.Dispose();
        _hotkeys.Dispose();
        _overlay.Dispose();
        _shellThread.Dispose();
    }
}
