namespace DirectInputWatcher;

public abstract record ControllerInputEvent(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset Timestamp);

public sealed record ButtonPressedEvent(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset Timestamp,
    int ButtonNumber)
    : ControllerInputEvent(DeviceId, DeviceName, Timestamp);

public sealed record AxisMovedEvent(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset Timestamp,
    int AxisNumber,
    string AxisName,
    double Value,
    double Baseline,
    double Difference)
    : ControllerInputEvent(DeviceId, DeviceName, Timestamp);

public sealed record PovChangedEvent(
    Guid DeviceId,
    string DeviceName,
    DateTimeOffset Timestamp,
    int PovNumber,
    int RawValue)
    : ControllerInputEvent(DeviceId, DeviceName, Timestamp)
{
    public double Degrees => RawValue == -1 ? -1 : RawValue / 100d;
}
