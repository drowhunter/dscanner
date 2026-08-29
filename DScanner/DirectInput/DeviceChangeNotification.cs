namespace DScanner.DirectInput;

public enum DeviceChangeKind
{
    InitialScan,
    Created,
    Deleted,
    Recovery
}

public sealed record DeviceChangeNotification(
    DeviceChangeKind Kind,
    string? DeviceName = null,
    string? DevicePath = null,
    string? ControllerPath = null,
    int? VendorId = null,
    int? ProductId = null);
