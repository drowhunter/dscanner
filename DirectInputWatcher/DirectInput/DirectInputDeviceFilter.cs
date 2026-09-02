
namespace DirectInputWatcher;

internal sealed class DirectInputDeviceFilter(
    DirectInputWatcherOptions options)
{
    private readonly HashSet<(int VendorId, int ProductId)> _whitelist =
        options.Whitelist
            .Select(value => (value.VendorId, value.ProductId))
            .ToHashSet();

    private readonly HashSet<(int VendorId, int ProductId)> _blacklist =
        options.Blacklist
            .Select(value => (value.VendorId, value.ProductId))
            .ToHashSet();

    public bool IsAllowed(DirectInputDeviceDescriptor descriptor)
    {
        if (descriptor.VendorId is not int vendorId
            || descriptor.ProductId is not int productId)
        {
            return _whitelist.Count == 0;
        }

        (int VendorId, int ProductId) identity = (vendorId, productId);
        if (_blacklist.Contains(identity))
        {
            return false;
        }

        return _whitelist.Count == 0 || _whitelist.Contains(identity);
    }
}
