using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lightshot.Core;
using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.Platform.Windows.Tray;

public record DisplayMenuItem(uint DisplayId, string DisplayName);

/// <summary>
/// Native Win32 popup menu for the system tray icon, rebuilt on every open
/// with live hotkey chords, per-display fullscreen submenu, and disabled placeholders.
/// </summary>
public static class TrayMenu
{
    private const uint MF_STRING = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DISABLED = 0x00000002;
    private const uint MF_POPUP = 0x00000010;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    public const uint CMD_AREA = 1001;
    public const uint CMD_FULLSCREEN = 1002;
    public const uint CMD_WINDOW = 1003;
    public const uint CMD_REPEAT_LAST = 1004;
    public const uint CMD_OPEN_FILE = 1005;
    public const uint CMD_HISTORY = 1006;
    public const uint CMD_SETTINGS = 1007;
    public const uint CMD_QUIT = 1008;
    public const uint CMD_DISPLAY_BASE = 2000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

    public static uint Show(
        IntPtr hWnd,
        int x,
        int y,
        HotkeyBindings? bindings = null,
        IReadOnlyList<DisplayMenuItem>? displays = null)
    {
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return 0;

        try
        {
            // 1. Area Capture
            string areaChord = GetChordSuffix(bindings, CaptureAction.Area);
            AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_AREA), $"Capture Area{areaChord}");

            // 2. Fullscreen Capture
            string fsChord = GetChordSuffix(bindings, CaptureAction.Fullscreen);
            if (displays != null && displays.Count > 1)
            {
                IntPtr hSubMenu = CreatePopupMenu();
                AppendMenuW(hSubMenu, MF_STRING, new UIntPtr(CMD_FULLSCREEN), "All Displays");
                for (int i = 0; i < displays.Count; i++)
                {
                    uint cmd = (uint)(CMD_DISPLAY_BASE + i);
                    AppendMenuW(hSubMenu, MF_STRING, new UIntPtr(cmd), displays[i].DisplayName);
                }
                AppendMenuW(hMenu, MF_POPUP, (UIntPtr)(ulong)hSubMenu, $"Capture Fullscreen{fsChord}");
            }
            else
            {
                AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_FULLSCREEN), $"Capture Fullscreen{fsChord}");
            }

            // 3. Window Capture
            string winChord = GetChordSuffix(bindings, CaptureAction.Window);
            AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_WINDOW), $"Capture Window{winChord}");

            // 4. Repeat Last
            string repChord = GetChordSuffix(bindings, CaptureAction.RepeatLast);
            AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_REPEAT_LAST), $"Repeat Last Capture{repChord}");

            // 5. Open image
            AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_OPEN_FILE), "Open Image...");

            // Separator
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // History
            AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_HISTORY), "History");

            // Settings
            AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_SETTINGS), "Settings...");

            // Separator
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);

            // Quit
            AppendMenuW(hMenu, MF_STRING, new UIntPtr(CMD_QUIT), "Quit Lightshot");

            // Set foreground window before TrackPopupMenuEx to ensure proper menu dismissal
            Win32Window.SetForegroundWindow(hWnd);

            uint selected = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, hWnd, IntPtr.Zero);
            return selected;
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private static string GetChordSuffix(HotkeyBindings? bindings, CaptureAction action)
    {
        if (bindings == null) return string.Empty;
        var binding = bindings[action];
        if (binding == null) return string.Empty;
        string chord = VirtualKeyNames.FormatChord(binding.Value);
        return string.IsNullOrEmpty(chord) ? string.Empty : $"\t{chord}";
    }
}
