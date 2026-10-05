// Ported from LightshotKit/Sources/LightshotKit/ImageSink.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// The output seam: where a RenderedImage goes once the user copies or saves it.
/// </summary>
public interface IImageSink
{
    void CopyToClipboard(RenderedImage image);
    void CopyText(string text);
    void Write(RenderedImage image, string destinationPath, ImageFormat format);
}
