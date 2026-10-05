// Ported from LightshotKit/Sources/LightshotKit/PermissionKind.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// A permission Lightshot must hold for a capability to work.
/// </summary>
public enum PermissionKind
{
    ScreenRecording,
    Microphone,
    Camera,
    InputMonitoring,
    SpeechRecognition
}
