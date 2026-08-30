using Vortice.DirectInput;

namespace DirectInputWatcher;

internal sealed class DirectInputContext : IDisposable
{
    private readonly IDirectInput8 _directInput = DInput.DirectInput8Create();

    public IDirectInputDevice8 CreateDevice(Guid instanceGuid) =>
        _directInput.CreateDevice(instanceGuid);

    public void Dispose() => _directInput.Dispose();
}
