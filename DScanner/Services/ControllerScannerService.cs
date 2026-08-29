using DScanner.Configuration;
using DScanner.DirectInput;
using DScanner.Models;
using DScanner.Reactive;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpGen.Runtime;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Diagnostics;
using System.Threading.Channels;

namespace DScanner.Services;

public sealed class ControllerScannerService(
    DirectInputDeviceEnumerator enumerator,
    DirectInputDeviceCache deviceCache,
    DirectInputDeviceSessionFactory sessionFactory,
    IDeviceChangeObservable deviceChanges,
    IConsoleUi consoleUi,
    IOptions<ScannerOptions> options,
    ILogger<ControllerScannerService> logger)
    : BackgroundService
{
    private static readonly TimeSpan SlowEnumerationThreshold = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, ActiveDevice> _activeDevices = [];
    private readonly ScannerOptions _options = options.Value;
    private readonly Channel<DeviceChangeNotification> _refreshRequests =
        Channel.CreateBounded<DeviceChangeNotification>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "DirectInput scanner starting at {PollFrequencyHz} Hz. Fast enumeration is enabled; XInput devices may be included.",
            _options.PollFrequencyHz);
        consoleUi.SetStatus(
            $"Watching USB DirectInput devices at {_options.PollFrequencyHz} Hz | Fast enumeration");

        await Task.Yield();
        IDisposable deviceChangeSubscription = Observable
            .Return(new DeviceChangeNotification(DeviceChangeKind.InitialScan))
            .Concat(deviceChanges.Observe())
            .Subscribe(
                notification => _refreshRequests.Writer.TryWrite(notification),
                exception => _refreshRequests.Writer.TryComplete(exception));

        try
        {
            await foreach (
                DeviceChangeNotification queuedNotification
                    in _refreshRequests.Reader.ReadAllAsync(stoppingToken))
            {
                DeviceChangeNotification notification = queuedNotification;
                while (_refreshRequests.Reader.TryRead(out DeviceChangeNotification? newer))
                {
                    notification = newer;
                }

                if (notification.Kind == DeviceChangeKind.InitialScan)
                {
                    logger.LogInformation(
                        "Discovering attached USB DirectInput game controllers.");
                    RestoreCachedDevices();
                }
                else
                {
                    logger.LogInformation(
                        "Refreshing watched DirectInput devices after USB {ChangeKind}: {DeviceName} (VID_{VendorId}, PID_{ProductId}).",
                        notification.Kind,
                        notification.DeviceName,
                        FormatHex(notification.VendorId),
                        FormatHex(notification.ProductId));
                }

                await RefreshDevicesAsync(stoppingToken);
            }
        }
        finally
        {
            deviceChangeSubscription.Dispose();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        ActiveDevice[] devices;
        lock (_gate)
        {
            devices = _activeDevices.Values.ToArray();
            _activeDevices.Clear();
        }

        foreach (ActiveDevice device in devices)
        {
            device.Dispose();
        }

        return base.StopAsync(cancellationToken);
    }

    private void ReconcileDevices(IReadOnlyList<DirectInputDeviceDescriptor> discovered)
    {
        logger.LogInformation(
            "DirectInput discovery found {DeviceCount} game controller(s).",
            discovered.Count);

        if (discovered.Count == 0)
        {
            logger.LogInformation(
                "No USB DirectInput game controllers are currently being watched.");
        }

        foreach (DirectInputDeviceDescriptor descriptor in discovered)
        {
            logger.LogInformation(
                "Enumerated DirectInput device {DeviceName} (VID_{VendorId}, PID_{ProductId}).",
                descriptor.Name,
                FormatHex(descriptor.VendorId),
                FormatHex(descriptor.ProductId));
            consoleUi.AddEvent(
                $"Found {descriptor.Name} (VID_{FormatHex(descriptor.VendorId)}, PID_{FormatHex(descriptor.ProductId)})");
        }

        HashSet<Guid> discoveredIds = discovered.Select(device => device.InstanceGuid).ToHashSet();
        Guid[] removedIds;

        lock (_gate)
        {
            removedIds = _activeDevices.Keys
                .Where(instanceGuid => !discoveredIds.Contains(instanceGuid))
                .ToArray();
        }

        foreach (Guid instanceGuid in removedIds)
        {
            StopDevice(instanceGuid, "disconnected");
        }

        foreach (DirectInputDeviceDescriptor descriptor in discovered)
        {
            lock (_gate)
            {
                if (_activeDevices.ContainsKey(descriptor.InstanceGuid))
                {
                    continue;
                }
            }

            StartDevice(descriptor);
        }
    }

    private void StartDevice(DirectInputDeviceDescriptor descriptor)
    {
        DirectInputDeviceSession? session = null;
        EventLoopScheduler? scheduler = null;
        IDisposable? subscription = null;

        try
        {
            session = sessionFactory.Create(descriptor);
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
                    _options.AxisResetThreshold)
                .Subscribe(
                    LogInputEvent,
                    exception => HandleDeviceFailure(descriptor.InstanceGuid, descriptor.Name, exception));

            ActiveDevice activeDevice = new(session, scheduler, subscription);
            lock (_gate)
            {
                if (!_activeDevices.TryAdd(descriptor.InstanceGuid, activeDevice))
                {
                    activeDevice.Dispose();
                    return;
                }
            }

            logger.LogInformation(
                "Monitoring DirectInput device {DeviceName} (VID_{VendorId}, PID_{ProductId}, {DeviceType}, {InstanceGuid}).",
                descriptor.Name,
                FormatHex(descriptor.VendorId),
                FormatHex(descriptor.ProductId),
                descriptor.Type,
                descriptor.InstanceGuid);
            consoleUi.AddEvent(
                $"Watching {descriptor.Name} (VID_{FormatHex(descriptor.VendorId)}, PID_{FormatHex(descriptor.ProductId)})");
        }
        catch (SharpGenException exception)
        {
            subscription?.Dispose();
            scheduler?.Dispose();
            session?.Dispose();
            logger.LogWarning(
                exception,
                "Could not acquire DirectInput device {DeviceName} ({InstanceGuid}).",
                descriptor.Name,
                descriptor.InstanceGuid);
        }
        catch (InvalidOperationException exception)
        {
            subscription?.Dispose();
            scheduler?.Dispose();
            session?.Dispose();
            logger.LogError(
                exception,
                "DirectInput device {DeviceName} could not be initialized.",
                descriptor.Name);
        }
    }

    private void HandleDeviceFailure(Guid instanceGuid, string deviceName, Exception exception)
    {
        logger.LogWarning(
            exception,
            "Polling DirectInput device {DeviceName} failed; it will be rediscovered.",
            deviceName);

        ThreadPool.QueueUserWorkItem(_ => StopDevice(instanceGuid, "polling failed"));
        _refreshRequests.Writer.TryWrite(
            new DeviceChangeNotification(DeviceChangeKind.Recovery, deviceName));
    }

    private async Task RefreshDevicesAsync(CancellationToken stoppingToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        Task<IReadOnlyList<DirectInputDeviceDescriptor>> enumerationTask =
            Task.Run(enumerator.Enumerate, CancellationToken.None);

        await ShowEnumerationProgressAsync(
            consoleUi,
            enumerationTask,
            elapsed,
            stoppingToken);

        try
        {
            IReadOnlyList<DirectInputDeviceDescriptor> discovered =
                await enumerationTask.WaitAsync(stoppingToken);
            deviceCache.Save(discovered);
            ReconcileDevices(discovered);

            if (elapsed.Elapsed >= SlowEnumerationThreshold)
            {
                logger.LogWarning(
                    "DirectInput device enumeration completed slowly after {ElapsedSeconds:F1} seconds. A device driver may be slow to respond.",
                    elapsed.Elapsed.TotalSeconds);
            }
        }
        catch (Exception exception) when (
            exception is SharpGenException
            or InvalidOperationException)
        {
            logger.LogError(exception, "DirectInput device enumeration failed.");
        }
    }

    private void RestoreCachedDevices()
    {
        IReadOnlyList<DirectInputDeviceDescriptor> cachedDevices =
            deviceCache.Load();
        if (cachedDevices.Count == 0)
        {
            consoleUi.SetStatus(
                "No cached devices; performing first DirectInput discovery");
            return;
        }

        logger.LogInformation(
            "Restoring {DeviceCount} DirectInput controller(s) from the device cache while native discovery runs.",
            cachedDevices.Count);
        consoleUi.SetStatus(
            $"Restoring {cachedDevices.Count} cached DirectInput controller(s)");
        ReconcileDevices(cachedDevices);
        consoleUi.SetStatus(
            $"Watching cached controllers at {_options.PollFrequencyHz} Hz; refreshing in background");
    }

    private static async Task ShowEnumerationProgressAsync(
        IConsoleUi consoleUi,
        Task enumerationTask,
        Stopwatch elapsed,
        CancellationToken stoppingToken)
    {
        consoleUi.BeginEnumeration();

        try
        {
            while (!enumerationTask.IsCompleted)
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                if (await Task.WhenAny(enumerationTask, delay) == enumerationTask)
                {
                    break;
                }

                stoppingToken.ThrowIfCancellationRequested();
                consoleUi.AdvanceEnumeration();
            }
        }
        finally
        {
            consoleUi.EndEnumeration(elapsed.Elapsed);
        }
    }

    private static string FormatHex(int? value) =>
        value?.ToString("X4") ?? "????";

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

        string deviceName = device.Session.DeviceName;
        device.Dispose();
        logger.LogInformation(
            "Stopped monitoring DirectInput device {DeviceName}: {Reason}.",
            deviceName,
            reason);
        consoleUi.AddEvent($"Stopped watching {deviceName}: {reason}");
    }

    private void LogInputEvent(ControllerInputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case ButtonPressedEvent button:
                consoleUi.AddEvent(
                    $"{button.DeviceName} button {button.ButtonNumber} pressed");
                logger.LogInformation(
                    "{DeviceName} button {ButtonNumber} pressed",
                    button.DeviceName,
                    button.ButtonNumber);
                break;

            case AxisMovedEvent axis:
                consoleUi.AddEvent(
                    $"{axis.DeviceName} axis {axis.AxisNumber} ({axis.AxisName}) moved to {axis.Value:F3}");
                logger.LogInformation(
                    "{DeviceName} axis {AxisNumber} ({AxisName}) moved to {NormalizedValue:F3}; baseline {BaselineValue:F3}; change {NormalizedDifference:+0.000;-0.000;0.000}",
                    axis.DeviceName,
                    axis.AxisNumber,
                    axis.AxisName,
                    axis.Value,
                    axis.Baseline,
                    axis.Difference);
                break;

            case PovChangedEvent pov:
                consoleUi.AddEvent(
                    $"{pov.DeviceName} POV {pov.PovNumber} moved to {pov.Degrees:0.##} degrees");
                logger.LogInformation(
                    "{DeviceName} POV {PovNumber} moved to {Degrees:0.##} degrees",
                    pov.DeviceName,
                    pov.PovNumber,
                    pov.Degrees);
                break;
        }
    }

    private sealed class ActiveDevice(
        DirectInputDeviceSession session,
        EventLoopScheduler scheduler,
        IDisposable subscription)
        : IDisposable
    {
        public DirectInputDeviceSession Session { get; } = session;

        public void Dispose()
        {
            subscription.Dispose();
            scheduler.Dispose();
            Session.Dispose();
        }
    }
}
