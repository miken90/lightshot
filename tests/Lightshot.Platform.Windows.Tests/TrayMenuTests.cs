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
}
