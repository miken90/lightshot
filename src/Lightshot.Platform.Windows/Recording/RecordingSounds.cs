// Ported from App/Sources/RecordingSounds.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;

namespace Lightshot.Platform.Windows.Recording;

public enum RecordingCue
{
    Tick,
    Start,
    Stop,
    Pause
}

/// <summary>
/// Audible cues for recording countdown and state transitions using Windows SystemSounds.
/// Honours the user's Windows sound scheme without bundled audio assets.
/// </summary>
public static class RecordingSounds
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint uType);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONEXCLAMATION = 0x00000030;
    private const uint MB_ICONASTERISK = 0x00000040;
    private const uint SIMPLE_BEEP = 0xFFFFFFFF;

    public static void Play(RecordingCue cue, bool enabled)
    {
        if (!enabled) return;

        try
        {
            switch (cue)
            {
                case RecordingCue.Tick:
                    MessageBeep(MB_OK);
                    break;
                case RecordingCue.Start:
                    MessageBeep(MB_ICONASTERISK);
                    break;
                case RecordingCue.Stop:
                    MessageBeep(SIMPLE_BEEP);
                    break;
                case RecordingCue.Pause:
                    MessageBeep(MB_ICONEXCLAMATION);
                    break;
            }
        }
        catch
        {
            // Sound scheme or audio output errors should never fail recording flow
        }
    }
}
