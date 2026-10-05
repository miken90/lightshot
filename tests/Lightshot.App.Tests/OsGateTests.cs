using Lightshot.App;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class OsGateTests
{
    [Fact]
    [Unit]
    public void RejectsBuildsBelow22621()
    {
        Assert.False(OsGate.IsBuildSupported(22000), "Build 22000 (Win 11 21H2) must be rejected.");
        Assert.False(OsGate.IsBuildSupported(19045), "Build 19045 (Win 10 22H2) must be rejected.");
        Assert.True(OsGate.IsBuildSupported(22621), "Build 22621 (Win 11 22H2) must be supported.");
        Assert.True(OsGate.IsBuildSupported(22631), "Build 22631 (Win 11 23H2) must be supported.");
        Assert.True(OsGate.IsBuildSupported(26100), "Build 26100 (Win 11 24H2) must be supported.");
        Assert.True(OsGate.IsBuildSupported(26200), "Build 26200 must be supported.");
    }
}
