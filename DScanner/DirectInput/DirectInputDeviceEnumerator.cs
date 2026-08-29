using Vortice.DirectInput;

namespace DScanner.DirectInput;

public sealed class DirectInputDeviceEnumerator
{
    public IReadOnlyList<DirectInputDeviceDescriptor> Enumerate()
    {
        using IDirectInput8 directInput = DInput.DirectInput8Create();
        return directInput
            .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly)
            .GroupBy(device => device.InstanceGuid)
            .Select(group => group.First())
            .Select(instance =>
            {
                (int? vendorId, int? productId) = GetVidPid(instance.ProductGuid);
                return new DirectInputDeviceDescriptor(
                    instance.InstanceGuid,
                    instance.ProductGuid,
                    GetDisplayName(instance),
                    instance.Type,
                    vendorId,
                    productId,
                    InterfacePath: null);
            })
            .ToArray();
    }

    public static (int? VendorId, int? ProductId) GetVidPid(Guid productGuid)
    {
        byte[] bytes = productGuid.ToByteArray();
        int vendorId = BitConverter.ToUInt16(bytes, 0);
        int productId = BitConverter.ToUInt16(bytes, 2);
        return vendorId == 0 && productId == 0
            ? (null, null)
            : (vendorId, productId);
    }

    private static string GetDisplayName(DeviceInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.ProductName))
        {
            return instance.ProductName;
        }

        return !string.IsNullOrWhiteSpace(instance.InstanceName)
            ? instance.InstanceName
            : instance.InstanceGuid.ToString();
    }
}
