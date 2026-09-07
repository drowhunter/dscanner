# DirectInputWatcher

A reusable Windows class library that watches every attached DirectInput game controller and reports
what the user just did — a button pressed, an axis moved, a POV hat clicked — as two Rx event
streams.

It handles the parts that are tedious to get right: fast startup from a device cache, USB hot-plug
reconciliation, per-device polling on dedicated threads, axis normalization with a settled baseline
and hysteresis, VID/PID filtering, and recovery from devices that vanish mid-poll.

The library is transport-agnostic and UI-agnostic. It does not reference
`Microsoft.Extensions.Hosting`, never starts itself, and writes nothing to the console — you call
`StartAsync`, subscribe, and decide what the events mean. [`DScanner`](../README.md) is a console
host built on top of it.

## Requirements

- Windows — DirectInput and WMI are Windows-only.
- .NET 10 (`net10.0-windows`).

Dependencies: `Vortice.DirectInput` (native DirectInput), `System.Management` (WMI USB
notifications), `System.Reactive`, and the `Microsoft.Extensions` abstractions for dependency
injection, options, and logging.

## Getting started

Add a reference to the project:

```bash
dotnet add reference ..\DirectInputWatcher\DirectInputWatcher.csproj
```

Register the watcher, resolve `IDirectInputWatcher`, subscribe, and start it. Logging must be
registered too — the watcher resolves `ILoggerFactory`.

```csharp
using DirectInputWatcher;
using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddLogging();
services.AddDirectInputWatcher(options =>
{
    options.PollFrequency = 15;
    options.DeviceCachePath = @"C:\ProgramData\MyApp\devices.json";
});

await using ServiceProvider provider = services.BuildServiceProvider();
IDirectInputWatcher watcher = provider.GetRequiredService<IDirectInputWatcher>();

using IDisposable lifecycle = watcher.Lifecycle.Subscribe(HandleLifecycle);
using IDisposable inputs = watcher.Inputs.Subscribe(HandleInput);

await watcher.StartAsync(cancellationToken);
// Application runs...
await watcher.StopAsync(cancellationToken);

static void HandleInput(ControllerInputEvent inputEvent)
{
    switch (inputEvent)
    {
        case ButtonPressedEvent button:
            Console.WriteLine($"{button.DeviceName}: button {button.ButtonNumber}");
            break;
        case AxisMovedEvent axis:
            Console.WriteLine($"{axis.DeviceName}: {axis.AxisName} -> {axis.Value:F2}");
            break;
        case PovChangedEvent pov:
            Console.WriteLine($"{pov.DeviceName}: POV {pov.PovNumber} at {pov.Degrees} degrees");
            break;
    }
}

static void HandleLifecycle(DirectInputLifecycleEvent lifecycleEvent) =>
    Console.WriteLine(lifecycleEvent);
```

`StartAsync` is idempotent — calling it on an already-running watcher returns immediately.
`StopAsync` releases every device and can be followed by another `StartAsync`. `IDirectInputWatcher`
is `IAsyncDisposable`; disposing stops the watcher and completes both observables.

## Events

### `Lifecycle`

Every subscriber first receives a `CurrentDevicesSnapshot` describing the controllers connected at
that moment, then live events. History is not replayed, so a late subscriber sees the current
devices rather than the connections that produced them.

| Event | Meaning |
| --- | --- |
| `CurrentDevicesSnapshot` | Devices connected at the moment of subscription. |
| `DeviceConnected` | A device was acquired and is being polled. `FromCache` is `true` when it came from the device cache rather than a native scan. |
| `DeviceDisconnected` | A device stopped being polled, with the reason (`device disconnected`, `polling failed`, `watcher stopped`). |
| `ScanStarted` / `ScanProgress` / `ScanCompleted` | Native enumeration boundaries. `ScanProgress` ticks about once a second while a scan runs, so a UI can show that a slow scan is still alive. Each carries a `ScanReason`: `Startup`, `UsbDeviceChanged`, or `Recovery`. |
| `UsbDeviceChanged` | A USB controller device was added or removed, with the name, device path, and VID/PID where they could be resolved. |
| `WatcherError` | A recoverable failure, carrying a `WatcherErrorKind` (`UsbWatcher`, `CacheRead`, `CacheWrite`, `Enumeration`, `Acquisition`, `Polling`), a message, the exception, and the device when one is implicated. |

Recoverable failures arrive as `WatcherError` values instead of terminating either observable, so a
subscription set up once stays alive for the life of the watcher.

### `Inputs`

| Event | Payload |
| --- | --- |
| `ButtonPressedEvent` | `ButtonNumber` — released-to-pressed transitions only, so holding a button emits once. |
| `AxisMovedEvent` | `AxisNumber`, `AxisName`, `Value` normalized to `-1.0..1.0`, plus the `Baseline` it was measured against and the signed `Difference`. |
| `PovChangedEvent` | `PovNumber` and `RawValue` in hundredths of a degree; `Degrees` converts it. `-1` means centred or released. |

All input events share `DeviceId` (the DirectInput instance GUID), `DeviceName`, and a UTC
`Timestamp`. Use `DeviceId` rather than the name to tell devices apart — two identical controllers
report the same name.

Input events are delivered on each device's own polling thread through a synchronized subject, so
handlers run one at a time but not on the thread that called `StartAsync`. Marshal to your UI thread
yourself, and keep handlers short — a slow handler delays that device's next poll.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `PollFrequency` | `15` | Polls per second per device. Must be greater than 0. |
| `AxisChangeThreshold` | `0.25` | Normalized movement from the baseline that emits an `AxisMovedEvent`. Must be greater than 0 and at most 2. |
| `AxisResetThreshold` | `0.20` | Normalized distance from the baseline at which an axis rearms. Must be non-negative and below `AxisChangeThreshold`. |
| `AxisBaselineCalibrationDuration` | `1s` | How long a freshly acquired device's readings are treated as baseline calibration. Axes emit nothing during this window. |
| `DeviceCachePath` | `null` | JSON file used to remember discovered devices between runs. Null or empty disables caching. |
| `Whitelist` | empty | `VidPid` pairs to watch exclusively. |
| `Blacklist` | empty | `VidPid` pairs to ignore. |

