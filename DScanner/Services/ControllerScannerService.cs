using DScanner.DirectInput;
using DirectInputWatcher;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DScanner.Services;

public sealed class ControllerScannerService(
    IDirectInputWatcher watcher,
    IConsoleUi consoleUi,
    ILogger<ControllerScannerService> logger)
    : BackgroundService
{
    private IDisposable? _lifecycleSubscription;
    private IDisposable? _inputSubscription;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _lifecycleSubscription = watcher.Lifecycle.Subscribe(LogLifecycleEvent);
        _inputSubscription = watcher.Inputs.Subscribe(LogInputEvent);

        await watcher.StartAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await watcher.StopAsync(cancellationToken);
        _inputSubscription?.Dispose();
        _lifecycleSubscription?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    private void LogLifecycleEvent(DirectInputLifecycleEvent lifecycleEvent)
    {
        switch (lifecycleEvent)
        {
            case CurrentDevicesSnapshot snapshot:
                consoleUi.SetStatus(
                    snapshot.Devices.Count == 0
                        ? "Watching USB DirectInput devices"
                        : $"Watching {snapshot.Devices.Count} DirectInput controller(s)");
                break;

            case DeviceConnected connected:
                string cacheMarker = connected.FromCache ? "* " : string.Empty;
                logger.LogInformation(
                    "{CacheMarker}Found DirectInput device {DeviceName} (VID_{VendorId}, PID_{ProductId}).",
                    cacheMarker,
                    connected.Device.Name,
                    FormatHex(connected.Device.VendorId),
                    FormatHex(connected.Device.ProductId));
                consoleUi.AddEvent(
                    $"{cacheMarker}Found {DirectInputDeviceLabel.Format(connected.Device.Name, connected.Device.InstanceGuid)} (VID_{FormatHex(connected.Device.VendorId)}, PID_{FormatHex(connected.Device.ProductId)})",
                    ConsoleColor.Green);
                consoleUi.AddEvent(
                    $"Watching {DirectInputDeviceLabel.Format(connected.Device.Name, connected.Device.InstanceGuid)} (VID_{FormatHex(connected.Device.VendorId)}, PID_{FormatHex(connected.Device.ProductId)})");
                break;

            case DeviceDisconnected disconnected:
                logger.LogInformation(
                    "Stopped monitoring DirectInput device {DeviceName}: {Reason}.",
                    disconnected.Device.Name,
                    disconnected.Reason);
                consoleUi.AddEvent(
                    $"Stopped watching {DirectInputDeviceLabel.Format(disconnected.Device.Name, disconnected.Device.InstanceGuid)}: {disconnected.Reason}");
                break;

            case ScanStarted:
                consoleUi.BeginEnumeration();
                break;

            case ScanProgress:
                consoleUi.AdvanceEnumeration();
                break;

            case ScanCompleted completed:
                consoleUi.EndEnumeration(completed.Elapsed);
                break;

            case UsbDeviceChanged usb:
                logger.LogInformation(
                    "USB device {ChangeKind}: {DeviceName} (VID_{VendorId}, PID_{ProductId}).",
                    usb.Kind,
                    usb.DeviceName,
                    FormatHex(usb.VendorId),
                    FormatHex(usb.ProductId));
                consoleUi.AddEvent(
                    $"USB {usb.Kind}: {usb.DeviceName ?? "Unknown USB device"} (VID_{FormatHex(usb.VendorId)}, PID_{FormatHex(usb.ProductId)})");
                break;

            case WatcherError error:
                logger.LogWarning(
                    error.Exception,
                    "{WatcherErrorKind}: {WatcherErrorMessage}",
                    error.Kind,
                    error.Message);
                consoleUi.AddEvent($"Warning: {error.Message}", ConsoleColor.Yellow);
                break;
        }
    }

    private void LogInputEvent(ControllerInputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case ButtonPressedEvent button:
                consoleUi.AddHighlightedEvent(
                    button.DeviceName,
                    $" {DirectInputDeviceLabel.FormatIdentifier(button.DeviceId)} button {button.ButtonNumber} pressed",
                    consoleUi.GetDeviceColor(button.DeviceId));
                logger.LogInformation(
                    "{DeviceName} button {ButtonNumber} pressed",
                    button.DeviceName,
                    button.ButtonNumber);
                break;

            case AxisMovedEvent axis:
                consoleUi.AddHighlightedEvent(
                    axis.DeviceName,
                    $" {DirectInputDeviceLabel.FormatIdentifier(axis.DeviceId)} axis {axis.AxisNumber} ({axis.AxisName}) moved to {axis.Value:F3}",
                    consoleUi.GetDeviceColor(axis.DeviceId));
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
                    $"{DirectInputDeviceLabel.Format(pov.DeviceName, pov.DeviceId)} POV {pov.PovNumber} moved to {pov.Degrees:0.##} degrees");
                logger.LogInformation(
                    "{DeviceName} POV {PovNumber} moved to {Degrees:0.##} degrees",
                    pov.DeviceName,
                    pov.PovNumber,
                    pov.Degrees);
                break;
        }
    }

    private static string FormatHex(int? value) =>
        value?.ToString("X4") ?? "????";
}
