// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// Encodes a RenderedImage into compressed or uncompressed bytes (PNG, JPEG, etc.).
/// Implemented in Lightshot.Rendering.
/// </summary>
public interface IImageCodec
{
    byte[] Encode(RenderedImage image, ImageFormat format);
}
