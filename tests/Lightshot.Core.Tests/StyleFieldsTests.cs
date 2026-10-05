// Ported from LightshotKit/Tests/LightshotKitTests/StyleFieldsTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class StyleFieldsTests
{
    [Fact]
    [Unit]
    public void EachKindShowsOnlyTheStyleItIsDrawnWith()
    {
        var a = new Point(0, 0);
        var b = new Point(10, 10);
        var box = new Rect(0, 0, 10, 10);
        Assert.Equal(StyleFields.Color | StyleFields.StrokeWidth | StyleFields.ArrowStyle, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Arrow(a, b)));
        Assert.Equal(StyleFields.Color | StyleFields.StrokeWidth, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Line(a, b)));
        Assert.Equal(StyleFields.Color | StyleFields.StrokeWidth, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Rectangle(box)));
        Assert.Equal(StyleFields.Color | StyleFields.StrokeWidth, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Ellipse(box)));
        Assert.Equal(StyleFields.Color | StyleFields.StrokeWidth, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Freehand([a, b])));
        Assert.Equal(StyleFields.Color | StyleFields.FontSize, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Text("Hi", box)));
        Assert.Equal(StyleFields.Color, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Highlight(box)));
        Assert.Equal(StyleFields.Color | StyleFields.FontSize, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.StepMarker(1, a, 12)));
        Assert.Equal(StyleFields.Redaction, StyleFieldsExtensions.Fields(new AnnotationElement.Kind.Redaction(box, RedactionStyle.Blackout)));
    }
}
