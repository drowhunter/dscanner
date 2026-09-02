namespace DScanner.Mapping;

/// <summary>
/// Settings for a mapping session, populated from the command line.
/// </summary>
public sealed class DeviceMappingSettings
{
    /// <summary>
    /// Gets or sets the directory mapping files are written to.
    /// Defaults to the current working directory.
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Gets or sets an explicit mapping file path, overriding the device-derived name.
    /// Useful when two identical controllers would otherwise share one file.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets how long to ignore input after a capture, so that releasing a button or
    /// recentring an axis is not mistaken for the next binding.
    /// </summary>
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromMilliseconds(300);
}
