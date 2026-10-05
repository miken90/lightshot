using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Architecture.Tests;

public class ExitCodeCanaryTests
{
    [Fact]
    [Unit]
    public void FailsWhenCanaryEnabled()
    {
        string? failFlag = Environment.GetEnvironmentVariable("LIGHTSHOT_CANARY_FAIL");
        if (string.Equals(failFlag, "1", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail("Canary failure deliberately triggered by LIGHTSHOT_CANARY_FAIL=1");
        }
    }
}
