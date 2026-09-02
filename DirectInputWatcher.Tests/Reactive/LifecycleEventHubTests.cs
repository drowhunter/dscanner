using System.Reactive.Linq;
using Vortice.DirectInput;

namespace DirectInputWatcher.Tests;

public sealed class LifecycleEventHubTests
{
    [Fact]
    public void LateSubscriberReceivesCurrentDevicesButNotHistoricalEvents()
    {
        using LifecycleEventHub hub = new();
        DirectInputDeviceDescriptor disconnected = Device("Disconnected");
        DirectInputDeviceDescriptor connected = Device("Connected");

        hub.Connect(disconnected, fromCache: false);
        hub.Disconnect(disconnected, "removed");
        hub.Connect(connected, fromCache: true);

        List<DirectInputLifecycleEvent> received = [];
        using IDisposable subscription = hub.Observable.Subscribe(received.Add);

        CurrentDevicesSnapshot snapshot =
            Assert.IsType<CurrentDevicesSnapshot>(Assert.Single(received));
        Assert.Equal([connected], snapshot.Devices);
    }

    [Fact]
    public void SubscriberReceivesSnapshotBeforeLiveEvents()
    {
        using LifecycleEventHub hub = new();
        DirectInputDeviceDescriptor connected = Device("Connected");
        List<DirectInputLifecycleEvent> received = [];

        using IDisposable subscription = hub.Observable.Subscribe(received.Add);
        hub.Connect(connected, fromCache: false);

        Assert.IsType<CurrentDevicesSnapshot>(received[0]);
        DeviceConnected deviceConnected =
            Assert.IsType<DeviceConnected>(received[1]);
        Assert.Equal(connected, deviceConnected.Device);
    }

    private static DirectInputDeviceDescriptor Device(string name) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            DeviceType.Joystick,
            0x1234,
            0x5678,
            null);
}
