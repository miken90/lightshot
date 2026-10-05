using Lightshot.Platform.Windows.Hotkeys;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class PrintScreenClaimTests
{
    [Fact]
    [Unit]
    public void ProbesSnippingToolSettingWithoutCrashing()
    {
        int? setting = PrintScreenClaim.GetSnippingToolSetting();
        // Should return an int (0 or 1) or null if registry key not set
        if (setting.HasValue)
        {
            Assert.True(setting.Value == 0 || setting.Value == 1);
        }
    }

    [Fact]
    [Unit]
    public void CheckClaimReturnsDetailedResult()
    {
        var result = PrintScreenClaim.CheckClaim();
        Assert.NotNull(result);
        Assert.NotNull(result.Details);
        Assert.NotNull(result.ExactSettingNeeded);
    }
}
