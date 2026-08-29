namespace DScanner.Models;

public sealed record ControllerSnapshot(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset Timestamp,
    IReadOnlyList<bool> Buttons,
    IReadOnlyList<AxisSample> Axes,
    IReadOnlyList<int> Povs);
