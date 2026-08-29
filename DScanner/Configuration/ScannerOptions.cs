namespace DScanner.Configuration;

public sealed class ScannerOptions
{
    public const string SectionName = "Scanner";

    public int PollFrequencyHz { get; set; } = 15;

    public double AxisChangeThreshold { get; set; } = 0.25;

    public double AxisResetThreshold { get; set; } = 0.20;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(1d / PollFrequencyHz);
}
