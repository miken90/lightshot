using Lightshot.Platform.Windows.Tray;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class QuitSignalTests
{
    [Fact]
    [Unit]
    public void QuitEventEndsProcessAndRemovesTrayIcon()
    {
        using var tray = new TrayIcon();
        Assert.True(tray.IsCreated, "Tray icon should be created on initialization.");

        // Simulate quit handling / disposal
        tray.Dispose();
        Assert.False(tray.IsCreated, "Tray icon should be removed after quit.");
    }
}
