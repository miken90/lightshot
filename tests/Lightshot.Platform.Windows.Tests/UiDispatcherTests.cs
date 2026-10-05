using System;
using System.Threading.Tasks;
using Lightshot.Platform.Windows.Shell;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class UiDispatcherTests
{
    [Fact]
    [Unit]
    public void DispatchesSynchronouslyWithoutContext()
    {
        var dispatcher = new UiDispatcher(syncContext: null);
        bool executed = false;
        dispatcher.Dispatch(() => executed = true);
        Assert.True(executed);
    }

    [Fact]
    [Unit]
    public async Task DispatchesAsyncWithoutContext()
    {
        var dispatcher = new UiDispatcher(syncContext: null);
        bool executed = false;
        await dispatcher.DispatchAsync(async () =>
        {
            await Task.Yield();
            executed = true;
        });
        Assert.True(executed);
    }
}
