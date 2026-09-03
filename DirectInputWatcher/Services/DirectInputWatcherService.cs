using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpGen.Runtime;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;

namespace DirectInputWatcher;

internal sealed class DirectInputWatcherService : IDirectInputWatcher
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifetimeGate = new(1, 1);
    private readonly Dictionary<Guid, ActiveDevice> _activeDevices = new();
    private readonly LifecycleEventHub _lifecycle = new();
    private readonly ISubject<ControllerInputEvent> _inputSubject;
    private readonly DirectInputDeviceEnumerator _enumerator;
    private readonly DirectInputDeviceCache _cache;
    private readonly DirectInputDeviceSessionFactory _sessionFactory;
    private readonly DirectInputDeviceFilter _filter;
    private readonly IUsbDeviceChangeSource _usbChanges;
    private readonly DirectInputWatcherOptions _options;
    private readonly ILogger<DirectInputWatcherService> _logger;

    private CancellationTokenSource? _runCancellation;
    private Channel<ScanReason>? _scanRequests;
    private IDisposable? _usbSubscription;
    private Task? _runTask;
    private bool _disposed;

    public DirectInputWatcherService(
        DirectInputDeviceEnumerator enumerator,
        DirectInputDeviceCache cache,
        DirectInputDeviceSessionFactory sessionFactory,
        DirectInputDeviceFilter filter,
        IUsbDeviceChangeSource usbChanges,
        IOptions<DirectInputWatcherOptions> options,
        ILoggerFactory loggerFactory)
    {
        _enumerator = enumerator;
        _cache = cache;
        _sessionFactory = sessionFactory;
        _filter = filter;
        _usbChanges = usbChanges;
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<DirectInputWatcherService>();
        _inputSubject = Subject.Synchronize(new Subject<ControllerInputEvent>());

        Lifecycle = _lifecycle.Observable;
        Inputs = _inputSubject.AsObservable();
    }

    public IObservable<DirectInputLifecycleEvent> Lifecycle { get; }

    public IObservable<ControllerInputEvent> Inputs { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifetimeGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
            {
                return;
            }

            _runCancellation = new CancellationTokenSource();
            _scanRequests = Channel.CreateBounded<ScanReason>(
                new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                });

            RestoreCachedDevices();
            _usbSubscription = _usbChanges.Observe().Subscribe(
                notification =>
                {
                    if (notification.Error is not null)
                    {
                        PublishError(
                            WatcherErrorKind.UsbWatcher,
                            "The USB device watcher stopped and will restart.",
                            notification.Error);
                        RequestScan(ScanReason.Recovery);
                        return;
                    }

                    Publish(
                        new UsbDeviceChanged(
                            DateTimeOffset.UtcNow,
                            notification.Kind!.Value,
                            notification.DeviceName,
                            notification.DevicePath,
                            notification.ControllerPath,
                            notification.VendorId,
                            notification.ProductId));
                    RequestScan(ScanReason.UsbDeviceChanged);
                });
            _runTask = RunAsync(
                _scanRequests.Reader,
                _runCancellation.Token);
            RequestScan(ScanReason.Startup);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifetimeGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetimeGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await StopCoreAsync();
            _lifecycle.Dispose();
            _inputSubject.OnCompleted();
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        if (_runTask is null)
        {
            return;
        }

        _usbSubscription?.Dispose();
        _usbSubscription = null;
        _runCancellation!.Cancel();
        _scanRequests!.Writer.TryComplete();

        try
        {
            await _runTask;
        }
        catch (OperationCanceledException)
            when (_runCancellation.IsCancellationRequested)
        {
        }

        _runTask = null;
        _scanRequests = null;
        _runCancellation.Dispose();
        _runCancellation = null;
        StopAllDevices("watcher stopped");
    }

    private async Task RunAsync(
        ChannelReader<ScanReason> requests,
        CancellationToken cancellationToken)
    {
        await foreach (ScanReason queuedReason in requests.ReadAllAsync(cancellationToken))
        {
            ScanReason reason = queuedReason;
            while (requests.TryRead(out ScanReason newerReason))
            {
                reason = newerReason;
            }

            try
            {
                await RefreshDevicesAsync(reason, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PublishError(
                    WatcherErrorKind.Enumeration,
                    "An unexpected error occurred while refreshing DirectInput devices.",
                    exception);
            }
        }
    }

    private async Task RefreshDevicesAsync(
        ScanReason reason,
        CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        Publish(new ScanStarted(DateTimeOffset.UtcNow, reason));
        Task<IReadOnlyList<DirectInputDeviceDescriptor>> enumerationTask =
            Task.Run(_enumerator.Enumerate, CancellationToken.None);

        try
        {
            while (!enumerationTask.IsCompleted)
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                if (await Task.WhenAny(enumerationTask, delay) == enumerationTask)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                Publish(
                    new ScanProgress(
                        DateTimeOffset.UtcNow,
                        reason,
                        elapsed.Elapsed));
            }

            IReadOnlyList<DirectInputDeviceDescriptor> discovered =
                await enumerationTask.WaitAsync(cancellationToken);
            SaveCache(discovered);
            Reconcile(discovered.Where(_filter.IsAllowed).ToArray());
            Publish(
                new ScanCompleted(
                    DateTimeOffset.UtcNow,
                    reason,
                    elapsed.Elapsed,
                    discovered.Count));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishError(
                WatcherErrorKind.Enumeration,
                "DirectInput device enumeration failed.",
                exception);
        }
    }

    private void RestoreCachedDevices()
    {
        IReadOnlyList<DirectInputDeviceDescriptor> cached;
        try
        {
            cached = _cache.Load();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            PublishError(
                WatcherErrorKind.CacheRead,
                "Could not read the DirectInput device cache.",
                exception);
            return;
        }

        foreach (DirectInputDeviceDescriptor descriptor in cached.Where(_filter.IsAllowed))
        {
            StartDevice(descriptor, fromCache: true);
        }
    }

    private void SaveCache(IReadOnlyList<DirectInputDeviceDescriptor> devices)
    {
        try
        {
            _cache.Save(devices);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            PublishError(
                WatcherErrorKind.CacheWrite,
                "Could not write the DirectInput device cache.",
                exception);
        }
    }

    private void Reconcile(IReadOnlyList<DirectInputDeviceDescriptor> discovered)
    {
        HashSet<Guid> discoveredIds =
            discovered.Select(device => device.InstanceGuid).ToHashSet();
        Guid[] removedIds;

        lock (_gate)
        {
            removedIds = _activeDevices.Keys
                .Where(instanceGuid => !discoveredIds.Contains(instanceGuid))
                .ToArray();
        }

        foreach (Guid instanceGuid in removedIds)
        {
            StopDevice(instanceGuid, "device disconnected");
        }

        foreach (DirectInputDeviceDescriptor descriptor in discovered)
        {
            StartDevice(descriptor, fromCache: false);
        }
    }

    private void StartDevice(
        DirectInputDeviceDescriptor descriptor,
        bool fromCache)
    {
        lock (_gate)
        {
            if (_activeDevices.ContainsKey(descriptor.InstanceGuid))
            {
                return;
            }
        }

        DirectInputDeviceSession? session = null;
        EventLoopScheduler? scheduler = null;
        IDisposable? subscription = null;

        try
        {
            session = _sessionFactory.Create(descriptor);
            scheduler = new EventLoopScheduler(start =>
                new Thread(start)
                {
                    IsBackground = true,
                    Name = $"DirectInput-{descriptor.InstanceGuid:N}"
                });

            DirectInputDeviceSession capturedSession = session;
            subscription = Observable
                .Interval(_options.PollInterval, scheduler)
                .Select(_ => capturedSession.ReadSnapshot())
                .DetectInputEvents(
                    _options.AxisChangeThreshold,
                    _options.AxisResetThreshold,
                    _options.AxisBaselineSampleCount)
                .Subscribe(
                    _inputSubject.OnNext,
                    exception => HandlePollingFailure(descriptor, exception));

            ActiveDevice activeDevice =
                new(descriptor, session, scheduler, subscription);
            lock (_gate)
            {
                if (!_activeDevices.TryAdd(
                    descriptor.InstanceGuid,
                    activeDevice))
                {
                    activeDevice.Dispose();
                    return;
                }

            }

            _lifecycle.Connect(descriptor, fromCache);
        }
        catch (Exception exception) when (
            exception is SharpGenException
            or InvalidOperationException)
        {
            subscription?.Dispose();
            scheduler?.Dispose();
            session?.Dispose();
            PublishError(
                WatcherErrorKind.Acquisition,
                $"Could not acquire DirectInput device {descriptor.Name}.",
                exception,
                descriptor);
        }
    }

    private void HandlePollingFailure(
        DirectInputDeviceDescriptor descriptor,
        Exception exception)
    {
        PublishError(
            WatcherErrorKind.Polling,
            $"Polling DirectInput device {descriptor.Name} failed.",
            exception,
            descriptor);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            StopDevice(descriptor.InstanceGuid, "polling failed");
            RequestScan(ScanReason.Recovery);
        });
    }

    private void StopDevice(Guid instanceGuid, string reason)
    {
        ActiveDevice? device;
        lock (_gate)
        {
            if (!_activeDevices.Remove(instanceGuid, out device))
            {
                return;
            }

        }

        _lifecycle.Disconnect(device.Descriptor, reason);
        device.Dispose();
    }

    private void StopAllDevices(string reason)
    {
        Guid[] instanceGuids;
        lock (_gate)
        {
            instanceGuids = _activeDevices.Keys.ToArray();
        }

        foreach (Guid instanceGuid in instanceGuids)
        {
            StopDevice(instanceGuid, reason);
        }
    }

    private void RequestScan(ScanReason reason) =>
        _scanRequests?.Writer.TryWrite(reason);

    private void Publish(DirectInputLifecycleEvent lifecycleEvent)
    {
        _logger.LogDebug(
            "DirectInput lifecycle event {LifecycleEventType}.",
            lifecycleEvent.GetType().Name);
        _lifecycle.Publish(lifecycleEvent);
    }

    private void PublishError(
        WatcherErrorKind kind,
        string message,
        Exception exception,
        DirectInputDeviceDescriptor? device = null)
    {
        _logger.LogWarning(exception, "{WatcherErrorMessage}", message);
        Publish(
            new WatcherError(
                DateTimeOffset.UtcNow,
                kind,
                message,
                exception,
                device));
    }

    private sealed class ActiveDevice(
        DirectInputDeviceDescriptor descriptor,
        DirectInputDeviceSession session,
        EventLoopScheduler scheduler,
        IDisposable subscription)
        : IDisposable
    {
        public DirectInputDeviceDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
            subscription.Dispose();
            scheduler.Dispose();
            session.Dispose();
        }
    }
}
