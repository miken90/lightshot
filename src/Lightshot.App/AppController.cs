// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Diagnostics;
using Lightshot.App.Theming;
using Lightshot.App.Views;
using Lightshot.App.Views.Editor;
using Lightshot.App.Views.History;
using Lightshot.App.Views.Notices;
using Lightshot.App.Views.Onboarding;
using Lightshot.App.Views.Pin;
using Lightshot.App.Views.QuickAccess;
using Lightshot.App.Views.Recording;
using Lightshot.App.Views.Settings;
using Lightshot.Core;
using Lightshot.Platform.Windows.Audio;
using Lightshot.Platform.Windows.Capture;
using Lightshot.Platform.Windows.Clipboard;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Files;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.Platform.Windows.Media;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.Platform.Windows.Recording;
using Lightshot.Platform.Windows.Settings;
using Lightshot.Platform.Windows.Shell;
using Lightshot.Platform.Windows.Tray;
using Lightshot.Platform.Windows.Windows;
using Lightshot.Rendering;

namespace Lightshot.App;

/// <summary>
/// Main application controller composing Core AppCoordinator with Windows platform implementations:
/// capture service, overlay host, hotkeys, tray, window presenter, sinks, settings store, editor,
/// Quick Access cards, PinBoard, History, and Settings.
/// </summary>
public sealed partial class AppController : ICaptureUI, IDisposable
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
    private readonly HistoryStore _historyStore;
    private readonly PinBoard _pinBoard;
    private readonly QuickAccessHost _quickAccessHost;
    private readonly ScratchStore _scratchStore;
    private readonly AudioDeviceService _audioDevices;
    private readonly WindowsRecordingService _recordingService;
    private readonly SystemMediaSink _mediaSink;
    private readonly RecordingSelectionOverlay _recordingOverlay;
    private readonly AppCoordinator _coordinator;

    private EditorWindow? _activeEditorWindow;
    private HistoryWindow? _historyWindow;
    private SettingsWindow? _settingsWindow;
    private OnboardingWindow? _onboardingWindow;
    private bool _disposed;

    public AppCoordinator Coordinator => _coordinator;
    public TrayIcon Tray => _trayIcon;
    public ISettingsStore Settings => _settingsStore;
    public HistoryStore History => _historyStore;
    public PinBoard PinBoard => _pinBoard;
    public QuickAccessHost QuickAccessHost => _quickAccessHost;

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
        IImageCodec? codec = null,
        HistoryStore? historyStore = null,
        PinBoard? pinBoard = null,
        QuickAccessHost? quickAccessHost = null)
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

        _historyStore = historyStore ?? new HistoryStore(
            AppPaths.History,
            _settingsStore.HistoryRetention,
            new SkiaThumbnailer(),
            codec: _codec);

        _pinBoard = pinBoard ?? new PinBoard();

        _quickAccessHost = quickAccessHost ?? new QuickAccessHost(
            actions: new QuickAccessActions(
                Copy: image => CopyImage(image),
                Save: image => SaveImage(image),
                SaveAs: image => SaveAsImage(image),
                Annotate: image => OpenEditor(image),
                Pin: image => PinImage(image)),
            settings: () => _settingsStore.QuickAccess,
            codec: _codec);

        // One scratch root for engine takes, coordinator outputs and recovery, so a crash leaves files recovery can see.
        _scratchStore = new ScratchStore(AppPaths.ScratchRecordings);
        _audioDevices = new AudioDeviceService();
        var engine = new RecordingEngine(scratchStore: _scratchStore, audioDeviceService: _audioDevices,
            playSounds: () => _settingsStore.RecordingDefaults.PlaySounds);
        _recordingService = new WindowsRecordingService(engine, _audioDevices);
        _mediaSink = new SystemMediaSink();
        _recordingOverlay = new RecordingSelectionOverlay(_overlay, _audioDevices, _settingsStore);

        _coordinator = new AppCoordinator(
            _captureService,
            _recordingOverlay,
            _imageSource,
            _imageSink,
            _settingsStore,
            history: _historyStore,
            recordingService: _recordingService,
            mediaSink: _mediaSink,
            gifEncoder: new MfGifEncoder(),
            mediaMetadata: new MfMediaMetadata(),
            scratchDirectory: AppPaths.LocalData,
            renderer: _renderer,
            codec: _codec,
            sleep: null,
            clock: null,
            ui: this);

        Debug.Assert(string.Equals(Path.GetFullPath(_coordinator.RecordingScratchDirectory), Path.GetFullPath(_scratchStore.RootDirectory), StringComparison.OrdinalIgnoreCase));
    }

    public void Initialize()
    {
        // Apply initial appearance theme and update tray icon
        ThemeService.Instance.Apply(_settingsStore.Appearance);
        _trayIcon.UpdateTheme(ThemeService.GetSystemUsesLightTheme());

        // Subscribe to setting change notifications
        if (_settingsStore is JsonSettingsStore jsonStore)
        {
            jsonStore.SettingChanged += OnSettingChanged;
        }

        var displays = DisplayTopology.GetDisplays();
        _trayIcon.Displays = displays.Select(d => new DisplayMenuItem(d.DisplayId, d.DeviceName + (d.IsPrimary ? " (Primary)" : ""))).ToList();

        _trayIcon.OnCaptureAction = action => TriggerCaptureAction(action);
        _trayIcon.OnFullscreenDisplayCapture = displayId => _ = _coordinator.CaptureFullscreenAsync(displayId);
        _trayIcon.OnOpenFile = () => _coordinator.OpenFile();
        _trayIcon.OnHistory = () => ShowHistoryWindow();
        _trayIcon.OnSettings = () => ShowSettingsWindow();
        _trayIcon.OnQuit = () =>
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Application.Current.Shutdown(0);
            }));
        };

        RegisterHotkeys();
        InitializeRecording();
        InitializeUpdates();
    }

    private void OnSettingChanged(object? sender, string key)
    {
        switch (key)
        {
            case SettingsKeys.AppAppearance:
                ThemeService.Instance.Apply(_settingsStore.Appearance);
                _trayIcon.UpdateTheme(ThemeService.GetSystemUsesLightTheme());
                break;
            case SettingsKeys.CaptureHotkeys:
                RegisterHotkeys();
                break;
            case SettingsKeys.HistoryRetention:
                _historyStore.SetRetention(_settingsStore.HistoryRetention);
                break;
        }
    }

    public IReadOnlyList<CaptureAction> RegisterHotkeys(HotkeyBindings? customBindings = null)
    {
        var bindings = customBindings ?? _settingsStore.Hotkeys ?? HotkeyBindings.Defaults;
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
        var unregisterable = _hotkeys.Register(activeBindings, action => TriggerCaptureAction(action));
        _trayIcon.Bindings = activeBindings;
        return unregisterable;
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
            case CaptureAction.RecordScreen:
                _ = _coordinator.ToggleRecordingAsync();
                break;
            case CaptureAction.PauseResumeRecording:
                _ = _coordinator.PauseResumeRecordingAsync();
                break;
            case CaptureAction.RestartRecording:
                _ = _coordinator.RestartRecordingAsync();
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

    public void CopyImage(CapturedImage image)
    {
        var doc = new AnnotationDocument(image);
        var rendered = _renderer.Render(doc);
        _clipboardSink.CopyToClipboard(rendered);
    }

    public bool SaveImage(CapturedImage image)
    {
        var doc = new AnnotationDocument(image);
        var rendered = _renderer.Render(doc);
        var path = _settingsStore.DefaultDestination();
        _imageSink.Write(rendered, path, _settingsStore.DefaultFormat);
        return true;
    }

    public bool SaveAsImage(CapturedImage image)
    {
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap Image (*.bmp)|*.bmp",
            DefaultExt = ".png",
            FileName = new FilenameFormatter(_settingsStore.FilenamePattern).Filename(DateTime.Now) + ".png"
        };
        if (sfd.ShowDialog() == true)
        {
            var doc = new AnnotationDocument(image);
            var rendered = _renderer.Render(doc);
            string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
            double quality = _settingsStore.DefaultFormat is ImageFormat.Jpeg jpeg ? jpeg.Quality : 0.9;
            ImageFormat format = ext switch
            {
                ".jpg" or ".jpeg" => new ImageFormat.Jpeg(quality),
                _ => new ImageFormat.Png()
            };
            _imageSink.Write(rendered, sfd.FileName, format);
            return true;
        }
        return false;
    }

    public PinWindow PinImage(CapturedImage image)
    {
        return _pinBoard.Pin(
            image,
            copyAction: () => CopyImage(image),
            saveAction: () => SaveImage(image),
            saveAsAction: () => SaveAsImage(image));
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

        window.OnPin = img =>
        {
            PinImage(img);
        };

        window.Closed += (s, e) =>
        {
            if (_activeEditorWindow == window)
            {
                _activeEditorWindow = null;
            }
        };

        _activeEditorWindow = window;
        window.PlaceOnDisplayAt(DisplayTopology.GetCursorPosition());
        window.Show();

        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        WindowPresenter.Present(hwnd);
    }

    public void PresentQuickAccess(CapturedImage image)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => PresentQuickAccess(image)));
            return;
        }

        _quickAccessHost.Present(image);
    }

    public void ShowHistoryWindow()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(ShowHistoryWindow));
            return;
        }

        if (_historyWindow == null || !_historyWindow.IsLoaded)
        {
            var vm = new HistoryViewModel(
                _historyStore,
                onReopen: image => OpenEditor(image),
                onCopy: image => CopyImage(image),
                settingsStore: _settingsStore);
            _historyWindow = new HistoryWindow(vm);
            _historyWindow.Closed += (s, e) => _historyWindow = null;
            // Open where the user is working, not on the primary monitor.
            WindowPlacement.PlaceOnDisplayAt(_historyWindow, DisplayTopology.GetCursorPosition(), WindowPlacement.CappedSize(_historyWindow.Width, _historyWindow.Height));
        }

        _historyWindow.Show();
        _historyWindow.Activate();
    }

    public void ShowSettingsWindow()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(ShowSettingsWindow));
            return;
        }

        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            var vm = new SettingsViewModel(
                _settingsStore,
                applyHotkeys: bindings => RegisterHotkeys(bindings),
                applyRetention: ret => _historyStore.SetRetention(ret),
                applyAppearance: pref =>
                {
                    ThemeService.Instance.Apply(pref);
                    _trayIcon.UpdateTheme(ThemeService.GetSystemUsesLightTheme());
                },
                updates: CreateUpdateSettingsViewModel());
            _settingsWindow = new SettingsWindow(vm);
            _settingsWindow.Closed += (s, e) => { _settingsWindow = null; _openUpdateSettings = null; };
            // Open where the user is working, not on the primary monitor.
            WindowPlacement.PlaceOnDisplayAt(_settingsWindow, DisplayTopology.GetCursorPosition(), WindowPlacement.CappedSize(_settingsWindow.Width, _settingsWindow.Height));
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowOnboarding()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(ShowOnboarding));
            return;
        }

        if (_onboardingWindow == null || !_onboardingWindow.IsLoaded)
        {
            var vm = new OnboardingViewModel(_settingsStore);
            _onboardingWindow = new OnboardingWindow(vm);
            _onboardingWindow.Closed += (s, e) => _onboardingWindow = null;
            // Open where the user is working, not on the primary monitor.
            var cursor = DisplayTopology.GetCursorPosition();
            WindowPlacement.PlaceOnDisplayAt(_onboardingWindow, cursor, WindowPlacement.CappedSize(_onboardingWindow.Width, double.PositiveInfinity));
            _onboardingWindow.SizeToContent = SizeToContent.Height;
            _onboardingWindow.ContentRendered += (_, _) =>
            {
                try
                {
                    var display = DisplayMath.FindDisplayAt(DisplayTopology.GetDisplays(), cursor);
                    if (display != null && _onboardingWindow != null && _onboardingWindow.ActualHeight > 0)
                    {
                        var scale = WindowPlacement.MoveOntoDisplay(_onboardingWindow, display.WorkArea);
                        var workArea = DisplayMath.PhysicalToDip(display.WorkArea, scale);
                        _onboardingWindow.Top = workArea.MinY + (workArea.Height - _onboardingWindow.ActualHeight) / 2;
                    }
                }
                catch { }
            };
        }

        _onboardingWindow.Show();
        _onboardingWindow.Activate();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            try
            {
                dispatcher.Invoke(DisposeCore);
                return;
            }
            catch
            {
                // Dispatcher may have aborted or shut down
            }
        }

        DisposeCore();
    }

    private void DisposeCore()
    {
        if (_settingsStore is JsonSettingsStore jsonStore)
        {
            jsonStore.SettingChanged -= OnSettingChanged;
        }

        _autoClearTimer?.Stop();

        try { _historyWindow?.Close(); } catch { }
        try { _settingsWindow?.Close(); } catch { }
        try { _onboardingWindow?.Close(); } catch { }
        try { _activeEditorWindow?.Close(); } catch { }

        _quickAccessHost.Dispose();
        _pinBoard.Dispose();
        DisposeUpdates();
        _trayIcon.Dispose();
        _hotkeys.Dispose();
        DisposeRecording();
        _overlay.Dispose();
        _shellThread.Dispose();
    }
}
