// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Lightshot.Core;
using Point = Lightshot.Core.Point;
using Rect = Lightshot.Core.Rect;

namespace Lightshot.App.Views.Editor;

/// <summary>
/// Adorner drawn above the canvas when the Crop tool is active.
/// Renders a dimmed outside region, rule-of-thirds grid inside the crop frame,
/// and border/corner handles.
/// </summary>
public sealed class CropOverlay : Adorner
{
    private readonly CanvasHost _canvasHost;
    private readonly EditorViewModel _viewModel;

    private static readonly Brush s_dimBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(140, 0, 0, 0));
    private static readonly Pen s_borderPen = new(Brushes.White, 2.0);
    private static readonly Pen s_gridPen = new(new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 255, 255, 255)), 1.0);
    private static readonly Brush s_handleBrush = Brushes.White;

    public const double CornerLength = 16.0;
    public const double EdgeHandleWidth = 16.0;
    public const double HandleThickness = 3.0;

    static CropOverlay()
    {
        s_dimBrush.Freeze();
        s_borderPen.Freeze();
        s_gridPen.Freeze();
        s_handleBrush.Freeze();
    }

    public CropOverlay(CanvasHost canvasHost, EditorViewModel viewModel)
        : base(canvasHost)
    {
        _canvasHost = canvasHost ?? throw new ArgumentNullException(nameof(canvasHost));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (!_viewModel.IsCropping) return;

        var cropFrame = _viewModel.CropFrame;
        if (!cropFrame.HasValue) return;

        var crop = cropFrame.Value.Standardized;
        var tl = _canvasHost.ImageToScreen(new Point(crop.MinX, crop.MinY));
        var br = _canvasHost.ImageToScreen(new Point(crop.MaxX, crop.MaxY));

        double left = Math.Min(tl.X, br.X);
        double top = Math.Min(tl.Y, br.Y);
        double right = Math.Max(tl.X, br.X);
        double bottom = Math.Max(tl.Y, br.Y);
        double width = right - left;
        double height = bottom - top;

        if (width <= 0 || height <= 0) return;

        double hostWidth = _canvasHost.ActualWidth;
        double hostHeight = _canvasHost.ActualHeight;

        // 1. Dim outside region using 4 rectangles
        dc.DrawRectangle(s_dimBrush, null, new System.Windows.Rect(0, 0, hostWidth, top)); // Top
        dc.DrawRectangle(s_dimBrush, null, new System.Windows.Rect(0, bottom, hostWidth, Math.Max(0, hostHeight - bottom))); // Bottom
        dc.DrawRectangle(s_dimBrush, null, new System.Windows.Rect(0, top, left, height)); // Left
        dc.DrawRectangle(s_dimBrush, null, new System.Windows.Rect(right, top, Math.Max(0, hostWidth - right), height)); // Right

        // 2. Crop border
        var cropScreenRect = new System.Windows.Rect(left, top, width, height);
        dc.DrawRectangle(null, s_borderPen, cropScreenRect);

        // 3. Rule of thirds grid
        double oneThirdW = width / 3.0;
        double twoThirdW = 2.0 * width / 3.0;
        double oneThirdH = height / 3.0;
        double twoThirdH = 2.0 * height / 3.0;

        dc.DrawLine(s_gridPen, new System.Windows.Point(left + oneThirdW, top), new System.Windows.Point(left + oneThirdW, bottom));
        dc.DrawLine(s_gridPen, new System.Windows.Point(left + twoThirdW, top), new System.Windows.Point(left + twoThirdW, bottom));
        dc.DrawLine(s_gridPen, new System.Windows.Point(left, top + oneThirdH), new System.Windows.Point(right, top + oneThirdH));
        dc.DrawLine(s_gridPen, new System.Windows.Point(left, top + twoThirdH), new System.Windows.Point(right, top + twoThirdH));

        // 4. Crop handles (corners and edges)
        // TopLeft
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(left, top, CornerLength, HandleThickness));
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(left, top, HandleThickness, CornerLength));

        // TopRight
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(right - CornerLength, top, CornerLength, HandleThickness));
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(right - HandleThickness, top, HandleThickness, CornerLength));

        // BottomLeft
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(left, bottom - HandleThickness, CornerLength, HandleThickness));
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(left, bottom - CornerLength, HandleThickness, CornerLength));

        // BottomRight
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(right - CornerLength, bottom - HandleThickness, CornerLength, HandleThickness));
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(right - HandleThickness, bottom - CornerLength, HandleThickness, CornerLength));

        // Mid-Edges
        double midX = left + width / 2.0;
        double midY = top + height / 2.0;

        // Top mid
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(midX - EdgeHandleWidth / 2.0, top, EdgeHandleWidth, HandleThickness));
        // Bottom mid
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(midX - EdgeHandleWidth / 2.0, bottom - HandleThickness, EdgeHandleWidth, HandleThickness));
        // Left mid
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(left, midY - EdgeHandleWidth / 2.0, HandleThickness, EdgeHandleWidth));
        // Right mid
        dc.DrawRectangle(s_handleBrush, null, new System.Windows.Rect(right - HandleThickness, midY - EdgeHandleWidth / 2.0, HandleThickness, EdgeHandleWidth));
    }
}
