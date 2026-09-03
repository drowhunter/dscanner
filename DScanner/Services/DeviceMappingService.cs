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
        // Wait for a snapshot that contains available devices, then let the
        // user choose which device to map. This ensures mapping mode ignores
        // input from other devices.
        TaskCompletionSource<CurrentDevicesSnapshot?> connected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable subscription = watcher.Lifecycle.Subscribe(lifecycleEvent =>
        {
            if (lifecycleEvent is CurrentDevicesSnapshot snapshot && snapshot.Devices.Count > 0)
            {
                connected.TrySetResult(snapshot);
            }
            else if (lifecycleEvent is DeviceConnected connectedEvent)
            {
                // A single device connected event is not as useful as the
                // snapshot, but if nothing else arrives, treat it as a signal
                // to query devices.
                connected.TrySetResult(null);
            }
        });

        consoleUi.SetPrompt("Waiting for controller enumeration...");
        CurrentDevicesSnapshot? snapshot = await connected.Task.WaitAsync(cancellationToken);

        // If the watcher delivered a snapshot, present the devices for
        // selection. If not, fall back to waiting for a snapshot through
        // the lifecycle subscription; but for simplicity, request a brief
        // delay and try again.
        if (snapshot is null)
        {
            // Give the watcher a moment to emit a snapshot.
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            // Try to synchronously get a snapshot from lifecycle by waiting
            // for the next CurrentDevicesSnapshot event.
            TaskCompletionSource<CurrentDevicesSnapshot?> next =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable sub2 = watcher.Lifecycle.Subscribe(e =>
            {
                if (e is CurrentDevicesSnapshot s && s.Devices.Count > 0) next.TrySetResult(s);
            });

            snapshot = await next.Task.WaitAsync(cancellationToken);
        }

        // At this point we should have a snapshot with devices.
        if (snapshot is null || snapshot.Devices.Count == 0)
        {
            // No devices found; nothing to do.
            throw new InvalidOperationException("No controllers were found to map.");
        }

        // If there's only one device, select it automatically.
        if (snapshot.Devices.Count == 1)
        {
            var device = snapshot.Devices[0];
            _deviceId = device.InstanceGuid;
            _deviceName = device.Name;
            _filePath = store.ResolvePath(_deviceName, _deviceId.Value);
            _entries.AddRange(store.Load(_filePath));

            consoleUi.AddEvent($"Selected {DirectInputDeviceLabel.Format(_deviceName, _deviceId.Value)}.", ConsoleColor.Cyan);
        }
        else
        {
            // Present a numbered list and ask the user to pick one.
            consoleUi.AddEvent("Multiple controllers detected; choose one to map:", ConsoleColor.Cyan);
            for (int i = 0; i < snapshot.Devices.Count; i++)
            {
                var d = snapshot.Devices[i];
                consoleUi.AddEvent($"  [{i}] {DirectInputDeviceLabel.Format(d.Name, d.InstanceGuid)}");
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                string? selection = await consoleUi.ReadLabelAsync("Enter device number: ", cancellationToken);
                if (int.TryParse(selection, out int index)
                    && index >= 0
                    && index < snapshot.Devices.Count)
                {
                    var device = snapshot.Devices[index];
                    _deviceId = device.InstanceGuid;
                    _deviceName = device.Name;
                    _filePath = store.ResolvePath(_deviceName, _deviceId.Value);
                    _entries.AddRange(store.Load(_filePath));

                    consoleUi.AddEvent($"Selected {DirectInputDeviceLabel.Format(_deviceName, _deviceId.Value)}.", ConsoleColor.Cyan);
                    break;
                }

                consoleUi.AddEvent("Invalid selection; try again.", ConsoleColor.Yellow);
            }
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
        TaskCompletionSource<CapturedInput?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Some controllers report one physical action as both axis movement and
        // a button press. Wait briefly so we can prompt the user to choose.
        TimeSpan raceWindow = TimeSpan.FromMilliseconds(100);
        ControllerInputEvent? pendingPrimary = null;
        CancellationTokenSource? pendingPrimaryCts = null;
        int pendingVersion = 0;
        object gate = new();

        using IDisposable subscription = watcher.Inputs.Subscribe(inputEvent =>
        {
            if (!IsCapturable(inputEvent))
            {
                return;
            }

            switch (inputEvent)
            {
                case AxisMovedEvent:
                case ButtonPressedEvent:
                    ControllerInputEvent? ambiguousPrimary = null;
                    ControllerInputEvent? ambiguousSecondary = null;
                    int localVersion = 0;
                    CancellationToken localCt;

                    lock (gate)
                    {
                        if (completion.Task.IsCompleted)
                        {
                            return;
                        }

                        if (pendingPrimary is not null && IsAxisButtonPair(pendingPrimary, inputEvent))
                        {
                            ambiguousPrimary = pendingPrimary;
                            ambiguousSecondary = inputEvent;
                            pendingPrimary = null;
                            pendingVersion++;
                            pendingPrimaryCts?.Cancel();
                            pendingPrimaryCts?.Dispose();
                            pendingPrimaryCts = null;
                        }
                        else
                        {
                            pendingVersion++;
                            localVersion = pendingVersion;
                            pendingPrimary = inputEvent;
                            pendingPrimaryCts?.Cancel();
                            pendingPrimaryCts?.Dispose();
                            pendingPrimaryCts = new CancellationTokenSource();
                            localCt = pendingPrimaryCts.Token;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(raceWindow, localCt).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    return;
                                }

                                ControllerInputEvent? resolved = null;
                                lock (gate)
                                {
                                    if (completion.Task.IsCompleted || pendingVersion != localVersion)
                                    {
                                        return;
                                    }

                                    resolved = pendingPrimary;
                                    pendingPrimary = null;
                                }

                                if (resolved is not null)
                                {
                                    completion.TrySetResult(new CapturedInput(resolved, null));
                                }
                            });

                            return;
                        }
                    }

                    if (ambiguousPrimary is not null && ambiguousSecondary is not null)
                    {
                        completion.TrySetResult(new CapturedInput(ambiguousPrimary, ambiguousSecondary));
                    }

                    break;

                default:
                    completion.TrySetResult(new CapturedInput(inputEvent, null));
                    break;
            }
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

        CapturedInput? captured = await completion.Task.WaitAsync(cancellationToken);
        if (captured is null)
        {
            return null;
        }

        if (captured.Secondary is null)
        {
            return captured.Primary;
        }

        return await ResolveAmbiguousInputAsync(captured.Primary, captured.Secondary, cancellationToken);
    }

    private async Task<ControllerInputEvent?> ResolveAmbiguousInputAsync(
        ControllerInputEvent first,
        ControllerInputEvent second,
        CancellationToken cancellationToken)
    {
        if (!IsAxisButtonPair(first, second))
        {
            return first;
        }

        AxisMovedEvent axis = first as AxisMovedEvent ?? (AxisMovedEvent)second;
        ButtonPressedEvent button = first as ButtonPressedEvent ?? (ButtonPressedEvent)second;
        string choicePrompt =
            $"Detected both axis {axis.AxisNumber} ({axis.AxisName}) and button {button.ButtonNumber}. "
            + "Map (A)xis or (B)utton (Esc to skip): ";

        while (!cancellationToken.IsCancellationRequested)
        {
            string? choice = await consoleUi.ReadLabelAsync(choicePrompt, cancellationToken);
            if (choice is null)
            {
                return null;
            }

            if (choice.Equals("a", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("axis", StringComparison.OrdinalIgnoreCase))
            {
                return axis;
            }

            if (choice.Equals("b", StringComparison.OrdinalIgnoreCase)
                || choice.Equals("button", StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }

            consoleUi.AddEvent(
                "Invalid selection; enter A for axis or B for button.",
                ConsoleColor.Yellow);
        }

        return null;
    }

    private static bool IsAxisButtonPair(ControllerInputEvent first, ControllerInputEvent second) =>
        first is AxisMovedEvent && second is ButtonPressedEvent
        || first is ButtonPressedEvent && second is AxisMovedEvent;

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
            "Mapped {Label} to {Description} (replaced {ReplacedLabel}).",
            label,
            description,
            replaced ?? "nothing");
    }

    private static DeviceMappingEntry CreateEntry(string label, ControllerInputEvent captured) =>
        captured switch
        {
            ButtonPressedEvent button => new DeviceMappingEntry(
                label,
                string.Empty,
                button.ButtonNumber,
                1,
                DeviceMappingInputType.Button),

            AxisMovedEvent axis => new DeviceMappingEntry(
                label,
                axis.AxisName?.Trim() ?? string.Empty,
                axis.AxisNumber,
                Math.Sign(axis.Value),
                DeviceMappingInputType.Axis),

            PovChangedEvent pov => new DeviceMappingEntry(
                label,
                string.Empty,
                pov.PovNumber,
                pov.Degrees == -1 ? -1 : (int)pov.Degrees,
                DeviceMappingInputType.Pov),

            _ => throw new ArgumentOutOfRangeException(nameof(captured))
        };

    private static string Describe(DeviceMappingEntry entry) =>
        entry.Type switch
        {
            DeviceMappingInputType.Button => $"button {entry.Index}",
            DeviceMappingInputType.Axis => $"axis {entry.Index} (value {entry.Value})",
            _ => entry.Value == -1 ? $"POV {entry.Index} (centre)" : $"POV {entry.Index} ({entry.Value}°)"
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

    private sealed record CapturedInput(
        ControllerInputEvent Primary,
        ControllerInputEvent? Secondary);
}
