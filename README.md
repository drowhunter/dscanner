# DScanner

A Windows console application that watches DirectInput game controllers and tells you exactly which
control you just touched. Press a button, nudge an axis, or click a POV hat, and the terminal names
it — device, control kind, and number.

It solves a specific problem: working out what a flight stick, wheel, pedal set, or button box
actually reports, so you can bind it in a game, a sim, or your own code. `--map` takes that further
and records your own labels for each control into a JSON file.

The repository also contains `DirectInputWatcher`, the reusable class library that does the real
work. `DScanner` is a thin console host over it.

## Requirements

- Windows (DirectInput and WMI are Windows-only)
- .NET 10 SDK

## Quick start

```bash
dotnet run --project .\DScanner\DScanner.csproj
```

Press `Ctrl+Q` while the console has focus to stop cleanly.

Show every option:

```bash
dotnet run --project .\DScanner\DScanner.csproj -- --help
```

## The terminal UI

The console is a fixed-layout terminal UI, not a scrolling log:

- Line 1 — application title.
- Line 2 — status: device count, poll frequency, enumeration mode.
- Line 3 — DirectInput enumeration progress, gaining a period per second while it scans.
- Line 5 to the second-last line — a scrolling window of controller and USB events.
- Last line — `Press Ctrl+Q to quit`.

Controller names carry the first eight characters of their DirectInput instance GUID, such as
`Identical Joystick [ID 31a184c3]`, so two of the same model stay distinguishable. Each device is
assigned its own colour, and in button and axis events only the device name is coloured — the
instance ID and the event detail stay white.

`ILogger` output goes to the log file rather than being mixed into the UI, so the layout never gets
trampled.

## Watch mode (default)

### Discovery

Successful discoveries are cached in `C:\ProgramData\DScanner\devices.json`. On later runs the
cached controllers are opened immediately while the slow native DirectInput enumeration refreshes
the cache in the background — so the app is useful within a second instead of waiting for a full
scan. Cached discovery events are prefixed with an asterisk: `* Found Identical Joystick [ID
31a184c3]`. A cached device counts as connected only once it has actually been acquired.

Enumeration deliberately skips Windows-wide XInput detection and per-device property probes, which
is what makes it fast. The trade-off is that XInput-compatible controllers (Xbox pads) can appear in
the results alongside true DirectInput devices.

USB hot-plug is detected through WMI `__InstanceCreationEvent` and `__InstanceDeletionEvent`
notifications for `Win32_USBControllerDevice`, not by polling — plug a stick in mid-run and it is
picked up. Bluetooth and purely virtual device changes do not trigger a refresh.

### What counts as an event

- **Buttons** report released-to-pressed transitions only, so holding a button logs once.
- **Axes** are normalized to `-1.0..1.0`. For the first second after a controller is acquired its
  readings establish a resting baseline and produce no events — this is what stops a drifting or
  off-centre stick from flooding the log. After that, movement of `0.25` from the baseline logs
  once, and the axis rearms after returning within `0.20`. That gap is hysteresis: without it an
  axis resting near the threshold would chatter.
- **POV hats** report changes in degrees. `-1` means centred or released.

Thresholds are tunable — see the options below.

## Mapping mode (`--map`)

```bash
dotnet run --project .\DScanner\DScanner.csproj -- --map
```

Instead of watching passively, this walks you through labelling controls:

1. If exactly one controller is connected it is selected automatically. If several are, you get a
   numbered list and pick one. Everything from here on is scoped to that device.
2. Type a label and press `Enter`.
3. Press the button, move the axis, or click the POV hat you want that label bound to.
4. Repeat. Submit an empty label to finish.

`Esc` skips the control currently being waited on. `Ctrl+Q` still quits at any point. Input from any
other controller is ignored, with a warning the first time each stray device appears.

Bindings are written to `<Device Name>.json` in the current directory, rewritten in full after every
single capture, so an interrupted session keeps everything captured so far. An existing file is
loaded and extended rather than overwritten, and re-binding a control that is already mapped
replaces its label instead of adding a duplicate.

```json
[
  { "label": "Fire", "index": 0, "value": 1, "type": "button" },
  { "label": "Throttle Up (Y)", "index": 1, "value": 1, "type": "axis" },
  { "label": "Hat Up", "index": 0, "value": 0, "type": "pov" }
]
```

- `index` is the control number on the device — button index, axis index, or POV index.
- `value` depends on `type`: buttons record `1`; axes record the sign of the axis position when it
  was captured, `-1` or `1`, so pushing one axis each way gives two distinct bindings; POVs record
  the hat's position in degrees, or `-1` for centred.
- Axis labels get the DirectInput axis name appended automatically — `X`, `Y`, `Z`, `Rx`, `Ry`,
  `Rz`, or a slider — so `Throttle Up` on the Y axis is stored as `Throttle Up (Y)`.

