using System.Text;

namespace DScanner.Mapping;

/// <summary>
/// Turns a DirectInput device name into a safe Windows file name.
/// </summary>
public static class DeviceMappingFileName
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Builds the mapping file name for a device, falling back to its instance id when the
    /// device name contains nothing usable.
    /// </summary>
    public static string Create(string deviceName, Guid instanceGuid)
    {
        string sanitized = Sanitize(deviceName);

        if (sanitized.Length == 0 || ReservedNames.Contains(sanitized))
        {
            string fallback = instanceGuid.ToString("N")[..8];
            sanitized = sanitized.Length == 0
                ? $"device-{fallback}"
                : $"{sanitized}-{fallback}";
        }

        return $"{sanitized}.json";
    }

    private static string Sanitize(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(deviceName.Length);
        bool pendingSpace = false;

        foreach (char character in deviceName)
        {
            if (char.IsWhiteSpace(character) || Array.IndexOf(invalid, character) >= 0)
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        // Windows silently strips trailing dots from file names.
        return builder.ToString().TrimEnd('.', ' ');
    }
}
