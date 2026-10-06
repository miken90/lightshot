// Ported from LightshotKit/Sources/LightshotKit/InputEventSource.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Text;
using Lightshot.Core;
using Windows.Win32;

namespace Lightshot.Platform.Windows.Input;

/// <summary>
/// Translates Win32 virtual keys and modifier state into Core KeystrokeOverlay KeyPress models.
/// Uses unmodified characters (via ToUnicodeEx with no-state-change flag) so Shift+1 displays as "Shift 1" (⇧1).
/// </summary>
public static class KeyTranslator
{
    private static readonly Dictionary<int, string> s_namedKeys = new()
    {
        [0x0D] = "↩",      // VK_RETURN
        [0x09] = "⇥",      // VK_TAB
        [0x20] = "Space",  // VK_SPACE
        [0x08] = "⌫",      // VK_BACK
        [0x2E] = "⌦",      // VK_DELETE
        [0x1B] = "⎋",      // VK_ESCAPE
        [0x0C] = "⌧",      // VK_CLEAR
        [0x25] = "←",      // VK_LEFT
        [0x26] = "↑",      // VK_UP
        [0x27] = "→",      // VK_RIGHT
        [0x28] = "↓",      // VK_DOWN
        [0x24] = "↖",      // VK_HOME
        [0x23] = "↘",      // VK_END
        [0x21] = "⇞",      // VK_PRIOR (PageUp)
        [0x22] = "⇟",      // VK_NEXT (PageDown)
        [0x2D] = "Insert", // VK_INSERT
    };

    static KeyTranslator()
    {
        // F1..F24 (0x70..0x87)
        for (int i = 1; i <= 24; i++)
        {
            s_namedKeys[0x70 + i - 1] = $"F{i}";
        }
    }

    public static bool IsModifierKey(int vkCode) => vkCode switch
    {
        0x10 or 0xA0 or 0xA1 => true, // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
        0x11 or 0xA2 or 0xA3 => true, // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
        0x12 or 0xA4 or 0xA5 => true, // VK_MENU, VK_LMENU, VK_RMENU (Alt)
        0x5B or 0x5C => true,         // VK_LWIN, VK_RWIN
        _ => false
    };

    public static KeyPress? Translate(
        int vkCode,
        KeyModifiers modifiers = KeyModifiers.None,
        uint scanCode = 0,
        IntPtr keyboardLayout = default,
        bool isRepeat = false)
    {
        if (IsModifierKey(vkCode))
        {
            return null;
        }

        string? label = GetLabel(vkCode, scanCode, keyboardLayout);
        if (string.IsNullOrEmpty(label))
        {
            return null;
        }

        return new KeyPress(label, modifiers, isRepeat);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        IntPtr dwhkl);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    public static string? GetLabel(int vkCode, uint scanCode = 0, IntPtr keyboardLayout = default)
    {
        if (s_namedKeys.TryGetValue(vkCode, out var named))
        {
            return named;
        }

        // Numpad math operators
        switch (vkCode)
        {
            case 0x6A: return "*";
            case 0x6B: return "+";
            case 0x6D: return "-";
            case 0x6E: return ".";
            case 0x6F: return "/";
        }

        // Numpad 0-9 (0x60-0x69)
        if (vkCode >= 0x60 && vkCode <= 0x69)
        {
            return (vkCode - 0x60).ToString();
        }

        try
        {
            var hkl = keyboardLayout == IntPtr.Zero
                ? GetKeyboardLayout(0)
                : keyboardLayout;

            // Empty key state array ensures unmodified characters (Shift+1 -> '1', not '!')
            var emptyKeyState = new byte[256];
            var sb = new System.Text.StringBuilder(16);

            // wFlags = 4: TM_POSTCHARBREAKS / do not change keyboard state (Windows 10 1607+)
            int ret = ToUnicodeEx((uint)vkCode, scanCode, emptyKeyState, sb, sb.Capacity, 4, hkl);
            if (ret > 0 && sb.Length > 0)
            {
                int first = char.ConvertToUtf32(sb.ToString(), 0);
                if (first >= 0x20 && !(first >= 0xF700 && first <= 0xF8FF))
                {
                    return char.ConvertFromUtf32(first).ToUpperInvariant();
                }
            }
        }
        catch
        {
            // Degrade gracefully to ASCII fallback
        }

        // ASCII 0-9 and A-Z fallback
        if ((vkCode >= '0' && vkCode <= '9') || (vkCode >= 'A' && vkCode <= 'Z'))
        {
            return ((char)vkCode).ToString();
        }

        return null;
    }
}
