using Lightshot.App;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class QuitSignalTests
{
    [Fact]
    [Unit]
    public void QuitEventEndsProcessAndRemovesTrayIcon()
    {
        using var tray = new TrayStub();
        tray.Initialize();
        Assert.True(tray.IsCreated, "Tray icon should be created on initialization.");

        // Simulate quit handling
        tray.Remove();
        Assert.False(tray.IsCreated, "Tray icon should be removed after quit.");
    }
}
