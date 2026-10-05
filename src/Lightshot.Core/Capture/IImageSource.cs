// Ported from LightshotKit/Sources/LightshotKit/ImageSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// The open-existing-image seam: loads images without leaking UI or file-picker framework dependencies.
/// </summary>
public interface IImageSource
{
    Result<CapturedImage, ImageLoadError> LoadImage(string path);
    Result<CapturedImage, ImageLoadError> OpenDocument();
}