Two details worth knowing. Some controllers expose a trigger as both a button and an axis; when both
fire together, mapping waits a 100 ms race window and prefers the axis, since that is almost always
the binding you wanted. And because axes emit nothing until their baseline settles, the first prompt
waits for calibration to finish before appearing.

If two identical controllers would otherwise share one file name, use `--map-file` to name the file
yourself.

`--map` needs an interactive console; with input redirected it exits with an error rather than
hanging on a prompt nobody can answer.

## Command-line options

| Option | Description |
| --- | --- |
| `--poll-frequency-hz <HZ>` | Polling frequency in samples per second. Default `15`. Also `--poll-frequency`. |
| `--axis-change-threshold <VALUE>` | Normalized movement from baseline that triggers an event. Range `>0` to `2`. Default `0.25`. |
| `--axis-reset-threshold <VALUE>` | Normalized distance from baseline that rearms an axis. Must be below the change threshold. Default `0.20`. |
| `--map` | Run interactive control mapping instead of watching. |
| `--map-output <DIR>` | Directory for generated mapping files. Default: the current directory. |
| `--map-file <PATH>` | Explicit mapping file path, overriding the device-derived name. |

Example:

```bash
dotnet run --project .\DScanner\DScanner.csproj -- --poll-frequency-hz 20 --axis-change-threshold 0.30 --axis-reset-threshold 0.15
```

## Configuration

Settings can come from `DScanner\appsettings.json`, from any standard .NET configuration provider
under the `DirectInputWatcher` section, or from the command line. Command-line values win.

```json
{
  "DirectInputWatcher": {
    "PollFrequency": 15,
    "AxisChangeThreshold": 0.25,
    "AxisResetThreshold": 0.20,
    "AxisBaselineCalibrationDuration": "00:00:01",
    "DeviceCachePath": "C:\\ProgramData\\DScanner\\devices.json",
    "Whitelist": [ "346E:0003" ],
    "Blacklist": []
  }
}
```

`Whitelist` and `Blacklist` take `VID:PID` pairs in hex. The blacklist always wins; when the
whitelist is non-empty, only listed devices are watched and unidentified ones are excluded. Filters
cut out the work of acquiring and polling devices you do not care about, but DirectInput cannot
enumerate by VID/PID natively, so a first uncached discovery still runs the full native scan.

## Logging

Each run truncates and rewrites `C:\ProgramData\DScanner\logs\dscanner.log`, so the file only ever
holds the current run. The file is opened shared, so you can tail it while the app runs:

```bash
Get-Content -Wait C:\ProgramData\DScanner\logs\dscanner.log
```

Log lines carry more detail than the terminal — axis entries include the baseline and the signed
change, not just the current value.

## Repository layout

| Project | Purpose |
| --- | --- |
| `DirectInputWatcher` | The reusable library: discovery, USB reconciliation, polling, normalization, calibration, caching, filtering, and Rx event streams. |
| `DirectInputWatcher.Configuration` | `IConfiguration`-binding registration overload, kept separate so the core library stays free of a configuration dependency. |
| `DScanner` | The console host: command line, terminal UI, keyboard input, logging setup, and mapping mode. |
| `DirectInputWatcher.Tests` | Library tests — watcher, Rx pipeline, configuration, filtering, lifecycle, cache. |
| `DScanner.Tests` | Console tests — command line, device labels, key pump, control mapping. |

See [AGENTS.md](AGENTS.md) for the architectural rules that keep the split clean.

## Using the library directly

`DirectInputWatcher` is an injectable service with separate lifecycle and input observables. It does
not reference `Microsoft.Extensions.Hosting` and never starts itself — you call `StartAsync`.

```csharp
using DirectInputWatcher;

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

Registration uses the standard options pipeline: defaults first, then any configuration you bound
beforehand, then the setup action last so it can override anything. Omitting `DeviceCachePath`
disables caching.

```csharp
services
    .AddOptions<DirectInputWatcherOptions>()
    .Bind(configuration.GetSection("DirectInputWatcher"));
services.AddDirectInputWatcher();
```

Every `Lifecycle` subscriber immediately receives one `CurrentDevicesSnapshot` holding only the
controllers connected at that moment, then live `DeviceConnected`, `DeviceDisconnected`,
`UsbDeviceChanged`, `ScanStarted`, `ScanProgress`, `ScanCompleted`, and recoverable `WatcherError`
events. History is not replayed. Recoverable failures arrive as `WatcherError` values rather than
terminating either observable.

`Inputs` carries `ButtonPressedEvent`, `AxisMovedEvent`, and `PovChangedEvent`.

## Building and testing

```bash
dotnet test DScanner.slnx -c Release
```

For hardware-facing changes, also verify by hand on Windows: cached startup, background native
reconciliation, USB hot-plug, live input events, and clean shutdown.
