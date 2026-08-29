namespace DScanner.DirectInput;

public static class DirectInputDeviceLabel
{
    public static string Format(string deviceName, Guid instanceGuid) =>
        $"{deviceName} {FormatIdentifier(instanceGuid)}";

    public static string FormatIdentifier(Guid instanceGuid) =>
        $"[ID {instanceGuid.ToString("N")[..8]}]";
}
