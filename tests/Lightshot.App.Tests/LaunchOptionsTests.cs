using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class LaunchOptionsTests
{
    [Fact]
    [Unit]
    public void No_tray_defaults_off()
    {
        Assert.False(LaunchOptions.Parse([]).NoTray);
        Assert.False(LaunchOptions.Parse(["--background"]).NoTray);
    }

    [Fact]
    [Unit]
    public void No_tray_flag_parsed_case_insensitive()
    {
        Assert.True(LaunchOptions.Parse(["--NO-TRAY"]).NoTray);
        Assert.True(LaunchOptions.Parse(["--background", "--no-tray"]).NoTray);
    }
}
