using Vortice.DirectInput;

namespace DirectInputWatcher.Tests;

public sealed class DirectInputDeviceCacheTests
{
    [Fact]
    public void CacheIsDisabledWhenPathIsNotConfigured()
    {
        DirectInputDeviceCache cache =
            new(new DirectInputWatcherOptions());

        cache.Save([Device()]);

        Assert.Empty(cache.Load());
    }

    [Fact]
    public void CacheRoundTripsConfiguredDescriptors()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"DirectInputWatcher-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "devices.json");

        try
        {
            DirectInputDeviceCache cache = new(
                new DirectInputWatcherOptions
                {
                    DeviceCachePath = path
                });
            DirectInputDeviceDescriptor device = Device();

            cache.Save([device]);

            Assert.Equal([device], cache.Load());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    private static DirectInputDeviceDescriptor Device() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Controller",
            DeviceType.Joystick,
            0x1234,
            0x5678,
            null);
}
