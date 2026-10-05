// Ported from LightshotKit/Sources/LightshotKit/ImageSource.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// Failure cases when loading an existing image file from disk.
/// </summary>
public abstract record ImageLoadError
{
    private ImageLoadError() { }

    /// <summary>
    /// User dismissed the file open dialog.
    /// </summary>
    public sealed record UserCancelled : ImageLoadError;

    /// <summary>
    /// The file exists but could not be read.
    /// </summary>
    public sealed record Unreadable : ImageLoadError;

    /// <summary>
    /// The bytes read do not represent a supported image format.
    /// </summary>
    public sealed record UnsupportedFormat : ImageLoadError;
}
