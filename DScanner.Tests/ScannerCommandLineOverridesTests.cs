using DirectInputWatcher;
using DScanner.Configuration;

namespace DScanner.Tests;

public sealed class ScannerCommandLineOverridesTests
{
    [Fact]
    public void ApplyTo_OverridesOnlySpecifiedValues()
    {
        DirectInputWatcherOptions options = new();
        ScannerCommandLineOverrides overrides = new(
            PollFrequencyHz: 20,
            AxisChangeThreshold: 0.30,
            AxisResetThreshold: null);

        overrides.ApplyTo(options);

        Assert.Equal(20, options.PollFrequency);
        Assert.Equal(0.30, options.AxisChangeThreshold);
        Assert.Equal(0.20, options.AxisResetThreshold);
    }
}
