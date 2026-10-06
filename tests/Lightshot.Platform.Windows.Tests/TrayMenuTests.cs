// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Runtime.InteropServices;
using Lightshot.Platform.Windows.Tray;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class TrayMenuTests
{
    private const uint MF_BYCOMMAND = 0x00000000;
    private const uint MF_DISABLED = 0x00000002;
    private const uint MF_GRAYED = 0x00000001;

    [DllImport("user32.dll")]
    private static extern uint GetMenuState(IntPtr hMenu, uint uId, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(IntPtr hMenu, uint uId, [Out] System.Text.StringBuilder lpString, int nMaxCount, uint uFlags);

    [Fact]
    [Unit]
    public void HistoryAndSettingsMenuItemsAreEnabled()
    {
        IntPtr hMenu = TrayMenu.CreateMenuHandle();
        Assert.NotEqual(IntPtr.Zero, hMenu);

        try
        {
            // CMD_HISTORY (1006) must be present and neither disabled nor grayed
            uint historyState = GetMenuState(hMenu, TrayMenu.CMD_HISTORY, MF_BYCOMMAND);
            Assert.NotEqual(0xFFFFFFFF, historyState); // Exists in menu
            Assert.Equal(0u, historyState & (MF_DISABLED | MF_GRAYED));

            // CMD_SETTINGS (1007) must be present and neither disabled nor grayed
            uint settingsState = GetMenuState(hMenu, TrayMenu.CMD_SETTINGS, MF_BYCOMMAND);
            Assert.NotEqual(0xFFFFFFFF, settingsState); // Exists in menu
            Assert.Equal(0u, settingsState & (MF_DISABLED | MF_GRAYED));
        }
        finally
        {
            TrayMenu.DestroyMenu(hMenu);
        }
    }

    [Fact]
    [Unit]
    public void RecordingMenuShowsTimerRowAndStop()
    {
        IntPtr hMenu = TrayMenu.CreateMenuHandle(null, null, new RecordingMenuState(true, "00:12"));
        Assert.NotEqual(IntPtr.Zero, hMenu);

        try
        {
            uint timerState = GetMenuState(hMenu, TrayMenu.CMD_RECORDING_TIMER, MF_BYCOMMAND);
            Assert.NotEqual(0xFFFFFFFF, timerState);
            Assert.NotEqual(0u, timerState & MF_GRAYED);

            var sb = new System.Text.StringBuilder(256);
            GetMenuStringW(hMenu, TrayMenu.CMD_RECORDING_TIMER, sb, sb.Capacity, MF_BYCOMMAND);
            Assert.Equal("Recording 00:12", sb.ToString());

            uint stopState = GetMenuState(hMenu, TrayMenu.CMD_STOP_RECORDING, MF_BYCOMMAND);
            Assert.NotEqual(0xFFFFFFFF, stopState);
            Assert.Equal(0u, stopState & (MF_DISABLED | MF_GRAYED));

            uint recordScreenState = GetMenuState(hMenu, TrayMenu.CMD_RECORD_SCREEN, MF_BYCOMMAND);
            Assert.Equal(0xFFFFFFFF, recordScreenState);
        }
        finally
        {
            TrayMenu.DestroyMenu(hMenu);
        }
    }

    [Fact]
    [Unit]
    public void IdleMenuShowsRecordScreenWithChord()
    {
        var binding = new Lightshot.Core.HotkeyBinding(0x52, Lightshot.Core.HotkeyModifiers.Control | Lightshot.Core.HotkeyModifiers.Shift, "Ctrl+Shift+R");
        var assignments = new System.Collections.Generic.Dictionary<Lightshot.Core.CaptureAction, Lightshot.Core.HotkeyBinding>
        {
            [Lightshot.Core.CaptureAction.RecordScreen] = binding
        };
        var bindings = new Lightshot.Core.HotkeyBindings(assignments);

        IntPtr hMenu = TrayMenu.CreateMenuHandle(null, bindings, null);
        Assert.NotEqual(IntPtr.Zero, hMenu);

        try
        {
            uint recState = GetMenuState(hMenu, TrayMenu.CMD_RECORD_SCREEN, MF_BYCOMMAND);
            Assert.NotEqual(0xFFFFFFFF, recState);

            var sb = new System.Text.StringBuilder(256);
            GetMenuStringW(hMenu, TrayMenu.CMD_RECORD_SCREEN, sb, sb.Capacity, MF_BYCOMMAND);
            string menuText = sb.ToString();
            Assert.EndsWith("\t" + Lightshot.Platform.Windows.Hotkeys.VirtualKeyNames.FormatChord(binding), menuText);

            uint timerState = GetMenuState(hMenu, TrayMenu.CMD_RECORDING_TIMER, MF_BYCOMMAND);
            Assert.Equal(0xFFFFFFFF, timerState);

            uint stopState = GetMenuState(hMenu, TrayMenu.CMD_STOP_RECORDING, MF_BYCOMMAND);
            Assert.Equal(0xFFFFFFFF, stopState);
        }
        finally
        {
            TrayMenu.DestroyMenu(hMenu);
        }
    }
}
