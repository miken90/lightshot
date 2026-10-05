using System;
using System.Collections.Generic;
using Lightshot.Core;

namespace Lightshot.Platform.Windows.Hotkeys;

/// <summary>
/// Maps Win32 Virtual Key codes and modifiers to and from human-readable labels and Core domain models.
/// </summary>
public static class VirtualKeyNames
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const ushort VK_BACK = 0x08;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_SPACE = 0x20;
    public const ushort VK_PRIOR = 0x21; // PageUp
    public const ushort VK_NEXT = 0x22;  // PageDown
    public const ushort VK_END = 0x23;
    public const ushort VK_HOME = 0x24;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_SNAPSHOT = 0x2C; // PrintScreen
    public const ushort VK_INSERT = 0x2D;
    public const ushort VK_DELETE = 0x2E;

    private static readonly Dictionary<ushort, string> s_vkToName = new()
    {
        [VK_SNAPSHOT] = "PrintScreen",
        [VK_ESCAPE] = "Escape",
        [VK_RETURN] = "Return",
        [VK_SPACE] = "Space",
        [VK_TAB] = "Tab",
        [VK_BACK] = "Backspace",
        [VK_DELETE] = "Delete",
        [VK_INSERT] = "Insert",
        [VK_HOME] = "Home",
        [VK_END] = "End",
        [VK_PRIOR] = "PageUp",
        [VK_NEXT] = "PageDown",
        [VK_LEFT] = "Left",
        [VK_UP] = "Up",
        [VK_RIGHT] = "Right",
        [VK_DOWN] = "Down",
    };

    private static readonly Dictionary<string, ushort> s_nameToVk = new(StringComparer.OrdinalIgnoreCase);

    static VirtualKeyNames()
    {
        foreach (var (k, v) in s_vkToName)
        {
            s_nameToVk[v] = k;
        }

        // F1 - F24 (0x70 - 0x87)
        for (int i = 1; i <= 24; i++)
        {
            ushort vk = (ushort)(0x70 + i - 1);
            string name = $"F{i}";
            s_vkToName[vk] = name;
            s_nameToVk[name] = vk;
        }

        // 0 - 9 (0x30 - 0x39)
        for (char c = '0'; c <= '9'; c++)
        {
            ushort vk = (ushort)c;
            string name = c.ToString();
            s_vkToName[vk] = name;
            s_nameToVk[name] = vk;
        }

        // A - Z (0x41 - 0x5A)
        for (char c = 'A'; c <= 'Z'; c++)
        {
            ushort vk = (ushort)c;
            string name = c.ToString();
            s_vkToName[vk] = name;
            s_nameToVk[name] = vk;
        }

        // Aliases
        s_nameToVk["PrtSc"] = VK_SNAPSHOT;
        s_nameToVk["PrintScr"] = VK_SNAPSHOT;
        s_nameToVk["Esc"] = VK_ESCAPE;
        s_nameToVk["Enter"] = VK_RETURN;
    }

    public static string GetKeyName(ushort vk)
    {
        if (s_vkToName.TryGetValue(vk, out var name))
        {
            return name;
        }
        return $"0x{vk:X2}";
    }

    public static ushort? GetVirtualKey(string keyName)
    {
        if (s_nameToVk.TryGetValue(keyName, out var vk))
        {
            return vk;
        }
        if (keyName.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(keyName[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexVk))
        {
            return hexVk;
        }
        return null;
    }

    public static uint ToWin32Modifiers(HotkeyModifiers modifiers, bool noRepeat = true)
    {
        uint flags = noRepeat ? MOD_NOREPEAT : 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) flags |= MOD_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Option)) flags |= MOD_ALT;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) flags |= MOD_SHIFT;
        if (modifiers.HasFlag(HotkeyModifiers.Command)) flags |= MOD_WIN;
        return flags;
    }

    public static HotkeyModifiers FromWin32Modifiers(uint fsModifiers)
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if ((fsModifiers & MOD_CONTROL) != 0) modifiers |= HotkeyModifiers.Control;
        if ((fsModifiers & MOD_ALT) != 0) modifiers |= HotkeyModifiers.Option;
        if ((fsModifiers & MOD_SHIFT) != 0) modifiers |= HotkeyModifiers.Shift;
        if ((fsModifiers & MOD_WIN) != 0) modifiers |= HotkeyModifiers.Command;
        return modifiers;
    }

    public static string FormatChord(HotkeyBinding binding)
    {
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Option)) parts.Add("Alt");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Command)) parts.Add("Win");
        parts.Add(binding.KeyLabel);
        return string.Join("+", parts);
    }
}
