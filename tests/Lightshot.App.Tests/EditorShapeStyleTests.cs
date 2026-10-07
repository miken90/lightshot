// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using SkiaSharp;
using Xunit;

namespace Lightshot.App.Tests;

[Trait("Tier", "Unit")]
public class EditorShapeStyleTests
{
    private static CapturedImage CreateTestImage(int width = 800, int height = 600)
    {
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    private static (EditorViewModel ViewModel, TestImageSink Sink, TestSettingsStore Settings) CreateFixture(
        int width = 800, int height = 600)
    {
        var doc = new AnnotationDocument(CreateTestImage(width, height));
        var sink = new TestImageSink();
        var settings = new TestSettingsStore();
        var vm = new EditorViewModel(doc, sink, new DocumentRenderer(), null, settings);
        return (vm, sink, settings);
    }

    [Fact]
    [Unit]
    public void ActiveFillUpdatesSelectedRectangle()
    {
        var (vm, _, _) = CreateFixture();
        var rectId = vm.Document.Add(new AnnotationElement(
            null,
            new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 60, 40)),
            new Style(RGBAColor.Black, 2.0, 14.0, fill: null)
        ));
        vm.Document.Select(rectId);
        vm.SyncStyleToSelection();

        Assert.Null(vm.ActiveFill);
        Assert.Null(vm.Document.Element(rectId)!.Style.Fill);

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) raised.Add(e.PropertyName);
        };

        var red = new RGBAColor(1, 0, 0, 1);
        vm.ActiveFill = red;

        // ActiveFill raised property and updated model & element
        Assert.Contains(nameof(EditorViewModel.ActiveFill), raised);
        Assert.Equal(red, vm.ActiveFill);
        Assert.Equal(red, vm.Document.Element(rectId)!.Style.Fill);

        // Test Undo path (Document.SetStyle)
        Assert.True(vm.CanUndo);
        vm.Undo();
        Assert.Null(vm.Document.Element(rectId)!.Style.Fill);

        // Test Redo path
        Assert.True(vm.CanRedo);
        vm.Redo();
        Assert.Equal(red, vm.Document.Element(rectId)!.Style.Fill);

        // Test Setting to null (No fill button path)
        vm.ActiveFill = null;
        Assert.Null(vm.ActiveFill);
        Assert.Null(vm.Document.Element(rectId)!.Style.Fill);
    }

    [Fact]
    [Unit]
    public void ActiveCornerRadiusUpdatesSelectedRectangle()
    {
        var (vm, _, _) = CreateFixture();
        var rectId = vm.Document.Add(new AnnotationElement(
            null,
            new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 60, 40)),
            new Style(RGBAColor.Black, 2.0, 14.0, fill: null, cornerRadius: 0)
        ));
        vm.Document.Select(rectId);
        vm.SyncStyleToSelection();

        Assert.Equal(0, vm.ActiveCornerRadius);
        Assert.Equal(0, vm.Document.Element(rectId)!.Style.CornerRadius);

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) raised.Add(e.PropertyName);
        };

        vm.ActiveCornerRadius = 16.0;

        // ActiveCornerRadius raised property and updated model & element
        Assert.Contains(nameof(EditorViewModel.ActiveCornerRadius), raised);
        Assert.Equal(16.0, vm.ActiveCornerRadius);
        Assert.Equal(16.0, vm.Document.Element(rectId)!.Style.CornerRadius);

        // Test Undo path
        Assert.True(vm.CanUndo);
        vm.Undo();
        Assert.Equal(0, vm.Document.Element(rectId)!.Style.CornerRadius);

        // Test Redo path
        Assert.True(vm.CanRedo);
        vm.Redo();
        Assert.Equal(16.0, vm.Document.Element(rectId)!.Style.CornerRadius);
    }

    [Fact]
    [Unit]
    public void CropAspectConstrainsCropSession()
    {
        var (vm, _, _) = CreateFixture(800, 600);
        vm.ActiveTool = EditorTool.Crop;

        // 1. Draw mode with 16:9 preset
        vm.CropAspect = AspectPreset.SixteenNine;
        vm.GestureStarted(new Point(100, 100));
        vm.GestureMoved(new Point(500, 400));
        vm.GestureEnded(new Point(500, 400));

        Assert.NotNull(vm.CropFrame);
        var f1 = vm.CropFrame.Value;
        double targetRatio = 16.0 / 9.0;
        Assert.Equal(targetRatio, f1.Width / f1.Height, 2);

        // 2. Draw mode with 1:1 (Square) preset
        vm.CropAspect = AspectPreset.Square;
        vm.GestureStarted(new Point(50, 50));
        vm.GestureMoved(new Point(250, 400));
        vm.GestureEnded(new Point(250, 400));

        Assert.NotNull(vm.CropFrame);
        var f2 = vm.CropFrame.Value;
        Assert.Equal(1.0, f2.Width / f2.Height, 2);

        // 3. Direct CropSession tests for resize and draw
        var bounds = new Rect(0, 0, 800, 600);
        var drawSession = new EditorViewModel.CropSession(
            new EditorViewModel.CropSession.Mode.Draw(),
            new Point(50, 50),
            bounds,
            16.0 / 9.0);
        var drawn = drawSession.GetRect(new Point(370, 230), bounds);
        Assert.Equal(targetRatio, drawn.Width / drawn.Height, 2);

        var resizeSession = new EditorViewModel.CropSession(
            new EditorViewModel.CropSession.Mode.Resize(Handle.BottomRight),
            new Point(200, 200),
            new Rect(50, 50, 160, 90),
            16.0 / 9.0);
        var resized = resizeSession.GetRect(new Point(400, 350), bounds);
        Assert.Equal(targetRatio, resized.Width / resized.Height, 2);
    }

    [Fact]
    [Unit]
    public void VisibleStyleFieldsShowsShapeControlsForRectangle()
    {
        var (vm, _, _) = CreateFixture();

        // 1. Tool-level visibility: Rectangle includes Color, StrokeWidth, Fill, CornerRadius
        vm.ActiveTool = EditorTool.Rectangle;
        var rectFields = vm.VisibleStyleFields;
        Assert.True((rectFields & StyleFields.Color) != 0);
        Assert.True((rectFields & StyleFields.StrokeWidth) != 0);
        Assert.True((rectFields & StyleFields.Fill) != 0);
        Assert.True((rectFields & StyleFields.CornerRadius) != 0);

        // 2. Tool-level visibility: Ellipse includes Color, StrokeWidth, Fill; excludes CornerRadius
        vm.ActiveTool = EditorTool.Ellipse;
        var ellipseFields = vm.VisibleStyleFields;
        Assert.True((ellipseFields & StyleFields.Color) != 0);
        Assert.True((ellipseFields & StyleFields.StrokeWidth) != 0);
        Assert.True((ellipseFields & StyleFields.Fill) != 0);
        Assert.False((ellipseFields & StyleFields.CornerRadius) != 0);

        // 3. Selection-level visibility: Selected Rectangle element
        var rectId = vm.Document.Add(new AnnotationElement(
            null,
            new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 50, 50)),
            Style.Default
        ));
        vm.ActiveTool = EditorTool.Select;
        vm.Document.Select(rectId);
        vm.SyncStyleToSelection();

        var selRectFields = vm.VisibleStyleFields;
        Assert.True((selRectFields & StyleFields.Fill) != 0);
        Assert.True((selRectFields & StyleFields.CornerRadius) != 0);
        Assert.True((selRectFields & StyleFields.Color) != 0);
        Assert.True((selRectFields & StyleFields.StrokeWidth) != 0);

        // 4. Selection-level visibility: Selected Ellipse element
        var ellipseId = vm.Document.Add(new AnnotationElement(
            null,
            new AnnotationElement.Kind.Ellipse(new Rect(20, 20, 40, 40)),
            Style.Default
        ));
        vm.Document.Select(ellipseId);
        vm.SyncStyleToSelection();

        var selEllipseFields = vm.VisibleStyleFields;
        Assert.True((selEllipseFields & StyleFields.Fill) != 0);
        Assert.False((selEllipseFields & StyleFields.CornerRadius) != 0);

        // 5. Tool without shape fields: Line
        vm.Document.Select(null);
        vm.ActiveTool = EditorTool.Line;
        var lineFields = vm.VisibleStyleFields;
        Assert.False((lineFields & StyleFields.Fill) != 0);
        Assert.False((lineFields & StyleFields.CornerRadius) != 0);
    }

    [Fact]
    [Unit]
    public void ActiveStyleRaisesActiveFillAndActiveCornerRadius()
    {
        var (vm, _, _) = CreateFixture();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) raised.Add(e.PropertyName);
        };

        var customStyle = new Style(
            RGBAColor.Black,
            strokeWidth: 4.0,
            fontSize: 16.0,
            fill: RGBAColor.Red,
            cornerRadius: 12.0
        );
        vm.ActiveStyle = customStyle;

        Assert.Contains(nameof(EditorViewModel.ActiveFill), raised);
        Assert.Contains(nameof(EditorViewModel.ActiveCornerRadius), raised);
        Assert.Equal(RGBAColor.Red, vm.ActiveFill);
        Assert.Equal(12.0, vm.ActiveCornerRadius);
    }
}
