// Ported from LightshotKit/Sources/LightshotKit/KeystrokeOverlay.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;

namespace Lightshot.Core;

/// <summary>
/// Turns a virtual key code into the label displayed for it.
/// </summary>
public static class KeyLabel
{
    private static readonly Dictionary<int, string> Named = new()
    {
        [36] = "↩", [76] = "⌤", [48] = "⇥", [49] = "Space", [51] = "⌫", [117] = "⌦", [53] = "⎋", [71] = "⌧",
        [123] = "←", [124] = "→", [125] = "↓", [126] = "↑", [115] = "↖", [119] = "↘", [116] = "⇞", [121] = "⇟",
        [122] = "F1", [120] = "F2", [99] = "F3", [118] = "F4", [96] = "F5", [97] = "F6", [98] = "F7", [100] = "F8",
        [101] = "F9", [109] = "F10", [103] = "F11", [111] = "F12",
        [114] = "?⃝"
    };

    private static readonly HashSet<string> NamedLabels = new(Named.Values);

    public static bool IsNamed(string label) => NamedLabels.Contains(label);

    public static string? Label(int keyCode, string? characters)
    {
        if (Named.TryGetValue(keyCode, out string? name))
        {
            return name;
        }

        if (string.IsNullOrEmpty(characters))
        {
            return null;
        }

        int first = char.ConvertToUtf32(characters, 0);
        if (first < 0x20 || (first >= 0xF700 && first <= 0xF8FF))
        {
            return null;
        }

        return char.ConvertFromUtf32(first).ToUpperInvariant();
    }
}
