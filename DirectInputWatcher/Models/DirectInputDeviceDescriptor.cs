using Vortice.DirectInput;

namespace DirectInputWatcher;

public sealed record DirectInputDeviceDescriptor(
    Guid InstanceGuid,
    Guid ProductGuid,
    string Name,
    DeviceType Type,
    int? VendorId,
    int? ProductId,
    string? InterfacePath);
