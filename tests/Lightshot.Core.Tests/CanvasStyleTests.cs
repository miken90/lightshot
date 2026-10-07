// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class CanvasStyleTests
{
    [Fact]
    [Unit]
    public void DocumentCanvasIsOffByDefaultAndNotUndone()
    {
        var doc = new AnnotationDocument(new CapturedImage(400, 300, []));
        Assert.Null(doc.Canvas);

        var customStyle = new CanvasStyle(Enabled: true, Padding: 30);
        doc.Canvas = customStyle;
        Assert.Equal(customStyle, doc.Canvas);

        var elem = new AnnotationElement(kind: new AnnotationElement.Kind.Rectangle(new Rect(10, 10, 50, 50)));
        doc.Add(elem);
        Assert.True(doc.CanUndo);

        doc.Undo();
        Assert.Empty(doc.Elements);
        Assert.Equal(customStyle, doc.Canvas);

        doc.Redo();
        Assert.Single(doc.Elements);
        Assert.Equal(customStyle, doc.Canvas);
    }

    [Fact]
    [Unit]
    public void GradientPresetIndexIsClampedAndStable()
    {
        Assert.Equal(12, GradientPresets.All.Count);

        var first = GradientPresets.At(0);
        Assert.Equal("Sunset", first.Name);
        Assert.Equal(135.0, first.AngleDegrees);

        var neg = GradientPresets.At(-5);
        Assert.Equal(first, neg);

        var last = GradientPresets.At(11);
        Assert.Equal("Charcoal", last.Name);

        var over = GradientPresets.At(100);
        Assert.Equal(last, over);

        for (int i = 0; i < 12; i++)
        {
            var p = GradientPresets.At(i);
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.Equal(135.0, p.AngleDegrees);
            Assert.Equal(1.0, p.From.A);
            Assert.Equal(1.0, p.To.A);
        }
    }
}
