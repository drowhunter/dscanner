using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DirectInputWatcher;

internal sealed class LifecycleEventHub : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DirectInputDeviceDescriptor> _connected = [];
    private readonly ISubject<DirectInputLifecycleEvent> _events =
        Subject.Synchronize(new Subject<DirectInputLifecycleEvent>());

    public LifecycleEventHub()
    {
        Observable = System.Reactive.Linq.Observable.Create<DirectInputLifecycleEvent>(
            observer =>
            {
                lock (_gate)
                {
                    observer.OnNext(
                        new CurrentDevicesSnapshot(
                            DateTimeOffset.UtcNow,
                            _connected.Values
                                .OrderBy(device => device.Name)
                                .ToArray()));
                    return _events.Subscribe(observer);
                }
            });
    }

    public IObservable<DirectInputLifecycleEvent> Observable { get; }

    public void Connect(DirectInputDeviceDescriptor device, bool fromCache)
    {
        lock (_gate)
        {
            _connected[device.InstanceGuid] = device;
            _events.OnNext(
                new DeviceConnected(
                    DateTimeOffset.UtcNow,
                    device,
                    fromCache));
        }
    }

    public void Disconnect(DirectInputDeviceDescriptor device, string reason)
    {
        lock (_gate)
        {
            if (!_connected.Remove(device.InstanceGuid))
            {
                return;
            }

            _events.OnNext(
                new DeviceDisconnected(
                    DateTimeOffset.UtcNow,
                    device,
                    reason));
        }
    }

    public void Publish(DirectInputLifecycleEvent lifecycleEvent)
    {
        lock (_gate)
        {
            _events.OnNext(lifecycleEvent);
        }
    }

    public void Dispose() => _events.OnCompleted();
}
