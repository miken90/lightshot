// Ported from LightshotKit/Sources/LightshotKit/HotkeyBinding.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

/// <summary>
/// The OS global hotkey registration seam.
/// </summary>
public interface IHotkeyService
{
    /// <summary>
    /// Replaces prior registrations with bindings, invoking handler when a chord fires.
    /// Returns actions that could not be registered.
    /// </summary>
    IReadOnlyList<CaptureAction> Register(HotkeyBindings bindings, Action<CaptureAction> handler);

    /// <summary>
    /// Unregisters all global hotkeys currently registered.
    /// </summary>
    void UnregisterAll();

    /// <summary>
    /// Convenience registration without handler.
    /// </summary>
    void Register(HotkeyBindings bindings) => Register(bindings, _ => { });
}
