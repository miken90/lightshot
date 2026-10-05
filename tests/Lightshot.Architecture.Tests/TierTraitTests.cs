using System.Reflection;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Architecture.Tests;

public class TierTraitTests
{
    [Fact]
    [Unit]
    public void UnitAttributeSetsTierTrait()
    {
        var method = typeof(TierTraitTests).GetMethod(nameof(UnitAttributeSetsTierTrait));
        Assert.NotNull(method);

        var unitAttr = method.GetCustomAttribute<UnitAttribute>();
        Assert.NotNull(unitAttr);
        Assert.Equal("Unit", unitAttr.TierName);

        // Verify other trait attributes
        var renderAttr = new RenderAttribute();
        Assert.Equal("Render", renderAttr.TierName);

        var gpuAttr = new GpuAttribute();
        Assert.Equal("Gpu", gpuAttr.TierName);

        var mediaAttr = new MediaAttribute();
        Assert.Equal("Media", mediaAttr.TierName);

        var desktopAttr = new DesktopAttribute();
        Assert.Equal("Desktop", desktopAttr.TierName);
    }
}
