// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Lightshot.App.Views.Notices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Settings;
using Microsoft.Win32;

namespace Lightshot.App.Views.Editor;

public partial class EditorWindow : Window
{
    private EditorViewModel? _viewModel;
    private SelectionAdorner? _selectionAdorner;
    private CropOverlay? _cropOverlay;
    private System.Windows.Point? _dragStartScreen;
    private bool _isDraggingOut;
    private CanvasPanelViewModel? _canvasViewModel;
    private EditorCanvasPresenter? _canvasPresenter;

    public ICommand CopyAndCloseCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand QuickSaveCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SendBackwardCommand { get; }
    public ICommand BringForwardCommand { get; }
    public Action<CapturedImage>? OnPin { get; set; }

    public EditorViewModel? ViewModel => _viewModel;
    public CanvasPanelViewModel? CanvasViewModel => _canvasViewModel;
    public EditorCanvasPresenter? CanvasPresenter => _canvasPresenter;

    public EditorWindow()
    {
        CopyAndCloseCommand = new RelayCommand(_ => _viewModel?.CopyAndClose());
        CopyCommand = new RelayCommand(_ => _viewModel?.Copy());
        SaveAsCommand = new RelayCommand(_ => PromptSaveAs());
        QuickSaveCommand = new RelayCommand(_ => _viewModel?.QuickSave(), _ => _viewModel?.CanQuickSave == true);
        UndoCommand = new RelayCommand(_ => _viewModel?.Undo());
        RedoCommand = new RelayCommand(_ => _viewModel?.Redo());
        DeleteCommand = new RelayCommand(_ => _viewModel?.DeleteSelection());
        SendBackwardCommand = new RelayCommand(_ => _viewModel?.SendBackward());
        BringForwardCommand = new RelayCommand(_ => _viewModel?.BringForward());

        InitializeComponent();

        ApplyWorkAreaCap();
        Loaded += OnLoaded;
    }

    public EditorWindow(EditorViewModel viewModel) : this()
    {
        InitializeViewModel(viewModel);
    }

    public void InitializeViewModel(EditorViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        if (QuickSaveCommand is RelayCommand relay)
        {
            relay.RaiseCanExecuteChanged();
        }

        DataContext = _viewModel;
        _viewModel.RequestClose += OnRequestClose;
        _viewModel.ShowError += OnShowError;

        ToolPaletteControl.BindViewModel(_viewModel);
        StyleBarControl.BindViewModel(_viewModel);

        // The canvas must re-render (and take its new size) before the window resizes the
        // container to it; handlers run in subscription order, so subscribe after the canvas.
        // Otherwise a crop, reset or undo leaves the container at the old size and the
        // centred canvas spills past it.
        MainCanvasHost.ViewModel = _viewModel;
        _viewModel.CanvasInvalidated += OnCanvasInvalidated;
        FloatingTextEditor.Attach(MainCanvasHost, _viewModel);

        _canvasViewModel = new CanvasPanelViewModel(_viewModel);
        CanvasOptionsPanel.ViewModel = _canvasViewModel;
        CanvasToggleButton.IsChecked = false;

        _canvasPresenter = new EditorCanvasPresenter(
            _viewModel,
            _canvasViewModel,
            CanvasStage,
            CanvasBackdrop,
            CanvasContainer,
            MainCanvasHost);

        _canvasViewModel.CanvasChanged += () =>
        {
            if (CanvasToggleButton.IsChecked != _canvasViewModel.IsEnabled)
            {
                CanvasToggleButton.IsChecked = _canvasViewModel.IsEnabled;
            }
        };

        UpdateContainerSize();
        _canvasPresenter.Apply();
    }

