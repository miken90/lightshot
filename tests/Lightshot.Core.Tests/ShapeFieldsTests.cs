// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class ShapeFieldsTests
{
    [Fact]
    [Unit]
    public void FlagsMatchSpec()
    {
        // 1. Verify enum flag values
        Assert.Equal(1 << 5, (int)StyleFields.Fill);
        Assert.Equal(1 << 6, (int)StyleFields.CornerRadius);

        // 2. Rectangle shape fields
        var rectKind = new AnnotationElement.Kind.Rectangle(Rect.Zero);
        Assert.Equal(StyleFields.Fill | StyleFields.CornerRadius, StyleFieldsExtensions.ShapeFields(rectKind));

        // 3. Ellipse shape fields
        var ellipseKind = new AnnotationElement.Kind.Ellipse(Rect.Zero);
        Assert.Equal(StyleFields.Fill, StyleFieldsExtensions.ShapeFields(ellipseKind));

        // 4. Other elements return None
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.Arrow(Point.Zero, Point.Zero)));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.Line(Point.Zero, Point.Zero)));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.Freehand([])));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.Text("", Rect.Zero)));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.Highlight(Rect.Zero)));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.Redaction(Rect.Zero, RedactionStyle.Blackout)));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.Focus(Rect.Zero)));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(new AnnotationElement.Kind.StepMarker(1, Point.Zero, 10)));
        Assert.Equal(StyleFields.None, StyleFieldsExtensions.ShapeFields(null));
    }

    [Fact]
    [Unit]
    public void StyleCornerRadiusClampsToZero()
    {
        var defaultStyle = new Style();
        Assert.Equal(0, defaultStyle.CornerRadius);

        var positiveStyle = new Style(cornerRadius: 16);
        Assert.Equal(16, positiveStyle.CornerRadius);

        var negativeStyle = new Style(cornerRadius: -8);
        Assert.Equal(0, negativeStyle.CornerRadius);

        var mutatedStyle = new Style { CornerRadius = -5 };
        Assert.Equal(0, mutatedStyle.CornerRadius);

        var clonedStyle = positiveStyle with { Color = RGBAColor.Black };
        Assert.Equal(16, clonedStyle.CornerRadius);
    }
}
