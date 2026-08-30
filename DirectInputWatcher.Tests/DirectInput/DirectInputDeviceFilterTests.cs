using Vortice.DirectInput;

namespace DirectInputWatcher.Tests;

public sealed class DirectInputDeviceFilterTests
{
    [Fact]
    public void BlacklistWinsOverWhitelist()
    {
        DirectInputWatcherOptions options = new()
        {
            Whitelist = [new VidPid(0x1234, 0x5678)],
            Blacklist = [new VidPid(0x1234, 0x5678)]
        };
        DirectInputDeviceFilter filter = new(options);

        Assert.False(filter.IsAllowed(Device(0x1234, 0x5678)));
    }

    [Fact]
    public void NonEmptyWhitelistRejectsUnlistedAndUnknownDevices()
    {
        DirectInputWatcherOptions options = new()
        {
            Whitelist = [new VidPid(0x1234, 0x5678)]
        };
        DirectInputDeviceFilter filter = new(options);

        Assert.True(filter.IsAllowed(Device(0x1234, 0x5678)));
        Assert.False(filter.IsAllowed(Device(0x9999, 0x0001)));
        Assert.False(filter.IsAllowed(Device(null, null)));
    }

    [Fact]
    public void EmptyWhitelistAllowsDevicesUnlessBlacklisted()
    {
        DirectInputWatcherOptions options = new()
        {
            Blacklist = [new VidPid(0x1234, 0x5678)]
        };
        DirectInputDeviceFilter filter = new(options);

        Assert.False(filter.IsAllowed(Device(0x1234, 0x5678)));
        Assert.True(filter.IsAllowed(Device(0x9999, 0x0001)));
        Assert.True(filter.IsAllowed(Device(null, null)));
    }

    private static DirectInputDeviceDescriptor Device(
        int? vendorId,
        int? productId) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Controller",
            DeviceType.Joystick,
            vendorId,
            productId,
            null);
}
