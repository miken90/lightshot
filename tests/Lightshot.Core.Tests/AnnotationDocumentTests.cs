// Ported from LightshotKit/Tests/LightshotKitTests/AnnotationDocumentTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class AnnotationDocumentTests
{
    private static AnnotationDocument MakeDocument(int width = 400, int height = 300) =>
        new(new CapturedImage(width, height, []));

    private static ElementID AddMarker(AnnotationDocument doc, double x = 0) =>
        doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.StepMarker(0, new Point(x, 0), 12)));

    private static int? Number(AnnotationDocument doc, ElementID id) =>
        doc.Element(id)?.ElementKind is AnnotationElement.Kind.StepMarker sm ? sm.Number : null;

    private static (Point From, Point To, Point? Bend, ArrowStyle Style) Arrow(AnnotationDocument doc, ElementID id)
    {
        var a = (AnnotationElement.Kind.Arrow)doc.Element(id)!.ElementKind;
        return (a.From, a.To, a.Bend, a.Style);
    }

    [Fact]
    [Unit]
    public void AddAppendsAndReturnsAddressableID()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 50, 40))));
        Assert.Single(doc.Elements);
        Assert.Equal(id, doc.Element(id)?.Id);
    }

    [Fact]
    [Unit]
    public void AddDoesNotChangeSelection()
    {
        var doc = MakeDocument();
        _ = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        Assert.Null(doc.SelectedID);
    }

    [Fact]
    [Unit]
    public void SelectRecordsAKnownElementAndClearsOnNil()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Select(id);
        Assert.Equal(id, doc.SelectedID);
        doc.Select(null);
        Assert.Null(doc.SelectedID);
    }

    [Fact]
    [Unit]
    public void SelectIgnoresUnknownIdentity()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Select(id);
        doc.Select(new ElementID());
        Assert.Equal(id, doc.SelectedID);
    }

    [Fact]
    [Unit]
    public void DeleteRemovesElementAndClearsItsSelection()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Select(id);
        doc.Delete(id);
        Assert.Empty(doc.Elements);
        Assert.Null(doc.SelectedID);
    }

    [Fact]
    [Unit]
    public void MoveTranslatesEveryCoordinate()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(new Point(10, 10), new Point(30, 40))));
        doc.Transform(id, new Transform.Move(5, -3));
        var arrow = Assert.IsType<AnnotationElement.Kind.Arrow>(doc.Element(id)?.ElementKind);
        Assert.Equal(new Point(15, 7), arrow.From);
        Assert.Equal(new Point(35, 37), arrow.To);
    }

    [Fact]
    [Unit]
    public void ResizeFromBottomRightGrowsBoundingBox()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 100, 100))));
        doc.Transform(id, new Transform.Resize(Handle.BottomRight, 50, 20));
        var box = doc.Element(id)!.ElementKind.BoundingBox;
        Assert.Equal(new Rect(0, 0, 150, 120), box);
    }

    [Fact]
    [Unit]
    public void ResizeScalesInteriorPointsProportionally()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Line(new Point(0, 0), new Point(100, 100))));
        doc.Transform(id, new Transform.Resize(Handle.Right, 100, 0));
        var line = Assert.IsType<AnnotationElement.Kind.Line>(doc.Element(id)?.ElementKind);
        Assert.Equal(new Point(0, 0), line.From);
        Assert.Equal(new Point(200, 100), line.To);
    }

    [Fact]
    [Unit]
    public void ResizeKeepsStepMarkerCircular()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.StepMarker(1, new Point(50, 50), 10)));
        doc.Transform(id, new Transform.Resize(Handle.BottomRight, 20, 0));
        var sm = Assert.IsType<AnnotationElement.Kind.StepMarker>(doc.Element(id)?.ElementKind);
        Assert.Equal(10.0, sm.Radius);
        Assert.Equal(new Point(60, 50), sm.Center);
    }

    [Fact]
    [Unit]
    public void TransformOnUnknownIdIsANoOp()
    {
        var doc = MakeDocument();
        _ = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Transform(new ElementID(), new Transform.Move(100, 100));
        doc.Undo();
        Assert.Empty(doc.Elements);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    [Unit]
    public void SetStyleReplacesAppearanceOnly()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        var box = doc.Element(id)!.ElementKind.BoundingBox;
        doc.SetStyle(id, new Style(color: RGBAColor.Black, strokeWidth: 8));
        Assert.Equal(8.0, doc.Element(id)?.Style.StrokeWidth);
        Assert.Equal(box, doc.Element(id)?.ElementKind.BoundingBox);
    }

    [Fact]
    [Unit]
    public void UpdateTextRewritesStringKeepingAHandSetWidth()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Text("before", new Rect(0, 0, 80, 20))));
        doc.UpdateText(id, "after");
        var t = Assert.IsType<AnnotationElement.Kind.Text>(doc.Element(id)?.ElementKind);
        Assert.Equal("after", t.Content);
        Assert.True(t.Box.Origin == new Point(0, 0) && t.Box.Width == 80);
        Assert.Equal(TextLayout.Box("after", Style.Default.FontSize, t.Box.Origin, 80).Height, t.Box.Height);
    }

    [Fact]
    [Unit]
    public void UpdateTextOnNonTextElementIsANoOp()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.UpdateText(id, "nope");
        doc.Undo();
        Assert.Empty(doc.Elements);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    [Unit]
    public void SetStepRadiusResizesTheDisc()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.StepMarker(0, new Point(50, 50), 12)));
        doc.SetStepRadius(id, 30);
        var sm = Assert.IsType<AnnotationElement.Kind.StepMarker>(doc.Element(id)?.ElementKind);
        Assert.Equal(30.0, sm.Radius);
        Assert.Equal(new Point(50, 50), sm.Center);
    }

    [Fact]
    [Unit]
    public void SetStepRadiusOnNonMarkerIsANoOp()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.SetStepRadius(id, 30);
        doc.Undo();
        Assert.Empty(doc.Elements);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    [Unit]
    public void ConsecutiveStyleEditsCollapseToOneUndoStep()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.SetStyle(id, new Style(strokeWidth: 4));
        doc.SetStyle(id, new Style(strokeWidth: 8));
        doc.SetStyle(id, new Style(strokeWidth: 12));
        doc.Undo();
        Assert.Equal(Style.Default.StrokeWidth, doc.Element(id)?.Style.StrokeWidth);
        Assert.True(doc.CanUndo);
        doc.Undo();
        Assert.Empty(doc.Elements);
    }

    [Fact]
    [Unit]
    public void ConsecutiveTextEditsCollapseToOneUndoStep()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Text("", new Rect(0, 0, 80, 20))));
        doc.UpdateText(id, "h");
        doc.UpdateText(id, "he");
        doc.UpdateText(id, "hello");
        doc.Undo();
        var t = Assert.IsType<AnnotationElement.Kind.Text>(doc.Element(id)?.ElementKind);
        Assert.Equal("", t.Content);
        Assert.True(doc.CanUndo);
    }

    [Fact]
    [Unit]
    public void EndCoalescingSplitsRunsIntoSeparateUndoSteps()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.SetStyle(id, new Style(strokeWidth: 8));
        doc.EndCoalescing();
        doc.SetStyle(id, new Style(strokeWidth: 16));
        doc.Undo();
        Assert.Equal(8.0, doc.Element(id)?.Style.StrokeWidth);
    }

    [Fact]
    [Unit]
    public void SelectionClosesTheRun()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.SetStyle(id, new Style(strokeWidth: 8));
        doc.Select(id);
        doc.SetStyle(id, new Style(strokeWidth: 16));
        doc.Undo();
        Assert.Equal(8.0, doc.Element(id)?.Style.StrokeWidth);
    }

    [Fact]
    [Unit]
    public void StyleAndStepRadiusShareOneUndoStep()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.StepMarker(0, new Point(20, 20), 12)));
        doc.SetStyle(id, new Style(fontSize: 40));
        doc.SetStepRadius(id, 40);
        doc.Undo();
        var sm = Assert.IsType<AnnotationElement.Kind.StepMarker>(doc.Element(id)?.ElementKind);
        Assert.Equal(12.0, sm.Radius);
        Assert.True(doc.CanUndo);
    }

    [Fact]
    [Unit]
    public void DifferentCommandsBetweenEditsBreakCoalescing()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.SetStyle(id, new Style(strokeWidth: 8));
        doc.Transform(id, new Transform.Move(5, 5));
        doc.SetStyle(id, new Style(strokeWidth: 16));
        doc.Undo();
        Assert.Equal(8.0, doc.Element(id)?.Style.StrokeWidth);
    }

    [Fact]
    [Unit]
    public void AddsStackInInsertionOrder()
    {
        var doc = MakeDocument();
        var a = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        var b = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        Assert.Equal([a, b], doc.Elements.Select(e => e.Id));
    }

    [Fact]
    [Unit]
    public void ReorderMovesElementToNewIndex()
    {
        var doc = MakeDocument();
        var a = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        var b = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        var c = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Reorder(a, 2);
        Assert.Equal([b, c, a], doc.Elements.Select(e => e.Id));
    }

    [Fact]
    [Unit]
    public void ReorderClampsOutOfRangeIndex()
    {
        var doc = MakeDocument();
        var a = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        var b = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Reorder(a, 99);
        Assert.Equal([b, a], doc.Elements.Select(e => e.Id));
    }

    [Fact]
    [Unit]
    public void ReturnsTopmostElementAtPoint()
    {
        var doc = MakeDocument();
        var bottom = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 100, 100))));
        var top = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 100, 100))));
        Assert.Equal(top, doc.ElementID(new Point(50, 50)));
    }

    [Fact]
    [Unit]
    public void ReorderChangesWhichElementIsHit()
    {
        var doc = MakeDocument();
        var bottom = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 100, 100))));
        var top = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 100, 100))));
        doc.Reorder(bottom, 1);
        Assert.Equal(bottom, doc.ElementID(new Point(50, 50)));
    }

    [Fact]
    [Unit]
    public void MissReturnsNil()
    {
        var doc = MakeDocument();
        _ = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 20, 20))));
        Assert.Null(doc.ElementID(new Point(200, 200)));
    }

    [Fact]
    [Unit]
    public void ThinLineIsHitWithinStrokeTolerance()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Line(new Point(0, 0), new Point(100, 0))));
        Assert.Equal(id, doc.ElementID(new Point(50, 4)));
        Assert.Null(doc.ElementID(new Point(50, 40)));
    }

    [Fact]
    [Unit]
    public void EllipseUsesRadialContainmentNotBoundingBox()
    {
        var doc = MakeDocument();
        _ = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Ellipse(new Rect(0, 0, 100, 100))));
        Assert.NotNull(doc.ElementID(new Point(50, 50)));
        Assert.Null(doc.ElementID(new Point(2, 2)));
    }

    [Fact]
    [Unit]
    public void NumbersAutoIncrementOnAdd()
    {
        var doc = MakeDocument();
        var a = AddMarker(doc, 0);
        var b = AddMarker(doc, 20);
        var c = AddMarker(doc, 40);
        Assert.Equal(1, Number(doc, a));
        Assert.Equal(2, Number(doc, b));
        Assert.Equal(3, Number(doc, c));
    }

    [Fact]
    [Unit]
    public void AddIgnoresCallerSuppliedNumber()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.StepMarker(99, new Point(0, 0), 12)));
        Assert.Equal(1, Number(doc, id));
    }

    [Fact]
    [Unit]
    public void DeletingAMarkerDoesNotRenumberSurvivors()
    {
        var doc = MakeDocument();
        var a = AddMarker(doc, 0);
        var b = AddMarker(doc, 20);
        var c = AddMarker(doc, 40);
        doc.Delete(b);
        Assert.Equal(1, Number(doc, a));
        Assert.Equal(3, Number(doc, c));
        var d = AddMarker(doc, 60);
        Assert.Equal(4, Number(doc, d));
    }

    [Fact]
    [Unit]
    public void DeletingHighestMarkerFreesItsNumberForReuse()
    {
        var doc = MakeDocument();
        _ = AddMarker(doc, 0);
        _ = AddMarker(doc, 20);
        var c = AddMarker(doc, 40);
        doc.Delete(c);
        var d = AddMarker(doc, 60);
        Assert.Equal(3, Number(doc, d));
    }

    [Fact]
    [Unit]
    public void NumberingResetsOnceAllMarkersGone()
    {
        var doc = MakeDocument();
        var a = AddMarker(doc, 0);
        doc.Delete(a);
        var b = AddMarker(doc, 0);
        Assert.Equal(1, Number(doc, b));
    }

    [Fact]
    [Unit]
    public void UndoRedoReversesAndReappliesAdd()
    {
        var doc = MakeDocument();
        _ = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        Assert.True(doc.CanUndo);
        doc.Undo();
        Assert.Empty(doc.Elements);
        doc.Redo();
        Assert.Single(doc.Elements);
    }

    [Fact]
    [Unit]
    public void UndoRestoresDeletedElement()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Delete(id);
        doc.Undo();
        Assert.NotNull(doc.Element(id));
    }

    [Fact]
    [Unit]
    public void UndoRevertsMoveGeometry()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(new Point(0, 0), new Point(10, 10))));
        doc.Transform(id, new Transform.Move(25, 25));
        doc.Undo();
        var arrow = Assert.IsType<AnnotationElement.Kind.Arrow>(doc.Element(id)?.ElementKind);
        Assert.Equal(new Point(0, 0), arrow.From);
    }

    [Fact]
    [Unit]
    public void UndoRevertsStyleAndText()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Text("v1", new Rect(0, 0, 40, 20))));
        doc.SetStyle(id, new Style(strokeWidth: 9));
        doc.UpdateText(id, "v2");
        doc.Undo();
        doc.Undo();
        Assert.Equal(Style.Default.StrokeWidth, doc.Element(id)?.Style.StrokeWidth);
        var t = Assert.IsType<AnnotationElement.Kind.Text>(doc.Element(id)?.ElementKind);
        Assert.Equal("v1", t.Content);
    }

    [Fact]
    [Unit]
    public void RedoReappliesANonAddCommand()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(new Point(0, 0), new Point(10, 10))));
        doc.Transform(id, new Transform.Move(25, 25));
        doc.Undo();
        doc.Redo();
        var arrow = Assert.IsType<AnnotationElement.Kind.Arrow>(doc.Element(id)?.ElementKind);
        Assert.Equal(new Point(25, 25), arrow.From);
    }

    [Fact]
    [Unit]
    public void UndoRevertsReorder()
    {
        var doc = MakeDocument();
        var a = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        var b = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Reorder(a, 1);
        doc.Undo();
        Assert.Equal([a, b], doc.Elements.Select(e => e.Id));
    }

    [Fact]
    [Unit]
    public void NewCommandAfterUndoInvalidatesRedoStack()
    {
        var doc = MakeDocument();
        _ = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Undo();
        Assert.True(doc.CanRedo);
        _ = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Ellipse(new Rect(0, 0, 10, 10))));
        Assert.False(doc.CanRedo);
        doc.Redo();
        Assert.Single(doc.Elements);
    }

    [Fact]
    [Unit]
    public void UndoAndRedoAreNoOpsWhenStacksEmpty()
    {
        var doc = MakeDocument();
        doc.Undo();
        doc.Redo();
        Assert.Empty(doc.Elements);
        Assert.False(doc.CanUndo);
        Assert.False(doc.CanRedo);
    }

    [Fact]
    [Unit]
    public void SelectIsNotUndoable()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(0, 0, 10, 10))));
        doc.Select(id);
        Assert.True(doc.CanUndo);
        doc.Undo();
        Assert.Empty(doc.Elements);
        Assert.Null(doc.SelectedID);
    }

    [Fact]
    [Unit]
    public void VisibleFrameDefaultsToFullImage()
    {
        var doc = MakeDocument(400, 300);
        Assert.Null(doc.CropRect);
        Assert.Equal(new Rect(0, 0, 400, 300), doc.VisibleFrame);
    }

    [Fact]
    [Unit]
    public void ApplyCropRecordsTheVisibleFrame()
    {
        var doc = MakeDocument();
        doc.ApplyCrop(new Rect(50, 40, 100, 80));
        Assert.Equal(new Rect(50, 40, 100, 80), doc.VisibleFrame);
    }

    [Fact]
    [Unit]
    public void ApplyCropClampsToImageBounds()
    {
        var doc = MakeDocument(400, 300);
        doc.ApplyCrop(new Rect(-50, -50, 1000, 1000));
        Assert.Equal(new Rect(0, 0, 400, 300), doc.VisibleFrame);
    }

    [Fact]
    [Unit]
    public void CropDoesNotMoveElementGeometry()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(120, 100, 40, 30))));
        doc.ApplyCrop(new Rect(100, 90, 120, 90));
        Assert.Equal(new Rect(120, 100, 40, 30), doc.Element(id)?.ElementKind.BoundingBox);
    }

    [Fact]
    [Unit]
    public void CropAppliedThenReversedLeavesElementsIntactInImageSpace()
    {
        var doc = MakeDocument();
        var a = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 40, 30))));
        var b = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(new Point(200, 150), new Point(260, 190))));
        var before = doc.Elements.ToList();
        doc.ApplyCrop(new Rect(0, 0, 150, 120));
        doc.Undo();
        Assert.Null(doc.CropRect);
        Assert.Equal(doc.ImageBounds, doc.VisibleFrame);
        Assert.Equal(before, doc.Elements);
        Assert.Equal(new Rect(10, 10, 40, 30), doc.Element(a)?.ElementKind.BoundingBox);
        Assert.NotNull(doc.Element(b));
    }

    [Fact]
    [Unit]
    public void ReshapingTheTipLeavesTheTailWhereItWas()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(new Point(10, 10), new Point(110, 10))));
        doc.Transform(id, new Transform.Reshape(EndpointHandle.Tip, 20, 30));
        var arr = Arrow(doc, id);
        Assert.Equal(new Point(10, 10), arr.From);
        Assert.Equal(new Point(130, 40), arr.To);
    }

    [Fact]
    [Unit]
    public void ReshapingALineMovesOneEndpoint()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Line(new Point(10, 10), new Point(110, 10))));
        doc.Transform(id, new Transform.Reshape(EndpointHandle.Tail, 5, 5));
        var line = Assert.IsType<AnnotationElement.Kind.Line>(doc.Element(id)?.ElementKind);
        Assert.Equal(new Point(15, 15), line.From);
        Assert.Equal(new Point(110, 10), line.To);
    }

    [Fact]
    [Unit]
    public void DraggingAnEndpointCarriesTheBendWithTheShaft()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(
            new Point(100, 100), new Point(200, 100), new Point(150, 70), ArrowStyle.Curved
        )));
        doc.Transform(id, new Transform.Reshape(EndpointHandle.Tip, -100, 100));
        var bend = Arrow(doc, id).Bend;
        Assert.NotNull(bend);
        Assert.True(Math.Abs(bend.Value.X - 130) < 0.001);
        Assert.True(Math.Abs(bend.Value.Y - 150) < 0.001);
    }

    [Fact]
    [Unit]
    public void DraggingTheBendRecurvesABendableArrowOnly()
    {
        var doc = MakeDocument();
        var curved = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(
            new Point(0, 0), new Point(100, 0), new Point(50, -10), ArrowStyle.Curved
        )));
        var straight = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(new Point(0, 50), new Point(100, 50))));
        doc.Transform(curved, new Transform.Reshape(EndpointHandle.Bend, 0, -20));
        doc.Transform(straight, new Transform.Reshape(EndpointHandle.Bend, 0, -20));
        Assert.Equal(new Point(50, -30), Arrow(doc, curved).Bend);
        Assert.Null(Arrow(doc, straight).Bend);
    }

    [Fact]
    [Unit]
    public void MovingACurvedArrowMovesItsBend()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(
            new Point(0, 0), new Point(100, 0), new Point(50, -10), ArrowStyle.Double
        )));
        doc.Transform(id, new Transform.Move(7, 9));
        Assert.Equal(new Point(57, -1), Arrow(doc, id).Bend);
    }

    [Fact]
    [Unit]
    public void SwitchingToABendableStyleAddsABendAndSwitchingBackDropsIt()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(new Point(0, 100), new Point(100, 100))));
        doc.SetArrowStyle(id, ArrowStyle.Curved);
        Assert.Equal(ArrowStyle.Curved, Arrow(doc, id).Style);
        Assert.NotNull(Arrow(doc, id).Bend);

        doc.SetArrowStyle(id, ArrowStyle.Fancy);
        Assert.Null(Arrow(doc, id).Bend);

        doc.Undo();
        Assert.Equal(ArrowStyle.Curved, Arrow(doc, id).Style);
    }

    [Fact]
    [Unit]
    public void ACurvedArrowIsHitAlongItsCurveNotItsChord()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Arrow(
            new Point(50, 200), new Point(250, 200), new Point(150, 120), ArrowStyle.Curved
        )));
        Assert.Equal(id, doc.ElementID(new Point(150, 120)));
        Assert.Null(doc.ElementID(new Point(150, 200)));
    }

    [Fact]
    [Unit]
    public void SetRedactionRestylesInPlaceKeepingRectAndSeed()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(10, 10, 80, 40), RedactionStyle.Blackout, 0.5, 42)));
        doc.SetRedaction(id, RedactionStyle.Blur, 1.7);
        Assert.Equal(new AnnotationElement.Kind.Redaction(new Rect(10, 10, 80, 40), RedactionStyle.Blur, 1.0, 42), doc.Element(id)?.ElementKind);
    }

    [Fact]
    [Unit]
    public void OneStrengthSliderDragIsOneUndoStep()
    {
        var doc = MakeDocument();
        var id = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Redaction(new Rect(10, 10, 80, 40), RedactionStyle.Blur, 0.2, 1)));
        double[] strengths = [0.3, 0.4, 0.5];
        foreach (var s in strengths)
        {
            doc.SetRedaction(id, RedactionStyle.Blur, s);
        }
        doc.EndCoalescing();
        doc.Undo();
        Assert.Equal(new AnnotationElement.Kind.Redaction(new Rect(10, 10, 80, 40), RedactionStyle.Blur, 0.2, 1), doc.Element(id)?.ElementKind);
    }

    [Fact]
    [Unit]
    public void ADrawingToolGrabsAMarkButCanStillDrawInsideAnOutline()
    {
        var doc = new AnnotationDocument(new CapturedImage(400, 300, []));
        var box = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(50, 50, 200, 100))));
        var ring = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Ellipse(new Rect(300, 50, 80, 80))));
        var wash = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Highlight(new Rect(50, 200, 100, 50))));

        Assert.Equal(box, doc.GrabbableElementID(new Point(50, 100)));
        Assert.Null(doc.GrabbableElementID(new Point(150, 100)));
        Assert.Equal(box, doc.ElementID(new Point(150, 100)));

        Assert.Equal(ring, doc.GrabbableElementID(new Point(340, 50)));
        Assert.Null(doc.GrabbableElementID(new Point(340, 90)));

        Assert.Equal(wash, doc.GrabbableElementID(new Point(100, 225)));

        var filled = Style.Default with { Fill = RGBAColor.Red };
        var solid = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(20, 260, 40, 30)), style: filled));
        Assert.Equal(solid, doc.GrabbableElementID(new Point(40, 275)));
    }

    [Fact]
    [Unit]
    public void UndoRestoresSnapshot()
    {
        var doc = MakeDocument();
        var id1 = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 50, 40))));
        var id2 = doc.Add(new AnnotationElement(kind: new AnnotationElement.Kind.Line(new Point(0, 0), new Point(100, 100))));
        doc.ApplyCrop(new Rect(0, 0, 200, 200));

        var snapshotElements = doc.Elements.Select(e => new AnnotationElement(e.Id, e.ElementKind, e.Style)).ToList();
        var snapshotCrop = doc.CropRect;
        var snapshotFrame = doc.VisibleFrame;

        doc.Transform(id1, new Transform.Move(20, 30));
        doc.SetStyle(id1, new Style(color: RGBAColor.Red, strokeWidth: 10));
        doc.Delete(id2);
        doc.ApplyCrop(new Rect(10, 10, 80, 80));

        doc.Undo(); // undo crop
        doc.Undo(); // undo delete
        doc.Undo(); // undo setStyle
        doc.Undo(); // undo move

        Assert.Equal(snapshotCrop, doc.CropRect);
        Assert.Equal(snapshotFrame, doc.VisibleFrame);
        Assert.Equal(snapshotElements.Count, doc.Elements.Count);
        for (int i = 0; i < snapshotElements.Count; i++)
        {
            Assert.Equal(snapshotElements[i], doc.Elements[i]);
        }
    }
}
