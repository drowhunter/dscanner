# DScanner

A Windows `.NET 10` console application that monitors DirectInput game controllers at 15 Hz. It uses dependency injection, Reactive Extensions, a dedicated terminal UI, and `ILogger` file logging to identify button presses, significant axis movement, and POV hat changes.

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
