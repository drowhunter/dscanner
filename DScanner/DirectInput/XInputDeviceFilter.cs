using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DScanner.DirectInput;

public sealed partial class XInputDeviceFilter(ILogger<XInputDeviceFilter> logger)
{
    public IReadOnlySet<(int VendorId, int ProductId)> GetXInputVidPidPairs()
    {
        HashSet<(int VendorId, int ProductId)> pairs = [];

        try
        {
            using ManagementObjectSearcher searcher =
                new("SELECT PNPDeviceID FROM Win32_PnPEntity");
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject result in results)
            {
                string? deviceId = result["PNPDeviceID"] as string;
                if (deviceId is null
                    || !deviceId.Contains("IG_", StringComparison.OrdinalIgnoreCase)
                    || !TryParseVidPid(deviceId, out int vendorId, out int productId))
                {
                    continue;
                }

                pairs.Add((vendorId, productId));
            }
        }
        catch (ManagementException exception)
        {
            logger.LogWarning(exception, "Could not query Windows PnP metadata for XInput devices.");
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Access was denied while querying Windows PnP metadata for XInput devices.");
        }
        catch (COMException exception)
        {
            logger.LogWarning(exception, "Windows PnP metadata could not be read for XInput filtering.");
        }

        return pairs;
    }

    public static bool TryParseVidPid(string deviceId, out int vendorId, out int productId)
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