    private void ApplyWorkAreaCap()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            var (w, h) = EditorViewModel.CalculateWindowSize(workArea.Width, workArea.Height);
            Width = w;
            Height = h;
        }
        catch
        {
            Width = EditorViewModel.DefaultWindowWidth;
            Height = EditorViewModel.DefaultWindowHeight;
        }
    }

    /// <summary>
    /// Sizes and centres the editor inside the work area of the display under a physical point: the
    /// capture's monitor, since the pointer is there when a capture or its card opens the editor.
    /// </summary>
    public void PlaceOnDisplayAt(Lightshot.Core.Point physicalPoint) =>
        WindowPlacement.PlaceOnDisplayAt(this, physicalPoint, EditorViewModel.CalculateWindowSize);

    public static Lightshot.Core.Rect CalculateFrame(Lightshot.Core.Rect workAreaPhysical, double windowScale) =>
        WindowPlacement.CalculateFrame(workAreaPhysical, windowScale, EditorViewModel.CalculateWindowSize);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var adornerLayer = AdornerLayer.GetAdornerLayer(MainCanvasHost);
        if (adornerLayer != null && _viewModel != null)
        {
            _selectionAdorner = new SelectionAdorner(MainCanvasHost, _viewModel);
            _cropOverlay = new CropOverlay(MainCanvasHost, _viewModel);

            adornerLayer.Add(_selectionAdorner);
            adornerLayer.Add(_cropOverlay);
        }

        UpdateContainerSize();
        _canvasPresenter?.Apply();
    }

    private void OnCanvasInvalidated()
    {
        UpdateContainerSize();
        _canvasPresenter?.Apply();
        var adornerLayer = AdornerLayer.GetAdornerLayer(MainCanvasHost);
        adornerLayer?.Update();
    }

    private void UpdateContainerSize()
    {
        if (MainCanvasHost.Width > 0 && MainCanvasHost.Height > 0)
        {
            CanvasContainer.Width = MainCanvasHost.Width;
            CanvasContainer.Height = MainCanvasHost.Height;
        }
    }

    private void OnRequestClose()
    {
        Close();
    }

    private void OnShowError((string Title, string Message, string? Details) info)
    {
        ErrorDialog.ShowNotice(this, info.Title, info.Message, info.Details);
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel == null) return;
        var screenPos = e.GetPosition(MainCanvasHost);
        _dragStartScreen = screenPos;
        _isDraggingOut = false;

        var imgPos = MainCanvasHost.ScreenToImage(screenPos);

        if (e.ClickCount == 2)
        {
            if (_viewModel.DoubleClick(imgPos))
            {
                return;
            }
        }

        MainCanvasHost.CaptureMouse();
        _viewModel.GestureStarted(imgPos);
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel == null) return;
        var screenPos = e.GetPosition(MainCanvasHost);

        // Check for drag-out gesture (Ctrl+drag or dragging outside with right/middle button or significant move with selection)
        if (e.LeftButton == MouseButtonState.Pressed && _dragStartScreen.HasValue && !_isDraggingOut)
        {
            var diff = screenPos - _dragStartScreen.Value;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (Math.Abs(diff.X) > 8 || Math.Abs(diff.Y) > 8))
            {
                InitiateDragOut();
                return;
            }
        }

        if (MainCanvasHost.IsMouseCaptured)
        {
            var imgPos = MainCanvasHost.ScreenToImage(screenPos);
            _viewModel.GestureMoved(imgPos);
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel == null) return;
        _dragStartScreen = null;
        _isDraggingOut = false;

        if (MainCanvasHost.IsMouseCaptured)
        {
            var screenPos = e.GetPosition(MainCanvasHost);
            var imgPos = MainCanvasHost.ScreenToImage(screenPos);
            _viewModel.GestureEnded(imgPos);
            MainCanvasHost.ReleaseMouseCapture();
        }
    }

    private void InitiateDragOut()
    {
        if (_viewModel == null) return;
        _isDraggingOut = true;
        MainCanvasHost.ReleaseMouseCapture();

        try
        {
            var renderer = new Lightshot.Rendering.DocumentRenderer();
            var rendered = renderer.Render(_viewModel.Document);

            string tempFile = Path.Combine(Path.GetTempPath(), $"Lightshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(tempFile, rendered.Data.ToArray());

            var dataObj = new DataObject();
            dataObj.SetData(DataFormats.FileDrop, new string[] { tempFile });

            using var ms = new MemoryStream(rendered.Data.ToArray());
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            dataObj.SetData(DataFormats.Bitmap, bmp);

            DragDrop.DoDragDrop(MainCanvasHost, dataObj, DragDropEffects.Copy);
        }
        catch
        {
            // Ignore drag-drop cancellation
        }
    }

    private void PromptSaveAs()
    {
        if (_viewModel == null) return;

        var now = DateTime.Now;
        var baseName = _viewModel.SuggestedFileName(now);
        bool isJpeg = _viewModel.DefaultExportFormat is ImageFormat.Jpeg;
        string ext = isJpeg ? ".jpg" : ".png";

        string fileName = (baseName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                           baseName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           baseName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            ? Path.ChangeExtension(baseName, ext)
            : $"{baseName}{ext}";

        var sfd = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            FilterIndex = isJpeg ? 2 : 1,
            DefaultExt = ext,
            FileName = fileName
        };

        if (sfd.ShowDialog(this) == true)
        {
            double jpegQuality = _viewModel.DefaultExportFormat is ImageFormat.Jpeg jpeg
                ? jpeg.Quality
                : SettingsKeys.DefaultJpegQuality;

            ImageFormat fmt = sfd.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                              sfd.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? new ImageFormat.Jpeg(jpegQuality)
                : new ImageFormat.Png();

            _viewModel.SaveAs(sfd.FileName, fmt);
        }
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        PromptSaveAs();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.Copy();
    }

    private void OnCanvasToggleClick(object sender, RoutedEventArgs e)
    {
        if (_canvasViewModel != null)
        {
            _canvasViewModel.IsEnabled = CanvasToggleButton.IsChecked == true;
        }
    }

    private void OnCanvasOptionsClick(object sender, RoutedEventArgs e)
    {
        CanvasOptionsPopup.IsOpen = !CanvasOptionsPopup.IsOpen;
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var renderer = new Lightshot.Rendering.DocumentRenderer();
        var rendered = renderer.Render(_viewModel.Document);
        var image = new CapturedImage(rendered.PixelWidth, rendered.PixelHeight, rendered.Data);
        OnPin?.Invoke(image);
        Close();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.CopyAndClose();
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
