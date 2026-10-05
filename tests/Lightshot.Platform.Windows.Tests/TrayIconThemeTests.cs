// MIT License, Copyright (c) 2026 Viet Le

using Lightshot.Platform.Windows.Tray;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class TrayIconThemeTests
{
    [Fact]
    [Unit]
    public void ChosenFilePerThemeValue()
    {
        // Light taskbar (SystemUsesLightTheme == true) needs dark ink (tray-light.ico)
        Assert.Equal("tray-light.ico", TrayIconAssets.ChooseTrayIconFileName(true));
        Assert.Equal("Resources/Icons/Tray/tray-light.ico", TrayIconAssets.ChooseTrayIconResourcePath(true));

        // Dark taskbar (SystemUsesLightTheme == false) needs light ink (tray-dark.ico)
        Assert.Equal("tray-dark.ico", TrayIconAssets.ChooseTrayIconFileName(false));
        Assert.Equal("Resources/Icons/Tray/tray-dark.ico", TrayIconAssets.ChooseTrayIconResourcePath(false));
    }
}
