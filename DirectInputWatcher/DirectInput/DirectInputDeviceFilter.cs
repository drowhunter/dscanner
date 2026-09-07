
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

    /// <summary>
    /// Determines whether the specified DirectInput device is allowed based on the
    /// configured whitelist and blacklist of vendor/product ID pairs.
    /// </summary>
    /// <remarks>
    /// A device is disallowed if its vendor/product ID pair is present in the blacklist,
    /// regardless of whitelist configuration. If the descriptor does not expose both a
    /// vendor ID and a product ID, the device is allowed only when no whitelist has been
    /// configured. When a whitelist is configured, only devices whose vendor/product ID
    /// pair is present in the whitelist are allowed.
    /// </remarks>
    /// <param name="descriptor">The descriptor of the device to evaluate.</param>
    /// <returns>
    /// <see langword="true"/> if the device is allowed; otherwise, <see langword="false"/>.
    /// </returns>
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
