using Lightshot.Platform.Windows;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class DesktopSessionTests
{
    [Fact]
    [Desktop]
    public void RunsInInteractiveSession()
    {
        bool isInteractive = DesktopSession.IsInteractiveSession();
        Assert.True(isInteractive, "Expected test to run in an active interactive Windows session.");
    }
}
