// Ported from LightshotKit/Sources/LightshotKit/AnnotationElement.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Which style controls apply to an element kind: the editor's toolbar shows only these
/// for the active tool or the selected element.
/// </summary>
[Flags]
public enum StyleFields
{
    None = 0,
    Color = 1 << 0,
    StrokeWidth = 1 << 1,
    FontSize = 1 << 2,
    ArrowStyle = 1 << 3,
    Redaction = 1 << 4,
    Fill = 1 << 5,
    CornerRadius = 1 << 6
}

public static class StyleFieldsExtensions
{
    public static StyleFields Fields(AnnotationElement.Kind kind) => kind switch
    {
        AnnotationElement.Kind.Arrow => StyleFields.Color | StyleFields.StrokeWidth | StyleFields.ArrowStyle,
        AnnotationElement.Kind.Line or
        AnnotationElement.Kind.Rectangle or
        AnnotationElement.Kind.Ellipse or
        AnnotationElement.Kind.Freehand => StyleFields.Color | StyleFields.StrokeWidth,
        AnnotationElement.Kind.Text or
        AnnotationElement.Kind.StepMarker => StyleFields.Color | StyleFields.FontSize,
        AnnotationElement.Kind.Highlight => StyleFields.Color,
        AnnotationElement.Kind.Redaction => StyleFields.Redaction,
        AnnotationElement.Kind.Focus => StyleFields.None,
        _ => StyleFields.None
    };

    public static StyleFields ShapeFields(AnnotationElement.Kind? kind) => kind switch
    {
        AnnotationElement.Kind.Rectangle => StyleFields.Fill | StyleFields.CornerRadius,
        AnnotationElement.Kind.Ellipse => StyleFields.Fill,
        _ => StyleFields.None
    };
}
