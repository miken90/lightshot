// Ported from LightshotKit/Sources/LightshotKit/QuickAccess.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// Actions run after every completed screenshot capture.
/// </summary>
public record AfterCaptureSettings(
    bool ShowQuickAccess = true,
    bool CopyToClipboard = true,
    bool SaveToFile = false);
