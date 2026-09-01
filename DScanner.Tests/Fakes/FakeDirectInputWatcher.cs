using System.Reactive.Subjects;
using DirectInputWatcher;

namespace DScanner.Tests.Fakes;

internal sealed class FakeDirectInputWatcher : IDirectInputWatcher
{
    public Subject<DirectInputLifecycleEvent> LifecycleSubject { get; } = new();

    public Subject<ControllerInputEvent> InputSubject { get; } = new();

    public IObservable<DirectInputLifecycleEvent> Lifecycle => LifecycleSubject;

    public IObservable<ControllerInputEvent> Inputs => InputSubject;

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
