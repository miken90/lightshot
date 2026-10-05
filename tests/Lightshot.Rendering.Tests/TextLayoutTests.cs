// Ported from LightshotKit/Tests/LightshotKitTests/TextLayoutTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Rendering.Tests;

public class TextLayoutTests
{
    private static AnnotationDocument Blank()
    {
        return new AnnotationDocument(new CapturedImage(400, 300, Array.Empty<byte>()));
    }

    private static ElementID AddLabel(AnnotationDocument doc, double fontSize = 20.0)
    {
        var box = TextLayout.Box("", fontSize, new Point(50, 40));
        return doc.Add(new AnnotationElement(
            kind: new AnnotationElement.Kind.Text("", box),
            style: new Style(fontSize: fontSize)));
    }

    private static Rect GetBox(AnnotationDocument doc, ElementID id)
    {
        return doc.Element(id)!.ElementKind.BoundingBox;
    }

    [Fact]
    [Render]
    public void ANewLabelIsOneLineTallAndGrowsAsItIsTyped()
    {
        var doc = Blank();
        var id = AddLabel(doc);
        var empty = GetBox(doc, id);
        double pad = TextLayout.Padding(20);

        Assert.Equal(new Point(50, 40), empty.Origin);
        Assert.Equal(TextLayout.MinimumWidth(20), empty.Width);
        Assert.True(empty.Height > 20 && empty.Height < 20 * 1.6 + 2 * pad);

        doc.UpdateText(id, "Campaign Start");
        var typed = GetBox(doc, id);

        Assert.True(typed.Width > empty.Width);
        Assert.Equal(empty.Height, typed.Height);
        Assert.True(TextLayout.HasNaturalWidth(typed, "Campaign Start", 20));
    }

    [Fact]
    [Render]
    public void ASideHandleSetsTheWidthAndTheTextWraps()
    {
        var doc = Blank();
        var id = AddLabel(doc);
        doc.UpdateText(id, "Campaign Start");
        var natural = GetBox(doc, id);

        doc.Transform(id, new Transform.Resize(Handle.Right, -natural.Width / 2.0, 0));
        var narrow = GetBox(doc, id);

        Assert.Equal(natural.MinX, narrow.MinX);
        Assert.True(Math.Abs(narrow.Width - natural.Width / 2.0) < 0.001);
        Assert.True(narrow.Height > natural.Height);

        // Typing more keeps the hand-set width
        doc.UpdateText(id, "Campaign Start!");
        Assert.True(Math.Abs(GetBox(doc, id).Width - narrow.Width) < 0.001);

        // Left handle moves left edge, right edge stays
        doc.Transform(id, new Transform.Resize(Handle.Left, -20, 0));
        var moved = GetBox(doc, id);
        Assert.True(Math.Abs(moved.MaxX - narrow.MaxX) < 0.001);
        Assert.True(Math.Abs(moved.MinX - (narrow.MinX - 20)) < 0.001);

        // Never narrower than minimum
        doc.Transform(id, new Transform.Resize(Handle.Right, -1000, 0));
        Assert.Equal(TextLayout.MinimumWidth(20), GetBox(doc, id).Width);
    }

    [Fact]
    [Render]
    public void TheCornerScalesTheTextAndOtherHandlesLeaveALabelAlone()
    {
        var doc = Blank();
        var id = AddLabel(doc);
        doc.UpdateText(id, "Hi");
        var before = GetBox(doc, id);

        doc.Transform(id, new Transform.Resize(Handle.BottomRight, before.Width, 0));
        Assert.Equal(40, doc.Element(id)!.Style.FontSize);

        var after = GetBox(doc, id);
        Assert.Equal(before.Origin, after.Origin);
        Assert.True(after.Height > before.Height * 1.5);

        doc.Transform(id, new Transform.Resize(Handle.Top, 30, 30));
        Assert.Equal(after, GetBox(doc, id));
    }

    [Fact]
    [Render]
    public void ANewFontSizeRefitsTheBox()
    {
        var doc = Blank();
        var id = AddLabel(doc);
        doc.UpdateText(id, "Hello");
        var small = GetBox(doc, id);

        var style = doc.Element(id)!.Style;
        doc.SetStyle(id, style with { FontSize = 30 });
        var large = GetBox(doc, id);

        Assert.True(large.Width > small.Width && large.Height > small.Height);
        Assert.True(TextLayout.HasNaturalWidth(large, "Hello", 30));
    }
}
