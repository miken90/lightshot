// Ported from LightshotKit/Sources/LightshotKit/HistoryStore.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Where a capture originated.
/// </summary>
public enum CaptureSource
{
    Fullscreen,
    Area,
    Window,
    File,
    Recording
}

/// <summary>
/// What a history entry holds: a screenshot, a video, or a GIF.
/// </summary>
public enum CaptureKind
{
    Screenshot,
    Video,
    Gif
}

/// <summary>
/// One entry in the local capture history.
/// </summary>
public record CaptureRecord(
    Guid Id,
    DateTime Timestamp,
    CaptureSource Source,
    CaptureKind Kind,
    int PixelWidth,
    int PixelHeight,
    double? Duration,
    string FileUrl,
    string ThumbnailUrl);
