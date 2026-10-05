// Ported from LightshotKit/Sources/LightshotKit/RecordingError.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// Typed failure surface for a recording session.
/// </summary>
public abstract record RecordingError
{
    public sealed record PermissionDenied(PermissionKind Kind) : RecordingError;
    public sealed record NoDisplayAvailable : RecordingError
    {
        public static readonly NoDisplayAvailable Instance = new();
    }
    public sealed record UserCancelled : RecordingError
    {
        public static readonly UserCancelled Instance = new();
    }
    public sealed record DiskFull : RecordingError
    {
        public static readonly DiskFull Instance = new();
    }
    public sealed record SystemFailure(string Message) : RecordingError;
}
