using DirectInputWatcher;
using DScanner.DirectInput;
using DScanner.Mapping;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DScanner.Services;

/// <summary>
/// Runs the interactive mapping loop: read a label, capture the next control pressed on the
/// device being mapped, and append the pair to that device's JSON mapping file.
/// </summary>
public sealed class DeviceMappingService(
    IDirectInputWatcher watcher,
    IConsoleUi consoleUi,
    IConsoleKeyDispatcher keyDispatcher,
    IDeviceMappingStore store,
    IOptions<DeviceMappingSettings> mappingSettings,
    IOptions<DirectInputWatcherOptions> watcherOptions,
    IHostApplicationLifetime applicationLifetime,
    ILogger<DeviceMappingService> logger)
    : BackgroundService
{
    private const string LabelPrompt = "Label (empty to finish): ";

    private readonly DeviceMappingSettings _settings = mappingSettings.Value;
    private readonly List<DeviceMappingEntry> _entries = [];
    private readonly HashSet<Guid> _warnedDevices = [];

    private Guid? _deviceId;
    private string? _deviceName;
    private string? _filePath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunMappingLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Mapping failed.");
            consoleUi.AddEvent($"Mapping failed: {exception.Message}", ConsoleColor.Red);
            applicationLifetime.StopApplication();
        }
    }

    private async Task RunMappingLoopAsync(CancellationToken stoppingToken)
    {
        consoleUi.AddEvent(
            "Mapping mode: type a label, press Enter, then press the control to bind.",
            ConsoleColor.Cyan);

        await WaitForDeviceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            string? label = await consoleUi.ReadLabelAsync(LabelPrompt, stoppingToken);

            if (string.IsNullOrWhiteSpace(label))
            {
                break;
            }

            ControllerInputEvent? captured = await CaptureInputAsync(
                $"Press the control for '{label}' (Esc to skip): ",
                stoppingToken);

            if (captured is null)
            {
                consoleUi.AddEvent($"Skipped '{label}'.", ConsoleColor.Yellow);
                continue;
            }

            RecordCapture(label, captured);

            if (_settings.SettleDelay > TimeSpan.Zero)
            {
                // Let the button release or axis recentre pass before arming the next capture.
                await Task.Delay(_settings.SettleDelay, stoppingToken);
            }
        }

        consoleUi.ClearPrompt();
        ReportCompletion();
        applicationLifetime.StopApplication();
    }

    private async Task WaitForDeviceAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource connected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable subscription = watcher.Lifecycle.Subscribe(lifecycleEvent =>
        {
            bool hasDevice = lifecycleEvent switch
            {
                CurrentDevicesSnapshot snapshot => snapshot.Devices.Count > 0,
                DeviceConnected => true,
                _ => false
            };

            if (hasDevice)
            {
                connected.TrySetResult();
            }
        });

        if (!connected.Task.IsCompleted)
        {
            consoleUi.SetPrompt("Waiting for a controller...");
            await connected.Task.WaitAsync(cancellationToken);
        }

        // Axes emit nothing until their startup baseline is established.
        TimeSpan calibration = watcherOptions.Value.AxisBaselineCalibrationDuration;
        if (calibration > TimeSpan.Zero)
        {
            consoleUi.SetPrompt("Calibrating axes...");
            await Task.Delay(calibration, cancellationToken);
        }
    }

    private async Task<ControllerInputEvent?> CaptureInputAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<ControllerInputEvent?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable subscription = watcher.Inputs.Subscribe(inputEvent =>
        {
            if (!IsCapturable(inputEvent))
            {
                return;
            }

            completion.TrySetResult(inputEvent);
        });

        using IDisposable escape = keyDispatcher.Capture(key =>
        {
            if (key.Key != ConsoleKey.Escape)
            {
                return false;
            }

            completion.TrySetResult(null);
            return true;
        });

        // Announce only once input is actually being listened for.
        consoleUi.SetPrompt(prompt);

        return await completion.Task.WaitAsync(cancellationToken);
    }

    private bool IsCapturable(ControllerInputEvent inputEvent)
    {
        if (_deviceId is Guid mappedDevice && inputEvent.DeviceId != mappedDevice)
        {
            WarnAboutOtherDevice(inputEvent);
            return false;
        }

        // A POV returning to centre is the release, not a new binding.
        return inputEvent is not PovChangedEvent { RawValue: -1 };
    }

    private void WarnAboutOtherDevice(ControllerInputEvent inputEvent)
    {
        lock (_warnedDevices)
        {
            if (!_warnedDevices.Add(inputEvent.DeviceId))
            {
                return;
            }
        }

        consoleUi.AddEvent(
            $"Ignoring input from {DirectInputDeviceLabel.Format(inputEvent.DeviceName, inputEvent.DeviceId)}; "
            + $"this session is mapping {_deviceName}.",
            ConsoleColor.Yellow);
    }

    private void RecordCapture(string label, ControllerInputEvent captured)
    {
        if (_deviceId is null)
        {
            _deviceId = captured.DeviceId;
            _deviceName = captured.DeviceName;
            _filePath = store.ResolvePath(captured.DeviceName, captured.DeviceId);
            _entries.AddRange(store.Load(_filePath));

            consoleUi.AddEvent(
                $"Mapping {DirectInputDeviceLabel.Format(captured.DeviceName, captured.DeviceId)} to {_filePath}",
                ConsoleColor.Cyan);
            logger.LogInformation(
                "Mapping device {DeviceName} to {MappingFilePath}.",
                captured.DeviceName,
                _filePath);
        }

        DeviceMappingEntry entry = CreateEntry(label, captured);
        string? replaced = DeviceMappingStore.Upsert(_entries, entry);
        store.Save(_filePath!, _entries);

        string description = Describe(entry);

        if (replaced is null)
        {
            consoleUi.AddEvent($"Mapped '{label}' to {description}.", ConsoleColor.Green);
        }
        else
        {
            consoleUi.AddEvent(
                $"Mapped '{label}' to {description}, replacing '{replaced}'.",
                ConsoleColor.Yellow);
        }

        logger.LogInformation(
            "Mapped {Label} to {MappingType} {MappingNumber} (replaced {ReplacedLabel}).",
            label,
            entry.Type,
            entry.ButtonNumber,
            replaced ?? "nothing");
    }

    private static DeviceMappingEntry CreateEntry(string label, ControllerInputEvent captured) =>
        captured switch
        {
            ButtonPressedEvent button => new DeviceMappingEntry(
                label,
                button.ButtonNumber,
                DeviceMappingInputType.Button),

            AxisMovedEvent axis => new DeviceMappingEntry(
                label,
                axis.AxisNumber,
                DeviceMappingInputType.Axis,
                axis.Difference < 0 ? -1 : 1),

            PovChangedEvent pov => new DeviceMappingEntry(
                label,
                pov.PovNumber,
                DeviceMappingInputType.Pov),

            _ => throw new ArgumentOutOfRangeException(nameof(captured))
        };

    private static string Describe(DeviceMappingEntry entry) =>
        entry.Type switch
        {
            DeviceMappingInputType.Button => $"button {entry.ButtonNumber}",
            DeviceMappingInputType.Axis =>
                $"axis {entry.ButtonNumber} ({(entry.Direction < 0 ? "negative" : "positive")})",
            _ => $"POV {entry.ButtonNumber}"
        };

    private void ReportCompletion()
    {
        if (_filePath is null)
        {
            consoleUi.AddEvent("Mapping cancelled; nothing was written.", ConsoleColor.Yellow);
            return;
        }

        consoleUi.AddEvent(
            $"Saved {_entries.Count} mapping(s) to {_filePath}",
            ConsoleColor.Green);
        logger.LogInformation(
            "Saved {MappingCount} mappings to {MappingFilePath}.",
            _entries.Count,
            _filePath);
    }
}
