using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Lightshot.Platform.Windows.Interop;

namespace Lightshot.Platform.Windows.Hotkeys;

public record PrintScreenClaimResult(
    int? SnippingToolSetting,
    bool RegisterHotKeySuccess,
    int LastWin32Error,
    string Details,
    string ExactSettingNeeded
);

/// <summary>
/// Probes Windows 11 Snipping Tool PrintScreen interception and registry setting.
/// </summary>
public static class PrintScreenClaim
{
    private const uint VK_SNAPSHOT = 0x2C;
    private const int TEST_SNAPSHOT_ID = 0x534E; // 'SN'

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static int? GetSnippingToolSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
            if (key != null)
            {
                object? val = key.GetValue("PrintScreenKeyForSnippingEnabled");
                if (val is int intVal) return intVal;
                if (val is string strVal && int.TryParse(strVal, out int parsed)) return parsed;
            }
        }
        catch
        {
            // Registry read fallback
        }
        return null;
    }

    private static readonly Win32Window.WndProc s_defWndProc = Win32Window.DefWindowProcW;

    public static PrintScreenClaimResult CheckClaim(IntPtr hWnd = default)
    {
        int? snippingEnabled = GetSnippingToolSetting();
        bool ownsWindow = false;
        string? createdClassName = null;

        if (hWnd == IntPtr.Zero)
        {
            string className = $"PrintScreenProbe_{Guid.NewGuid():N}";
            createdClassName = className;
            IntPtr hInst = Win32Window.GetModuleHandleW(null);
            var wc = new Win32Window.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Win32Window.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_defWndProc),
                hInstance = hInst,
                lpszClassName = className
            };
            Win32Window.RegisterClassExW(ref wc);
            hWnd = Win32Window.CreateWindowExW(0, className, "Probe", 0, 0, 0, 0, 0, Win32Window.HWND_MESSAGE, IntPtr.Zero, hInst, IntPtr.Zero);
            ownsWindow = true;
        }

        try
        {
            bool registered = RegisterHotKey(hWnd, TEST_SNAPSHOT_ID, 0, VK_SNAPSHOT);
            int err = registered ? 0 : Marshal.GetLastWin32Error();

            if (registered)
            {
                UnregisterHotKey(hWnd, TEST_SNAPSHOT_ID);
            }

            string details;
            string settingNeeded;
            if (registered)
            {
                details = "VK_SNAPSHOT successfully claimed without OS interception.";
                settingNeeded = "No registry change required; bare PrintScreen hotkey is available.";
            }
            else
            {
                details = $"VK_SNAPSHOT registration failed with Win32 error {err} (0x{err:X8}). SnippingTool claim active.";
                settingNeeded = "Set HKCU\\Control Panel\\Keyboard\\PrintScreenKeyForSnippingEnabled to 0 (DWORD) to release VK_SNAPSHOT for third-party hotkeys, or use default chords (Ctrl+PrintScreen / Ctrl+Shift+PrintScreen).";
            }

            return new PrintScreenClaimResult(
                SnippingToolSetting: snippingEnabled,
                RegisterHotKeySuccess: registered,
                LastWin32Error: err,
                Details: details,
                ExactSettingNeeded: settingNeeded
            );
        }
        finally
        {
            if (ownsWindow)
            {
                if (hWnd != IntPtr.Zero)
                {
                    Win32Window.DestroyWindow(hWnd);
                }
                if (createdClassName != null)
                {
                    IntPtr hInst = Win32Window.GetModuleHandleW(null);
                    Win32Window.UnregisterClassW(createdClassName, hInst);
                }
            }
        }
    }
}
