using System.Threading.Tasks;
using Lightshot.Platform.Windows.Audio;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class AudioDeviceServiceTests
{
    [Fact]
    [Unit]
    public void RemovedActiveDeviceRaisesAudioSourceLost()
    {
        using var service = new AudioDeviceService();
        service.ActiveDeviceId = "{0.0.1.00000000}.{mic-endpoint-123}";

        bool sourceLost = false;
        service.AudioSourceLost += () => sourceLost = true;

        var client = (IMMNotificationClient)service;
        client.OnDeviceRemoved("{0.0.1.00000000}.{mic-endpoint-123}");

        Assert.True(sourceLost, "Removing the active microphone must trigger AudioSourceLost.");
    }

    [Fact]
    [Unit]
    public void DisconnectedStateOnActiveDeviceRaisesAudioSourceLost()
    {
        using var service = new AudioDeviceService();
        service.ActiveDeviceId = "{0.0.1.00000000}.{mic-endpoint-456}";

        bool sourceLost = false;
        service.AudioSourceLost += () => sourceLost = true;

        var client = (IMMNotificationClient)service;
        // DeviceState: 1 = Active, 2 = Disabled, 4 = NotPresent, 8 = Unplugged
        client.OnDeviceStateChanged("{0.0.1.00000000}.{mic-endpoint-456}", 8);

        Assert.True(sourceLost, "Unplugging the active microphone must trigger AudioSourceLost.");
    }

    [Fact]
    [Unit]
    public void RemovedInactiveDeviceDoesNotRaiseAudioSourceLost()
    {
        using var service = new AudioDeviceService();
        service.ActiveDeviceId = "{0.0.1.00000000}.{active-mic}";

        bool sourceLost = false;
        service.AudioSourceLost += () => sourceLost = true;

        var client = (IMMNotificationClient)service;
        client.OnDeviceRemoved("{0.0.1.00000000}.{different-unrelated-mic}");

        Assert.False(sourceLost, "Removing an unrelated device must not trigger AudioSourceLost for the active mic.");
    }

    [Fact]
    [Unit]
    public void DevicesChangedFiresOnDeviceAddedOrRemoved()
    {
        using var service = new AudioDeviceService();

        int changeCount = 0;
        service.DevicesChanged += () => changeCount++;

        var client = (IMMNotificationClient)service;
        client.OnDeviceAdded("some-device-id");
        client.OnDeviceRemoved("some-device-id");

        Assert.Equal(2, changeCount);
    }

    [Fact]
    [Unit]
    public async Task AvailableInputsReturnsListOrEmptyWithoutThrowing()
    {
        using var service = new AudioDeviceService();
        var inputs = await service.AvailableInputsAsync();

        Assert.NotNull(inputs);
        // Valid list returned; if hardware is present, each entry has valid properties
        foreach (var dev in inputs)
        {
            Assert.False(string.IsNullOrWhiteSpace(dev.Id));
            Assert.False(string.IsNullOrWhiteSpace(dev.Name));
        }
    }
}
