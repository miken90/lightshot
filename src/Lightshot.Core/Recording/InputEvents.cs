// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Core;

/// <summary>
/// A pointer event the recorder's overlays react to.
/// </summary>
public abstract record PointerEvent
{
    public sealed record Moved(Point Position) : PointerEvent;
    public sealed record Down(Point Position) : PointerEvent;
}

/// <summary>
/// The modifier keys held with a key press.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1 << 0,
    Option = 1 << 1,
    Shift = 1 << 2,
    Command = 1 << 3,
    Function = 1 << 4
}

public static class KeyModifiersExtensions
{
    public static string Glyphs(this KeyModifiers modifiers)
    {
        string result = "";
        if (modifiers.HasFlag(KeyModifiers.Function)) result += "fn";
        if (modifiers.HasFlag(KeyModifiers.Control)) result += "⌃";
        if (modifiers.HasFlag(KeyModifiers.Option)) result += "⌥";
        if (modifiers.HasFlag(KeyModifiers.Shift)) result += "⇧";
        if (modifiers.HasFlag(KeyModifiers.Command)) result += "⌘";
        return result;
    }

    public static bool IsCommandChord(this KeyModifiers modifiers) =>
        (modifiers & (KeyModifiers.Command | KeyModifiers.Control | KeyModifiers.Option)) != 0;
}

/// <summary>
/// One key going down, already resolved to what the overlay prints.
/// </summary>
public readonly record struct KeyPress(string Label, KeyModifiers Modifiers = KeyModifiers.None, bool IsRepeat = false)
{
    public KeyPress(string label, bool isRepeat) : this(label, KeyModifiers.None, isRepeat) { }
    public string Text => Modifiers.Glyphs() + Label;
}

/// <summary>
/// A keyboard event from the OS tap.
/// </summary>
public abstract record KeyEvent
{
    public sealed record KeyDown(KeyPress Press) : KeyEvent;
    public sealed record ModifiersChanged(KeyModifiers Modifiers) : KeyEvent;
    public sealed record SecureInput(bool On) : KeyEvent;
}

/// <summary>
/// Any input event the recorder's overlays consume.
/// </summary>
public abstract record InputEvent
{
    public sealed record Pointer(PointerEvent Event) : InputEvent;
    public sealed record Key(KeyEvent Event) : InputEvent;
}

/// <summary>
/// The OS input seam: streams input events while a take records.
/// </summary>
public interface IInputEventSource
{
    void Start(Action<InputEvent> onEvent);
    void Stop();
}
