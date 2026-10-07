// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lightshot.Core;
using Lightshot.Rendering;
using SkiaSharp;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Views.Editor;

/// <summary>
/// Connects editor events to WPF layout, transforms, clipping, and backdrop rendering.
/// Keeps crop tight while restoring the canvas frame when exiting crop.
/// </summary>
public sealed class EditorCanvasPresenter : IDisposable
{
    private readonly EditorViewModel _viewModel;
    private readonly CanvasPanelViewModel _canvasViewModel;
    private readonly FrameworkElement? _canvasStage;
    private readonly Image? _canvasBackdrop;
    private readonly FrameworkElement? _canvasContainer;
    private readonly CanvasHost? _canvasHost;

    public bool IsTightView { get; private set; } = true;
    public CanvasFrame CurrentFrame { get; private set; }

    public EditorCanvasPresenter(
        EditorViewModel viewModel,
        CanvasPanelViewModel canvasViewModel,
        FrameworkElement? canvasStage = null,
        Image? canvasBackdrop = null,
        FrameworkElement? canvasContainer = null,
        CanvasHost? canvasHost = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _canvasViewModel = canvasViewModel ?? throw new ArgumentNullException(nameof(canvasViewModel));
        _canvasStage = canvasStage;
        _canvasBackdrop = canvasBackdrop;
        _canvasContainer = canvasContainer;
        _canvasHost = canvasHost;

        _canvasViewModel.CanvasChanged += OnCanvasChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CanvasInvalidated += OnViewModelCanvasInvalidated;

        Apply();
    }

    private void OnCanvasChanged()
    {
        Apply();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.ActiveTool) ||
            e.PropertyName == nameof(EditorViewModel.IsCropping))
        {
            Apply();
        }
    }

    private void OnViewModelCanvasInvalidated()
    {
        if (_canvasViewModel.IsEnabled && _canvasViewModel.FillKind == CanvasFillKind.AutoEdge)
        {
            Apply();
        }
    }

    public void Apply()
    {
        if (_canvasStage?.Dispatcher != null && !_canvasStage.Dispatcher.CheckAccess())
        {
            _canvasStage.Dispatcher.BeginInvoke(new Action(Apply));
            return;
        }

        PerformApply();
    }

    private void PerformApply()
    {
        var doc = _viewModel.Document;
        if (doc == null) return;

        var visibleFrame = doc.VisibleFrame;
        int visibleW = Math.Max(1, (int)Math.Round(visibleFrame.Width));
        int visibleH = Math.Max(1, (int)Math.Round(visibleFrame.Height));

        bool isCropActive = _viewModel.IsCropping;
        bool isCanvasActive = _canvasViewModel.IsEnabled && !isCropActive;

        IsTightView = !isCanvasActive;

        if (!isCanvasActive)
        {
            CurrentFrame = new CanvasFrame(visibleW, visibleH, new Rect(0, 0, visibleW, visibleH), 1.0);

            if (_canvasBackdrop != null)
            {
                _canvasBackdrop.Visibility = Visibility.Collapsed;
                _canvasBackdrop.Source = null;
            }

            if (_canvasContainer != null)
            {
                _canvasContainer.Margin = new Thickness(0);
                _canvasContainer.LayoutTransform = System.Windows.Media.Transform.Identity;
                _canvasContainer.Clip = null;
            }

            if (_canvasStage != null)
            {
                _canvasStage.Width = visibleW;
                _canvasStage.Height = visibleH;
            }
            return;
        }

        var style = _canvasViewModel.Style with { Enabled = true };
        var frame = CanvasLayout.Compute(visibleW, visibleH, style);
        CurrentFrame = frame;

        // Render backdrop
        SKBitmap? edgeSource = _canvasHost?.BackingBitmap;
        if (edgeSource == null && style.EffectiveFill.Kind == CanvasFillKind.AutoEdge)
        {
            edgeSource = DocumentRenderer.Flatten(doc, _viewModel.DisplayElements);
        }

        using var backdropBmp = CanvasComposer.RenderBackdrop(frame, style, edgeSource);
        var surface = PixelSurfaceConverter.FromBitmap(backdropBmp);
        var bs = BitmapSource.Create(
            surface.Width,
            surface.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            surface.Pixels,
            surface.Stride);
        bs.Freeze();

        if (_canvasBackdrop != null)
        {
            _canvasBackdrop.Source = bs;
            _canvasBackdrop.Width = frame.Width;
            _canvasBackdrop.Height = frame.Height;
            _canvasBackdrop.Visibility = Visibility.Visible;
        }

        if (_canvasContainer != null)
        {
            _canvasContainer.Margin = new Thickness(frame.ImageRect.MinX, frame.ImageRect.MinY, 0, 0);

            if (Math.Abs(frame.Scale - 1.0) > 1e-4)
            {
                _canvasContainer.LayoutTransform = new ScaleTransform(frame.Scale, frame.Scale);
            }
            else
            {
                _canvasContainer.LayoutTransform = System.Windows.Media.Transform.Identity;
            }

            if (style.CornerRadius > 0)
            {
                double radiusLocal = style.CornerRadius / frame.Scale;
                _canvasContainer.Clip = new RectangleGeometry(
                    new System.Windows.Rect(0, 0, visibleW, visibleH),
                    radiusLocal,
                    radiusLocal);
            }
            else
            {
                _canvasContainer.Clip = null;
            }
        }

        if (_canvasStage != null)
        {
            _canvasStage.Width = frame.Width;
            _canvasStage.Height = frame.Height;
        }
    }

    public void Dispose()
    {
        _canvasViewModel.CanvasChanged -= OnCanvasChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CanvasInvalidated -= OnViewModelCanvasInvalidated;
    }
}
