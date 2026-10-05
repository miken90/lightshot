// Ported from LightshotKit/Sources/LightshotKit/CaptureAction.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// A capture action a global hotkey can be bound to.
/// </summary>
public enum CaptureAction
{
    Area,
    Window,
    Fullscreen,
    RepeatLast,
    RecordScreen,
    CaptureText,
    PauseResumeRecording,
    RestartRecording
}

public static class CaptureActionExtensions
{
    public static string Title(this CaptureAction action) => action switch
    {
        CaptureAction.Area => "Capture Area",
        CaptureAction.Window => "Capture Window",
        CaptureAction.Fullscreen => "Capture Fullscreen",
        CaptureAction.RepeatLast => "Repeat Last Capture",
        CaptureAction.RecordScreen => "Record Screen",
        CaptureAction.CaptureText => "OCR Text",
        CaptureAction.PauseResumeRecording => "Pause/Resume Recording",
        CaptureAction.RestartRecording => "Restart Recording",
        _ => action.ToString()
    };
}
