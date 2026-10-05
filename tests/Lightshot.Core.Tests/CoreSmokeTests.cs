using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Core.Tests;

public class CoreSmokeTests
{
    [Fact]
    [Unit]
    public void CoreInitializesSuccessfully()
    {
        Assert.Equal("Lightshot.Core", CoreMarker.ProjectName);
    }
}
