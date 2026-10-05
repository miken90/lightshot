// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// Renders an AnnotationDocument (base image + elements in z-order + focus dim + redactions)
/// into a flattened RenderedImage. Implemented in Lightshot.Rendering.
/// </summary>
public interface IImageRenderer
{
    RenderedImage Render(AnnotationDocument document);
}
