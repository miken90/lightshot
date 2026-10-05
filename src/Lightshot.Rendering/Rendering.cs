// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Core;

namespace Lightshot.Rendering;

/// <summary>
/// Static entry point for flattening an AnnotationDocument into a RenderedImage.
/// </summary>
public static class Rendering
{
    private static readonly DocumentRenderer DefaultRenderer = new();

    public static RenderedImage Render(AnnotationDocument document)
    {
        return DefaultRenderer.Render(document);
    }
}
