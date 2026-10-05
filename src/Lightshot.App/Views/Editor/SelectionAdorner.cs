// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Lightshot.Core;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Views.Editor;

/// <summary>
/// Adorner drawn above the canvas bitmap to show selection borders and resize/reshape handles.
/// These visuals exist purely in WPF and are never exported to rendered bitmaps.
/// </summary>
public sealed class SelectionAdorner : Adorner
{
    private readonly CanvasHost _canvasHost;
    private readonly EditorViewModel _viewModel;

    private static readonly SolidColorBrush s_selectionBorderBrush = new(System.Windows.Media.Color.FromArgb(220, 0, 122, 255));
    private static readonly SolidColorBrush s_handleFillBrush = Brushes.White;
    private static readonly Pen s_selectionBorderPen = new(s_selectionBorderBrush, 1.5) { DashStyle = DashStyles.Dash };
    private static readonly Pen s_handleBorderPen = new(s_selectionBorderBrush, 1.5);

    public const double HandleVisualRadius = 4.5;

    static SelectionAdorner()
    {
        s_selectionBorderBrush.Freeze();
        s_handleBorderPen.Freeze();
        s_selectionBorderPen.Freeze();
    }

    public SelectionAdorner(CanvasHost canvasHost, EditorViewModel viewModel)
        : base(canvasHost)
    {
        _canvasHost = canvasHost ?? throw new ArgumentNullException(nameof(canvasHost));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        IsHitTestVisible = false; // Mouse events are captured by canvas host or overlaid controls
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_viewModel.ActiveTool == EditorTool.Crop) return;
        if (!_viewModel.HasSelection || !_viewModel.SelectedID.HasValue) return;

        var selected = _viewModel.Document.Element(_viewModel.SelectedID.Value);
        if (selected == null) return;

        var kind = selected.ElementKind;

        // Line / Arrow endpoint handles
        if (kind.EndpointHandles != null)
        {
            foreach (var (handle, pt) in kind.EndpointHandles)
            {
                var screenPt = _canvasHost.ImageToScreen(pt);
                dc.DrawEllipse(s_handleFillBrush, s_handleBorderPen, screenPt, HandleVisualRadius, HandleVisualRadius);
            }
            return;
        }

        // Text element handles (Left, Right, BottomRight)
        if (kind is AnnotationElement.Kind.Text t)
        {
            var box = t.Box.Standardized;
            var topLeft = _canvasHost.ImageToScreen(new Point(box.MinX, box.MinY));
            var bottomRight = _canvasHost.ImageToScreen(new Point(box.MaxX, box.MaxY));
            var screenRect = new System.Windows.Rect(topLeft, bottomRight);

            dc.DrawRectangle(null, s_selectionBorderPen, screenRect);

            foreach (var handle in AnnotationDocument.TextHandles)
            {
                var imgPt = GeometryUtils.HandlePoint(handle, box);
                var screenPt = _canvasHost.ImageToScreen(imgPt);
                dc.DrawEllipse(s_handleFillBrush, s_handleBorderPen, screenPt, HandleVisualRadius, HandleVisualRadius);
            }
            return;
        }

        // Box elements (Rectangle, Ellipse, Redact, Highlight, Focus, StepMarker)
        var bounds = kind.BoundingBox;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            var tl = _canvasHost.ImageToScreen(new Point(bounds.MinX, bounds.MinY));
            var br = _canvasHost.ImageToScreen(new Point(bounds.MaxX, bounds.MaxY));
            var screenRect = new System.Windows.Rect(tl, br);

            dc.DrawRectangle(null, s_selectionBorderPen, screenRect);

            foreach (Handle handle in Enum.GetValues(typeof(Handle)))
            {
                var imgPt = GeometryUtils.HandlePoint(handle, bounds);
                var screenPt = _canvasHost.ImageToScreen(imgPt);
                dc.DrawEllipse(s_handleFillBrush, s_handleBorderPen, screenPt, HandleVisualRadius, HandleVisualRadius);
            }
        }
    }
}
