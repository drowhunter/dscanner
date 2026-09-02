# DScanner Architecture

## Projects

- `DirectInputWatcher`: reusable Windows `.NET 10` class library for DirectInput discovery, USB hot-plug reconciliation, controller polling, normalization, baseline calibration, caching, filtering, and Rx event streams.
- `DScanner`: console host containing System.CommandLine, Generic Host integration, Serilog setup, terminal rendering, keyboard input, and the interactive control-mapping mode.
- `DirectInputWatcher.Tests`: reusable watcher, Rx, configuration, filtering, lifecycle, and cache tests.
- `DScanner.Tests`: console-specific command-line, UI-label, key-pump, and control-mapping tests.

`DirectInputWatcher` groups files by responsibility:

- `Configuration`: options-pattern registration and VID/PID values.
- `Events`: public lifecycle and controller-input events.
- `Models`: device descriptors and internal polling snapshots.
- `DirectInput`: native enumeration, acquisition, cache, filtering, and normalization.
- `Reactive`: input detection and lifecycle state publication.
- `Usb`: WMI change observation and USB identity parsing.
- `Services`: watcher orchestration.

## DirectInputWatcher contract

Register the watcher with `AddDirectInputWatcher`, resolve `IDirectInputWatcher`, subscribe to `Lifecycle` and `Inputs`, then explicitly call `StartAsync` and `StopAsync`.

The library does not reference `Microsoft.Extensions.Hosting` and does not start itself. It exposes:

- `Lifecycle`: each subscriber first receives `CurrentDevicesSnapshot` containing only controllers currently connected, then live connection, disconnection, USB, scan progress, completion, and recoverable error events.
- `Inputs`: button, normalized axis, and POV events.

Historical lifecycle events are not replayed. Recoverable failures are emitted as `WatcherError` values instead of terminating either observable.

## Console input

`ConsoleKeyPump` is the only reader of console keystrokes. It checks Ctrl+Q first and shuts the
host down, then offers the key to the innermost active `IConsoleKeyDispatcher.Capture` handler.
Never call `Console.ReadKey` or `Console.ReadLine` elsewhere: the pump would race it for keys and
`ConsoleUiService` would paint over the echo. Read a line through `IConsoleUi.ReadLabelAsync`,
which edits on the prompt row rendered directly above the footer.

## Mapping mode

`--map` adds `DeviceMappingService`, which loops: read a label, capture the next input event,
append it to that device's JSON file. Notes that are easy to get wrong:

- The first captured event locks the session to one device; later events from others are ignored.
- `PovChangedEvent` with `RawValue` of `-1` is the release, not a binding, and is skipped.
- A settle delay after each capture stops a button release or axis recentre being read as the next binding.
- Axes emit nothing until baseline calibration finishes, so the first prompt waits for it.
- The prompt is shown from inside the capture, after subscribing, so no input can be missed.
- Files are rewritten in full after every capture using an atomic temp-then-move, and re-binding an
  existing control replaces its label instead of appending a duplicate.

## Configuration

`DirectInputWatcherOptions` supports:

- Poll frequency.
- Axis change/reset thresholds.
- Initial axis baseline calibration duration.
- Optional cache file path; no path disables persistent caching.
- VID/PID whitelist and blacklist. Blacklist wins, and a non-empty whitelist excludes unlisted or unidentified devices.

Configuration uses the standard options pattern. Consumers may bind or configure `DirectInputWatcherOptions` before registration; the optional `AddDirectInputWatcher(options => ...)` setup action runs as post-configuration and overrides earlier values.

DirectInput cannot natively enumerate by VID/PID. Filters avoid acquiring and polling unrelated devices and allow matching cached descriptors to start immediately, but first-time uncached discovery still invokes native DirectInput enumeration.

## Runtime behavior

- Cached descriptors count as connected only after successful DirectInput acquisition.
- Native discovery runs in the background and reconciles by instance GUID without duplicating cached connections.
- WMI `Win32_USBControllerDevice` creation/deletion events trigger throttled automatic reconciliation.
- Controllers poll independently on dedicated Rx event-loop schedulers.
- Axes normalize to `-1..1`, establish a settled startup baseline, trigger at the configured baseline-relative threshold, and use a lower reset threshold for hysteresis.
- Buttons emit released-to-pressed transitions.
- POV values emit changes in hundredths of degrees; public events expose degrees and preserve `-1` as centered/depressed.

## Separation rules

- Keep DirectInput, WMI, Rx state, cache, and reusable event models in `DirectInputWatcher`.
- Keep all console strings, colors, layout, key handling, mapping files, logging-provider configuration, and host lifetime in `DScanner`.
- The library may depend on Microsoft DI/configuration/options/logging, but not Hosting, System.CommandLine, Serilog, or console UI types.
- Use typed lifecycle/input events across the project boundary; do not add UI callbacks to the library.

## Validation

Run:

```powershell
dotnet test DScanner.slnx -c Release
```

For hardware changes, also verify cached startup, background native reconciliation, USB hot-plug, input events, and clean shutdown on Windows.
