// Ported from LightshotKit/Sources/LightshotKit/CaptureError.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Typed failure surface for a capture attempt.
/// </summary>
public abstract record CaptureError
{
    private CaptureError() { }

    /// <summary>
    /// Screen Recording permission is missing or was revoked.
    /// </summary>
    public sealed record PermissionDenied : CaptureError;

    /// <summary>
    /// No display was available to capture.
    /// </summary>
    public sealed record NoDisplayAvailable : CaptureError;

    /// <summary>
    /// The user backed out of the capture. A silent no-op.
    /// </summary>
    public sealed record UserCancelled : CaptureError;

    /// <summary>
    /// Any other failure from the OS capture layer.
    /// </summary>
    public sealed record SystemFailure(string Message) : CaptureError;
}
