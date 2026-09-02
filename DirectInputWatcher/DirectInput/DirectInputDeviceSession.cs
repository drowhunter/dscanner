using Microsoft.Extensions.Logging;
using SharpGen.Runtime;
using Vortice.DirectInput;

namespace DirectInputWatcher;

internal sealed class DirectInputDeviceSession : IDisposable
{
    private const int ConfiguredAxisMinimum = -1000;
    private const int ConfiguredAxisMaximum = 1000;

    private readonly IDirectInputDevice8 _device;
    private readonly DirectInputDeviceDescriptor _descriptor;
    private readonly ILogger<DirectInputDeviceSession> _logger;
    private readonly AxisBinding[] _axes;
    private readonly int _buttonCount;
    private readonly int _povCount;
    private bool _disposed;

    public DirectInputDeviceSession(
        IDirectInputDevice8 device,
        DirectInputDeviceDescriptor descriptor,
        nint cooperativeWindowHandle,
        ILogger<DirectInputDeviceSession> logger)
    {
        _device = device;
        _descriptor = descriptor;
        _logger = logger;

        try
        {
            _device.SetDataFormat<RawJoystickState>().CheckError();
            _device.SetCooperativeLevel(
                cooperativeWindowHandle,
                CooperativeLevel.Background | CooperativeLevel.NonExclusive).CheckError();

            _axes = BuildAxisBindings();
            Capabilities capabilities = _device.Capabilities;
            _buttonCount = Math.Min(capabilities.ButtonCount, 128);
            _povCount = Math.Min(capabilities.PovCount, 4);
            _device.Acquire().CheckError();
        }
        catch
        {
            _device.Dispose();
            throw;
        }
    }

    public Guid DeviceId => _descriptor.InstanceGuid;

    public string DeviceName => _descriptor.Name;

    public DirectInputDeviceDescriptor Descriptor => _descriptor;

    public ControllerSnapshot ReadSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Result pollResult = _device.Poll();
        if (pollResult.Failure)
        {
            Result acquireResult = _device.Acquire();
            if (acquireResult.Failure)
            {
                acquireResult.CheckError();
            }

            _device.Poll().CheckError();
        }

        JoystickState state = _device.GetCurrentJoystickState();
        AxisSample[] axes = _axes
            .Select(binding => new AxisSample(
                binding.Number,
                binding.Name,
                DirectInputValueNormalizer.Normalize(
                    binding.Read(state),
                    binding.Minimum,
                    binding.Maximum)))
            .ToArray();

        return new ControllerSnapshot(
            _descriptor.InstanceGuid,
            _descriptor.Name,
            DateTimeOffset.UtcNow,
            state.Buttons.Take(_buttonCount).ToArray(),
            axes,
            state.PointOfViewControllers.Take(_povCount).ToArray());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Result result = _device.Unacquire();
        if (result.Failure)
        {
            _logger.LogDebug(
                "DirectInput device {DeviceName} was already unacquired during disposal.",
                _descriptor.Name);
        }

        _device.Dispose();
    }

    private AxisBinding[] BuildAxisBindings()
    {
        List<AxisBinding> bindings = [];
        int sliderIndex = 0;

        foreach (DeviceObjectInstance axis in _device.GetObjects(DeviceObjectTypeFlags.Axis))
        {
            Func<JoystickState, int>? read = null;
            string semanticName;

            if (axis.ObjectType == ObjectGuid.XAxis)
            {
                semanticName = "X";
                read = state => state.X;
            }
            else if (axis.ObjectType == ObjectGuid.YAxis)
            {
                semanticName = "Y";
                read = state => state.Y;
            }
            else if (axis.ObjectType == ObjectGuid.ZAxis)
            {
                semanticName = "Z";
                read = state => state.Z;
            }
            else if (axis.ObjectType == ObjectGuid.RxAxis)
            {
                semanticName = "Rx";
                read = state => state.RotationX;
            }
            else if (axis.ObjectType == ObjectGuid.RyAxis)
            {
                semanticName = "Ry";
                read = state => state.RotationY;
            }
            else if (axis.ObjectType == ObjectGuid.RzAxis)
            {
                semanticName = "Rz";
                read = state => state.RotationZ;
            }
            else if (axis.ObjectType == ObjectGuid.Slider && sliderIndex < 2)
            {
                int capturedSliderIndex = sliderIndex++;
                semanticName = $"Slider{capturedSliderIndex}";
                read = state => state.Sliders[capturedSliderIndex];
            }
            else
            {
                _logger.LogWarning(
                    "Skipping unsupported axis object {AxisName} ({ObjectType}) on {DeviceName}.",
                    axis.Name,
                    axis.ObjectType,
                    _descriptor.Name);
                continue;
            }

            (int minimum, int maximum) = ConfigureAxisRange(axis);
            string name = string.IsNullOrWhiteSpace(axis.Name) ? semanticName : axis.Name;
            bindings.Add(new AxisBinding(bindings.Count, name, read, minimum, maximum));
        }

        return bindings.ToArray();
    }

    private (int Minimum, int Maximum) ConfigureAxisRange(DeviceObjectInstance axis)
    {
        ObjectProperties properties = _device.GetObjectPropertiesById(axis.ObjectId);

        try
        {
            properties.Range = new InputRange(ConfiguredAxisMinimum, ConfiguredAxisMaximum);
            return (ConfiguredAxisMinimum, ConfiguredAxisMaximum);
        }
        catch (SharpGenException exception)
        {
            InputRange range = properties.Range;
            if (range.Maximum > range.Minimum)
            {
                _logger.LogDebug(
                    exception,
                    "Axis {AxisName} on {DeviceName} rejected the configured range; using {Minimum}..{Maximum}.",
                    axis.Name,
                    _descriptor.Name,
                    range.Minimum,
                    range.Maximum);
                return (range.Minimum, range.Maximum);
            }

            throw new InvalidOperationException(
                $"Axis {axis.Name} on {_descriptor.Name} has an invalid DirectInput range.",
                exception);
        }
    }

    private sealed record AxisBinding(
        int Number,
        string Name,
        Func<JoystickState, int> Read,
        int Minimum,
        int Maximum);
}
