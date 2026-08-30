namespace DirectInputWatcher;

public abstract record DirectInputLifecycleEvent(DateTimeOffset Timestamp);

public sealed record CurrentDevicesSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<DirectInputDeviceDescriptor> Devices)
    : DirectInputLifecycleEvent(Timestamp);

public sealed record DeviceConnected(
    DateTimeOffset Timestamp,
    DirectInputDeviceDescriptor Device,
    bool FromCache)
    : DirectInputLifecycleEvent(Timestamp);

public sealed record DeviceDisconnected(
    DateTimeOffset Timestamp,
    DirectInputDeviceDescriptor Device,
    string Reason)
    : DirectInputLifecycleEvent(Timestamp);

public sealed record ScanStarted(
    DateTimeOffset Timestamp,
    ScanReason Reason)
    : DirectInputLifecycleEvent(Timestamp);

public sealed record ScanProgress(
    DateTimeOffset Timestamp,
    ScanReason Reason,
    TimeSpan Elapsed)
    : DirectInputLifecycleEvent(Timestamp);

public sealed record ScanCompleted(
    DateTimeOffset Timestamp,
    ScanReason Reason,
    TimeSpan Elapsed,
    int DiscoveredDeviceCount)
    : DirectInputLifecycleEvent(Timestamp);

public sealed record UsbDeviceChanged(
    DateTimeOffset Timestamp,
    UsbDeviceChangeKind Kind,
    string? DeviceName,
    string? DevicePath,
    string? ControllerPath,
    int? VendorId,
    int? ProductId)
    : DirectInputLifecycleEvent(Timestamp);

public sealed record WatcherError(
    DateTimeOffset Timestamp,
    WatcherErrorKind Kind,
    string Message,
    Exception Exception,
    DirectInputDeviceDescriptor? Device = null)
    : DirectInputLifecycleEvent(Timestamp);

public enum ScanReason
{
    Startup,
    UsbDeviceChanged,
    Recovery
}

public enum WatcherErrorKind
{
    UsbWatcher,
    CacheRead,
    CacheWrite,
    Enumeration,
    Acquisition,
    Polling
}
