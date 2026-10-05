// Ported from LightshotKit/Sources/LightshotKit/CaptureAuthorizationStatus.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// Permission state, as the app understands it before a capture.
/// A three-state value (matching the OS permission pattern).
/// </summary>
public enum CaptureAuthorizationStatus
{
    NotDetermined,
    Authorized,
    Denied
}
