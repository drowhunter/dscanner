using Microsoft.Extensions.Logging;

namespace DScanner.DirectInput;

public sealed class DirectInputDeviceSessionFactory(
    DirectInputContext context,
    CooperativeWindowHandle cooperativeWindow,
    ILoggerFactory loggerFactory)
{
    public DirectInputDeviceSession Create(DirectInputDeviceDescriptor descriptor) =>
        new(
            context.CreateDevice(descriptor.InstanceGuid),
            descriptor,
            cooperativeWindow.Handle,
            loggerFactory.CreateLogger<DirectInputDeviceSession>());
}
