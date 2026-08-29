namespace DScanner.Configuration;

public sealed record ScannerCommandLineOverrides(
    int? PollFrequencyHz,
    double? AxisChangeThreshold,
    double? AxisResetThreshold)
{
    public void ApplyTo(ScannerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (PollFrequencyHz.HasValue)
        {
            options.PollFrequencyHz = PollFrequencyHz.Value;
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
}
