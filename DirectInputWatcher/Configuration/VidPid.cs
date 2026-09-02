using System.ComponentModel;
using System.Globalization;

namespace DirectInputWatcher;

[TypeConverter(typeof(VidPidTypeConverter))]
public sealed record VidPid
{
    public VidPid()
    {
    }

    public VidPid(int vendorId, int productId)
    {
        VendorId = vendorId;
        ProductId = productId;
    }

    public int VendorId { get; set; }

    public int ProductId { get; set; }

    public override string ToString() =>
        $"{VendorId.ToString("X4", CultureInfo.InvariantCulture)}:{ProductId.ToString("X4", CultureInfo.InvariantCulture)}";

    public static VidPid Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string[] parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !TryParseHex(parts[0], out int vendorId)
            || !TryParseHex(parts[1], out int productId))
        {
            throw new FormatException(
                $"'{value}' is not a VID:PID value such as 346E:0003.");
        }

        return new VidPid(vendorId, productId);
    }

    private static bool TryParseHex(string value, out int result)
    {
        string normalized = value.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;
        return int.TryParse(
            normalized,
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out result)
            && result <= ushort.MaxValue;
    }
}

internal sealed class VidPidTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(
        ITypeDescriptorContext? context,
        Type sourceType) =>
        sourceType == typeof(string)
        || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value) =>
        value is string text
            ? VidPid.Parse(text)
            : base.ConvertFrom(context, culture, value);
}
