// Ported from LightshotKit/Sources/LightshotKit/HotkeyBinding.swift
// MIT License, Copyright (c) 2026 Viet Le

namespace Lightshot.Core;

/// <summary>
/// The OS global hotkey registration seam.
/// </summary>
public interface IHotkeyService
{
    void Register(HotkeyBindings bindings);
}
