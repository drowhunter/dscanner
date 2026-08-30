using Microsoft.Extensions.Logging;
using System.Management;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DirectInputWatcher;

internal sealed class UsbDeviceChangeSource : IUsbDeviceChangeSource
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(1);
    private readonly ILogger _logger;

    public UsbDeviceChangeSource(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<UsbDeviceChangeSource>();
    }

    public IObservable<UsbDeviceChangeNotification> Observe() =>
        Observable.Defer(() =>
        {
            _logger.LogInformation(
                "Watching USB device additions and removals through WMI {CreationEvent} and {DeletionEvent} events for {AssociationClass}.",
                "__InstanceCreationEvent",
                "__InstanceDeletionEvent",
                "Win32_USBControllerDevice");

            return Observable.Merge(
                    Observe("__InstanceCreationEvent", UsbDeviceChangeKind.Created),
                    Observe("__InstanceDeletionEvent", UsbDeviceChangeKind.Deleted)
                )
                .RestartOnError(
                    restartFactory: Observe,
                    restartDelay: RestartDelay,
                    errorNotificationFactory: ex => new UsbDeviceChangeNotification(Error: ex)
                );
        });

    private IObservable<UsbDeviceChangeNotification> Observe(string eventClass, UsbDeviceChangeKind kind) =>
        Observable.Create<UsbDeviceChangeNotification>(observer =>
        {
            ManagementEventWatcher watcher = new(new WqlEventQuery(
                $"SELECT * FROM {eventClass} WITHIN 1 WHERE TargetInstance ISA 'Win32_USBControllerDevice'"));

            EventArrivedEventHandler eventHandler = (_, args) =>
            {
                ManagementBaseObject? association = args.NewEvent["TargetInstance"] as ManagementBaseObject;
                string? controllerPath = association?["Antecedent"]?.ToString();
                string? devicePath = association?["Dependent"]?.ToString();
                UsbDeviceIdentity identity = ResolveIdentity(devicePath);

                _logger.LogInformation("USB device {ChangeKind}: {DeviceName} (VID_{VendorId}, PID_{ProductId}).",
                    kind,
                    identity.Name,
                    FormatHex(identity.VendorId),
                    FormatHex(identity.ProductId));
                observer.OnNext(new UsbDeviceChangeNotification(
                    kind,
                    identity.Name,
                    devicePath,
                    controllerPath,
                    identity.VendorId,
                    identity.ProductId));
            };

            StoppedEventHandler stoppedHandler = (_, args) =>
            {
                if (args.Status != ManagementStatus.NoError)
                {
                    observer.OnError(new ManagementException($"The USB {eventClass} watcher stopped with status {args.Status}."));
                }
            };

            watcher.EventArrived += eventHandler;
            watcher.Stopped += stoppedHandler;

            try
            {
                watcher.Start();
                _logger.LogInformation(
                    "WMI {EventClass} watcher started for {AssociationClass}.",
                    eventClass,
                    "Win32_USBControllerDevice");
            }
            catch (Exception exception)
            {
                watcher.EventArrived -= eventHandler;
                watcher.Stopped -= stoppedHandler;
                watcher.Dispose();
                observer.OnError(exception);
                return Disposable.Empty;
            }

            return Disposable.Create(() =>
            {
                watcher.EventArrived -= eventHandler;
                watcher.Stopped -= stoppedHandler;

                try
                {
                    watcher.Stop();
                }
                catch (ManagementException exception)
                {
                    _logger.LogDebug(
                        exception,
                        "The USB {EventClass} watcher was already stopped.",
                        eventClass);
                }
                finally
                {
                    watcher.Dispose();
                }
            });
        });

    private UsbDeviceIdentity ResolveIdentity(string? devicePath)
    {
        string? name = null;
        string? pnpDeviceId = null;

        if (!string.IsNullOrWhiteSpace(devicePath))
        {
            try
            {
                using ManagementObject device = new(devicePath);
                device.Get();
                name = device["Name"] as string;
                pnpDeviceId = device["PNPDeviceID"] as string;
            }
            catch (ManagementException exception)
            {
                _logger.LogDebug(
                    exception,
                    "Could not resolve USB device metadata for {DevicePath}.",
                    devicePath);
            }
        }

        int? vendorId = null;
        int? productId = null;
        string identifier = pnpDeviceId ?? devicePath ?? string.Empty;
        if (VidPidParser.TryParse(
            identifier,
            out int parsedVendorId,
            out int parsedProductId))
        {
            vendorId = parsedVendorId;
            productId = parsedProductId;
        }

        return new UsbDeviceIdentity(
            string.IsNullOrWhiteSpace(name) ? devicePath ?? "Unknown USB device" : name,
            vendorId,
            productId);
    }

    private static string FormatHex(int? value) =>
        value?.ToString("X4") ?? "????";

    private sealed record UsbDeviceIdentity(
        string Name,
        int? VendorId,
        int? ProductId);
}
