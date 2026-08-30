namespace DirectInputWatcher;

/// <summary>
/// Configuration options for the DirectInput device watcher, controlling polling frequency,
/// axis change detection thresholds, and device filtering.
/// </summary>
public sealed class DirectInputWatcherOptions
{
    /// <summary>
    /// The default configuration section name used when binding these options from configuration.
    /// </summary>
    public const string DefaultSectionName = "DirectInputWatcher";

    /// <summary>
    /// Gets or sets the polling frequency in Hertz (polls per second).
    /// Must be greater than 0. Default is 15 Hz.
    /// </summary>
    public int PollFrequency { get; set; } = 15;

    /// <summary>
    /// Gets or sets the threshold for detecting significant axis changes.
    /// Value represents the normalized change (0.0 to 2.0) required to trigger a change event.
    /// Must be greater than 0 and less than or equal to 2. Default is 0.25.
    /// </summary>
    public double AxisChangeThreshold { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the threshold for resetting an axis to its baseline state.
    /// Value represents the normalized change required to consider an axis "at rest".
    /// Must be non-negative and less than <see cref="AxisChangeThreshold"/>. Default is 0.20.
    /// </summary>
    public double AxisResetThreshold { get; set; } = 0.20;

    /// <summary>
    /// Gets or sets the duration for collecting baseline calibration samples when a device is first detected.
    /// Must be non-negative. Default is 1 second.
    /// </summary>
    public TimeSpan AxisBaselineCalibrationDuration { get; set; } =
        TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the file path for caching device information between application runs.
    /// If null or empty, device caching is disabled.
    /// </summary>
    public string? DeviceCachePath { get; set; }

    /// <summary>
    /// Gets or sets the list of vendor/product ID pairs that should be exclusively monitored.
    /// If populated, only devices matching these IDs will be watched. If empty, all devices are allowed (subject to blacklist).
    /// </summary>
    public List<VidPid> Whitelist { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of vendor/product ID pairs that should be excluded from monitoring.
    /// Devices matching these IDs will not be watched.
    /// </summary>
    public List<VidPid> Blacklist { get; set; } = [];

    /// <summary>
    /// Gets the time interval between polls, calculated as the reciprocal of <see cref="PollFrequency"/>.
    /// </summary>
    internal TimeSpan PollInterval =>
        TimeSpan.FromSeconds(1d / PollFrequency);

    /// <summary>
    /// Gets the number of samples to collect during baseline calibration,
    /// calculated from <see cref="AxisBaselineCalibrationDuration"/> and <see cref="PollFrequency"/>.
    /// Minimum value is 1.
    /// </summary>
    internal int AxisBaselineSampleCount =>
        Math.Max(
            1,
            (int)Math.Ceiling(
                AxisBaselineCalibrationDuration.TotalSeconds
                * PollFrequency));

    /// <summary>
    /// Validates that all configuration options meet their required constraints.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if all options are valid; otherwise, <see langword="false"/>.
    /// </returns>
    internal bool Validate()
    {
        bool valid = this.PollFrequency > 0
        && this.AxisChangeThreshold is > 0 and <= 2
        && this.AxisResetThreshold >= 0
        && this.AxisResetThreshold < this.AxisChangeThreshold
        && this.AxisBaselineCalibrationDuration >= TimeSpan.Zero
        && this.Whitelist.All(IsValid)
        && this.Blacklist.All(IsValid);
        return valid;
    }

    /// <summary>
    /// Validates that a vendor/product ID pair has valid ranges.
    /// </summary>
    /// <param name="value">The vendor/product ID pair to validate.</param>
    /// <returns>
    /// <see langword="true"/> if both VendorId and ProductId are within valid range (0 to 65535); otherwise, <see langword="false"/>.
    /// </returns>
    private static bool IsValid(VidPid value) =>
        value.VendorId is >= 0 and <= ushort.MaxValue
        && value.ProductId is >= 0 and <= ushort.MaxValue;
}
