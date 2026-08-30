namespace DirectInputWatcher;

internal sealed record ControllerSnapshot(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset Timestamp,
    IReadOnlyList<bool> Buttons,
    IReadOnlyList<AxisSample> Axes,
    IReadOnlyList<int> Povs);
