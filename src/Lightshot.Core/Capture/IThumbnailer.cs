// Ported from LightshotKit/Sources/LightshotKit/HistoryStore.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// Generates scaled thumbnail image bytes from full-resolution image data.
/// Implemented in Lightshot.Rendering (via SkiaSharp).
/// </summary>
public interface IThumbnailer
{
    byte[]? CreateThumbnail(byte[] imageData, int maxPixelSize);
}
