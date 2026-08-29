using Vortice.DirectInput;

namespace DScanner.DirectInput;

public sealed record DirectInputDeviceDescriptor(
    Guid InstanceGuid,
    Guid ProductGuid,
    string Name,
    DeviceType Type,
    int? VendorId,
    int? ProductId,
    string? InterfacePath);
