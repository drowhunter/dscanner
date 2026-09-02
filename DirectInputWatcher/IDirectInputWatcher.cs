namespace DirectInputWatcher;

public interface IDirectInputWatcher : IAsyncDisposable
{
    IObservable<DirectInputLifecycleEvent> Lifecycle { get; }

    IObservable<ControllerInputEvent> Inputs { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
