using System.Globalization;
using System.Text.RegularExpressions;

namespace DirectInputWatcher;

internal static partial class VidPidParser
{
    internal static bool TryParse(
        string deviceId,
        out int vendorId,
        out int productId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        Match match = VidPidPattern().Match(deviceId);
        if (match.Success
            && int.TryParse(match.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vendorId)
            && int.TryParse(match.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out productId))
        {
            return true;
        }

        vendorId = 0;
        productId = 0;
        return false;
    }

    [GeneratedRegex(
        @"VID_(?<vid>[0-9A-F]{4}).*PID_(?<pid>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VidPidPattern();
}
