using DirectInputWatcher;
using DScanner.Mapping;

namespace DScanner.Configuration;

public sealed record ScannerCommandLineOverrides(
    int? PollFrequencyHz,
    double? AxisChangeThreshold,
    double? AxisResetThreshold,
    bool Map = false,
    string? MapOutputDirectory = null,
    string? MapFilePath = null)
{
    public void ApplyTo(DirectInputWatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (PollFrequencyHz.HasValue)
        {
            options.PollFrequency = PollFrequencyHz.Value;
        }

        if (AxisChangeThreshold.HasValue)
        {
            options.AxisChangeThreshold = AxisChangeThreshold.Value;
        }

        if (AxisResetThreshold.HasValue)
        {
            options.AxisResetThreshold = AxisResetThreshold.Value;
        }
    }

    public void ApplyTo(DeviceMappingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.IsNullOrWhiteSpace(MapOutputDirectory))
        {
            settings.OutputDirectory = MapOutputDirectory;
        }

        if (!string.IsNullOrWhiteSpace(MapFilePath))
        {
            settings.FilePath = MapFilePath;
        }
    }
}
