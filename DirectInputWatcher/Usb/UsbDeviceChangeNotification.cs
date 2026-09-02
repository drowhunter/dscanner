namespace DirectInputWatcher;

public enum UsbDeviceChangeKind
{
    Created,
    Deleted
}

internal sealed record UsbDeviceChangeNotification(
    UsbDeviceChangeKind? Kind = null,
    string? DeviceName = null,
    string? DevicePath = null,
    string? ControllerPath = null,
    int? VendorId = null,
    int? ProductId = null,
    Exception? Error = null);
