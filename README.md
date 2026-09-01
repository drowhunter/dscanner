# DScanner

A Windows `.NET 10` console application that monitors DirectInput game controllers at 15 Hz. It uses dependency injection, Reactive Extensions, a dedicated terminal UI, and `ILogger` file logging to identify button presses, significant axis movement, and POV hat changes.

The solution also contains the reusable `DirectInputWatcher` class library. `DScanner` references that library and contains only console hosting, command-line, logging-provider, and UI concerns.

The console is a dedicated terminal UI:

- Line 1 displays the application title.
- Line 3 displays DirectInput enumeration progress.
- Lines 5 through the second-last line scroll controller and USB events.
- The last line permanently displays `Press Ctrl+Q to quit`.

Controller names include the first eight characters of their DirectInput instance GUID, such as `Identical Joystick [ID 31a184c3]`, so identical devices remain distinguishable.

Press `Ctrl+Q` while the scanner console is focused to stop the application cleanly. `ILogger` output is written to the log file rather than mixed into the terminal UI.

## Run

```powershell
dotnet run --project .\DScanner\DScanner.csproj
```

Show all command-line options:

```powershell
dotnet run --project .\DScanner\DScanner.csproj -- --help
```

Example:

```powershell
dotnet run --project .\DScanner\DScanner.csproj -- `
  --poll-frequency-hz 20 `
  --axis-change-threshold 0.30 `
  --axis-reset-threshold 0.15
```

Each axis is normalized to `-1.0..1.0`. During the first second after a controller is acquired, its latest readings establish the resting baseline without producing axis events. The application then logs once when movement from that settled baseline reaches `0.25`, and rearms after returning within `0.20`.

## Mapping controls

`--map` runs an interactive labelling loop instead of watching passively. Type a label, press
`Enter`, then press the button, move the axis, or press the POV hat you want that label bound to.
The loop repeats until you submit an empty label. `Esc` skips the control currently being waited
for, and `Ctrl+Q` still quits at any point.

```powershell
dotnet run --project .\DScanner\DScanner.csproj -- --map
```

The first control you press decides which device the session maps; input from any other
controller is ignored with a warning. Bindings are written to `<Device Name>.json` in the current
directory after every capture, so an interrupted session keeps everything captured so far. Use
`--map-output <DIR>` to change the directory, or `--map-file <PATH>` to name the file yourself —
useful when two identical controllers would otherwise share one file.

```json
[
  { "label": "Fire", "buttonNumber": 0, "type": "button" },
  { "label": "Throttle Up", "buttonNumber": 2, "type": "axis", "direction": 1 },
  { "label": "Hat Up", "buttonNumber": 0, "type": "pov" }
]
```

`buttonNumber` holds the button, axis, or POV number, depending on `type`. Axis entries also carry
`direction` (`-1` or `1`) so that pushing one axis each way produces two distinct bindings.
Re-binding a control that is already mapped replaces its label rather than adding a duplicate, and
an existing mapping file is loaded and extended rather than overwritten.

Because axes emit nothing until their resting baseline settles, the first prompt appears once a
controller is connected and calibrated.

Fast enumeration avoids Windows-wide XInput detection and per-device DirectInput property probes. XInput-compatible controllers can therefore appear in the results.

Successful discovery is cached in `C:\ProgramData\DScanner\devices.json`. On later runs, cached controllers are opened immediately while the slow native DirectInput enumeration refreshes the cache in the background.
An asterisk prefixes cached discovery events, for example `* Found Identical Joystick [ID 31a184c3]`.
The terminal renders all `Found` events in bright green.
For button and axis events, only the controller name is highlighted in pink-like bright red; the instance ID and event details remain white.

POV values are displayed in degrees. A value of `-1` means the hat is depressed/centered.

Settings can be changed in `DScanner\appsettings.json`, through standard .NET configuration providers under the `Scanner` section, or with command-line options. Command-line values take precedence.

USB device changes are detected through Rx streams around Windows Management Instrumentation `__InstanceCreationEvent` and `__InstanceDeletionEvent` notifications for `Win32_USBControllerDevice` associations rather than periodic DirectInput enumeration. Bluetooth and purely virtual device changes do not trigger a refresh.

Each run overwrites `C:\ProgramData\DScanner\logs\dscanner.log`, so the file contains only the current run. The same messages are also written to the console when the application is run interactively.

While DirectInput enumerates controllers, the console displays `Enumerating DirectInput devices` and adds one period per second until discovery completes.

## DirectInputWatcher library

`DirectInputWatcher` exposes a manually controlled injectable service with separate lifecycle and input observables. It does not depend on `Microsoft.Extensions.Hosting` and does not start automatically.

```csharp
using DirectInputWatcher;

services
    .AddOptions<DirectInputWatcherOptions>()
    .Bind(configuration.GetSection("DirectInputWatcher"));

services.AddDirectInputWatcher(options =>
{
    options.DeviceCachePath = cachePath;
    options.PollFrequency = 15;
    options.AxisChangeThreshold = 0.25;
    options.AxisResetThreshold = 0.20;
    options.AxisBaselineCalibrationDuration = TimeSpan.FromSeconds(1);
    options.Whitelist.Add(new VidPid(0x346E, 0x0003));
    options.Blacklist.Add(new VidPid(0x045E, 0x02FF));
});

IDirectInputWatcher watcher =
    serviceProvider.GetRequiredService<IDirectInputWatcher>();

using IDisposable lifecycle = watcher.Lifecycle.Subscribe(HandleLifecycle);
using IDisposable inputs = watcher.Inputs.Subscribe(HandleInput);

await watcher.StartAsync(cancellationToken);
// Application runs...
await watcher.StopAsync(cancellationToken);
```

It can also use only defaults or previously configured options:

```csharp
services
    .AddOptions<DirectInputWatcherOptions>()
    .Bind(configuration.GetSection("DirectInputWatcher"));
services.AddDirectInputWatcher();
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

`AddDirectInputWatcher` uses the standard options pipeline. A new options object starts with sensible defaults, previously registered configuration is applied, and the optional setup action runs last so it can override any value directly. Caching is disabled when `DeviceCachePath` is omitted. Blacklist entries always win; when the whitelist is non-empty, only listed devices are monitored.

Every `Lifecycle` subscriber immediately receives one `CurrentDevicesSnapshot` containing only controllers connected at that time. It then receives live `DeviceConnected`, `DeviceDisconnected`, `UsbDeviceChanged`, `ScanStarted`, `ScanProgress`, `ScanCompleted`, and recoverable `WatcherError` events. Historical disconnections and progress events are not replayed.

USB devices connected later are detected through WMI and automatically trigger DirectInput reconciliation. VID/PID filters reduce acquisition and polling work and accelerate cached startup, but DirectInput does not support native VID/PID-filtered enumeration, so an uncached discovery still requires the full native enumeration call.