Options are validated when they are first resolved; invalid values throw
`OptionsValidationException` rather than failing quietly at registration.

Registration uses the standard options pipeline — defaults first, then anything you bound
beforehand, then the `AddDirectInputWatcher` setup action last, so the setup action wins:

```csharp
services
    .AddOptions<DirectInputWatcherOptions>()
    .Bind(configuration.GetSection(DirectInputWatcherOptions.DefaultSectionName));
services.AddDirectInputWatcher();
```

`DirectInputWatcher.Configuration` provides an overload that does the binding for you, kept in a
separate project so the core library takes no configuration dependency:

```csharp
using DirectInputWatcher.Configuration;

services.AddDirectInputWatcher(configuration);
```

```json
{
  "DirectInputWatcher": {
    "PollFrequency": 15,
    "AxisChangeThreshold": 0.25,
    "AxisResetThreshold": 0.20,
    "AxisBaselineCalibrationDuration": "00:00:01",
    "DeviceCachePath": "C:\\ProgramData\\MyApp\\devices.json",
    "Whitelist": [ "346E:0003" ],
    "Blacklist": []
  }
}
```

### Filtering

`VidPid` values are hex `VID:PID` pairs, written either as `"346E:0003"` in configuration or as
`new VidPid(0x346E, 0x0003)` in code. The blacklist always wins. When the whitelist is non-empty,
only listed devices are watched and devices that do not report a VID/PID at all are excluded.

Filtering saves the work of acquiring and polling devices you do not care about, but DirectInput
cannot enumerate by VID/PID natively, so a first uncached discovery still runs the full scan.

## How it works

**Discovery.** `StartAsync` immediately restores devices from the cache file and tries to acquire
them, then kicks off a native DirectInput enumeration in the background. Native enumeration can take
seconds; the cache is what makes the library useful within one. Cached descriptors count as
connected only once acquisition actually succeeds, and the background scan reconciles by instance
GUID without duplicating them. The cache is rewritten after every scan through a temp-then-move, so
a crash mid-write cannot leave a truncated file.

**Hot-plug.** USB additions and removals are observed through WMI `__InstanceCreationEvent` and
`__InstanceDeletionEvent` notifications for `Win32_USBControllerDevice` — not by polling. Each
notification queues a rescan. Scan requests coalesce through a one-slot channel, so a burst of USB
events during a hub reset collapses into a single scan. If the WMI watcher itself dies it is
restarted after a delay and a `WatcherError` of kind `UsbWatcher` is emitted. Bluetooth and purely
virtual device changes do not trigger a rescan.

**Polling.** Each device gets its own background thread and Rx event-loop scheduler, so one slow or
misbehaving controller cannot stall the others. A failed poll triggers one reacquire attempt, and a
device that keeps failing is dropped with a `WatcherError` of kind `Polling` followed by a recovery
scan that picks it back up if it returns.

**Axes.** Raw values are requested in a `-1000..1000` range and normalized to `-1.0..1.0`. Devices
that reject that range keep their own, which is normalized the same way. For
`AxisBaselineCalibrationDuration` after a device is acquired, readings only establish a resting
baseline and emit nothing — that is what stops a drifting or off-centre stick from flooding the
stream. After that, movement of `AxisChangeThreshold` from the baseline emits once, and the axis
rearms after returning within `AxisResetThreshold`. The gap between the two is hysteresis: without
it, an axis resting near the threshold would chatter. Pushing an axis the other way past the
threshold emits again without needing to recentre first.

X, Y, Z, Rx, Ry, Rz and the first two sliders are read; any other axis object is skipped with a
logged warning. Up to 128 buttons and 4 POV hats per device are read.

**Window handle.** DirectInput needs a window to set a cooperative level against. The console window
is used when there is one; otherwise the library creates a hidden window on its own message-pumping
thread. Devices are acquired as background and non-exclusive, so input is reported whether or not
your application has focus.

## Layout

| Folder | Contents |
| --- | --- |
| `Configuration` | Options, `VidPid`, and the DI registration extension. |
| `Events` | Public lifecycle and controller-input event records. |
| `Models` | The public device descriptor and internal polling snapshots. |
| `DirectInput` | Native enumeration, device sessions, cache, filter, normalization, cooperative window. |
| `Reactive` | Input-detection pipeline and the lifecycle event hub. |
| `ReactiveExtensions` | `RestartOnError`, used to keep the USB watcher alive. |
| `Usb` | WMI change observation and VID/PID parsing. |
| `Services` | `DirectInputWatcherService`, the orchestrator behind `IDirectInputWatcher`. |

Everything except the options, events, descriptor, and registration extension is `internal`;
`DirectInputWatcher.Tests` and `DirectInputWatcher.Configuration` see internals through
`InternalsVisibleTo`.

See [AGENTS.md](../AGENTS.md) for the architectural rules that keep the library free of console and
hosting concerns.

## Building and testing

```bash
dotnet test DScanner.slnx -c Release
```

The tests cover the Rx detection pipeline, lifecycle publication, filtering, caching, normalization,
options registration, and VID/PID parsing — everything that does not need a physical controller. For
hardware-facing changes, also verify by hand on Windows: cached startup, background reconciliation,
USB hot-plug, live input events, and clean shutdown.
