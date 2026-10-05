using Lightshot.Rendering;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Rendering.Tests;

public class RenderingSmokeTests
{
    [Fact]
    [Render]
    public void RenderingInitializesSuccessfully()
    {
        Assert.Equal("Lightshot.Rendering", RenderingMarker.ProjectName);
    }
}
