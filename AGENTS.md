# DirectInput Scanner Implementation Plan

## Goal

Build a Windows console application targeting `.NET 10` that:

- Uses the .NET Generic Host and dependency injection.
- Uses Reactive Extensions (`System.Reactive`) to process controller state.
- Enumerates attached DirectInput game controllers, including gamepads, joysticks, flight controls, and steering wheels.
- Uses fast DirectInput enumeration; XInput-compatible devices may also be reported.
- Logs input through `ILogger`.
- Logs button press transitions, axis threshold transitions, and POV hat changes.

## Platform and Dependencies

- Target framework: `net10.0-windows`.
- Output type: console executable.
- Enable nullable reference types and implicit global usings.
- NuGet packages:
  - `Microsoft.Extensions.Hosting`
  - `System.Reactive`
  - `Vortice.DirectInput`
  - A Windows device-information package only if required for reliable XInput filtering.
- Use `Vortice.DirectInput` rather than legacy SharpDX packages because SharpDX is archived.

## Proposed Project Structure

```text
DScanner/
|-- DScanner.csproj
|-- Program.cs
|-- Configuration/
|   `-- ScannerOptions.cs
|-- DirectInput/
|   |-- DirectInputDeviceEnumerator.cs
|   |-- DirectInputDeviceSession.cs
|   |-- XInputDeviceFilter.cs
|   `-- DirectInputValueNormalizer.cs
|-- Models/
|   |-- ControllerSnapshot.cs
|   `-- ControllerInputEvent.cs
|-- Services/
|   `-- ControllerScannerService.cs
`-- AGENTS.md
```

Keep interfaces only where they provide a test seam, especially for device enumeration, polling, time, and logging behavior.

## Application Composition

1. In `Program.cs`, create a Generic Host with `Host.CreateApplicationBuilder(args)`.
2. Configure console logging through the host logging pipeline.
3. Bind scanner settings to `ScannerOptions`.
4. Register the DirectInput enumerator, XInput filter, polling/session factory, normalizer, and scanner service with dependency injection.
5. Register `ControllerScannerService` as an `IHostedService` or `BackgroundService`.
6. Run the host until Ctrl+C or process termination requests cancellation.
7. Dispose every DirectInput device, Rx subscription, scheduler, and DirectInput context during shutdown.

## Configuration

Define options with these defaults:

```text
PollFrequencyHz = 15
DeviceRefreshInterval = 2 seconds
AxisChangeThreshold = 0.25
AxisResetThreshold = 0.20
```

Poll each device at 15 Hz, approximately once every `66.67 ms`. The lower reset threshold supplies hysteresis so noisy input near a significant change does not repeatedly log. Validate that thresholds are normalized value differences between `0.0` and `2.0`, and that the reset threshold is lower than the activation threshold.

## DirectInput Enumeration

1. Initialize DirectInput once for the process.
2. Enumerate attached devices in the DirectInput game-controller class.
3. Include DirectInput device types representing gamepads, joysticks, driving controllers, flight controllers, and first-person controllers where exposed by the library.
4. Deduplicate devices by stable instance GUID.
5. Capture the DirectInput instance GUID, product GUID, instance name, product name, and device subtype.
6. Refresh enumeration periodically so hot-plugged devices are added and disconnected devices are removed.
7. Use the product name when available; otherwise use the instance name. Fall back to the instance GUID only if both names are empty.

## XInput Tradeoff

DirectInput enumeration can expose XInput controllers. The original Windows-wide PnP filtering and per-device property inspection caused unacceptable startup delays, so the runtime scanner intentionally skips XInput exclusion.

1. Enumerate only `DeviceClass.GameControl` with `AttachedOnly`.
2. Do not query all `Win32_PnPEntity` instances during discovery.
3. Do not create each DirectInput device merely to inspect interface and VID/PID properties.
4. Derive USB VID/PID directly from the DirectInput product GUID.
5. Accept that an XInput-compatible controller may appear in the results.

## Device Setup and Polling

For each accepted DirectInput device:

1. Create and initialize a device session.
2. Set cooperative behavior appropriate for a background console scanner: non-exclusive and background access.
3. Enumerate device objects to build stable, zero-based logical indexes for:
   - Buttons
   - Axes
   - POV hats
4. Configure every available axis to a known DirectInput range, preferably `-1000..1000`.
5. Acquire the device and poll it on the configured interval.
6. If polling reports input loss or a not-acquired state, attempt reacquisition with bounded retry behavior.
7. If the device is disconnected, terminate that session and allow the refresh loop to rediscover it later.
8. Report acquisition and polling failures through `ILogger`; do not hide exceptions or emit success-shaped fallback state.

## Snapshot Model

Convert every successful poll into an immutable `ControllerSnapshot` containing:

- Device instance GUID
- Device display name
- Timestamp
- Button states indexed from zero
- Named/indexed normalized axis values
- POV values indexed from zero

Keep native DirectInput objects inside the device adapter. The Rx pipeline should operate on application-owned immutable values so state comparisons are safe.

## Axis Normalization

1. Scale every reported axis value into `-1.0..1.0`, regardless of whether the native axis is signed, unsigned, centered, a trigger, or a pedal.
2. Do not assume that `0.0` is the resting position. Some controls rest at `1.0` and move toward `0.0` or `-1.0` when pressed.
3. Capture the first valid normalized value for every axis after device acquisition as that axis's baseline.
4. Detect movement using the absolute normalized difference from the captured baseline: `abs(current - baseline)`.
5. Treat a difference of `0.25` or greater as significant. This deliberately follows the expected behavior that movement from `1.0` to `0.99` is ignored while movement from `1.0` to `0.75` triggers an event.
6. Clamp malformed or out-of-range native values to `-1.0..1.0` and log unexpected device metadata at `Debug` or `Warning` level as appropriate.
7. Assign stable axis numbers from the enumerated device-object order and retain the native semantic name, such as `X`, `Y`, `Z`, `Rx`, `Ry`, `Rz`, `Slider0`, or `Slider1`, for diagnostics.

## Reactive Extensions Pipeline

Create one observable stream per active device:

1. Produce poll ticks at 15 Hz using `Observable.Interval` with the configured frequency and a dedicated scheduler.
2. Poll the device once per tick and emit a `ControllerSnapshot`.
3. Serialize polling per device; never allow overlapping polls.
4. Use an Rx `Scan` state machine per axis to retain its startup baseline, current normalized value, and armed/active state.
5. Apply Rx `Where` filtering after `Scan` so only normalized changes that meet the configured baseline-relative threshold become axis events.
6. Use `DistinctUntilChanged` for raw normalized samples where useful, but do not rely on a tolerance-based comparer alone because approximate equality is not transitive and can produce an unstable reference value.
7. Pair button and POV snapshots with `Buffer(2, 1)`, `Pairwise`, or their own `Scan` state to detect transitions.
8. Convert qualifying state changes into typed events:
   - `ButtonPressed`
   - `AxisThresholdCrossed`
   - `PovChanged`
9. Merge all device event streams into one scanner-level stream.
10. End individual streams when their device is disconnected without stopping streams for other devices.
11. Dispose subscriptions when devices disappear or the host shuts down.

Do not use Rx merely as a timer wrapper; state transitions, filtering, stream merging, error boundaries, and subscription lifetimes should be represented in the observable pipeline.

## Event Rules

### Buttons

- Log only the transition from released to pressed.
- Do not log continuously while a button remains held.
- Button numbers are zero-based unless implementation testing proves the native API/user expectation requires one-based labels; use one convention consistently in code, logs, and tests.

### Axes

- The first valid sample after acquisition establishes each axis's resting baseline and does not log.
- An axis is active when `abs(currentNormalizedValue - baselineNormalizedValue) >= 0.25`.
- Log once when the axis crosses from within the baseline region into the active region.
- Do not log every poll while the axis remains at least `0.25` away from its baseline.
- Rearm the axis only after its difference from the baseline falls below the reset threshold, default `0.20`.
- Keep the startup baseline fixed for the lifetime of the acquired device session; do not drift it in response to small changes.
- Re-establish baselines when a device is disconnected and reacquired as a new session.
- Log the current signed normalized value and its signed difference from the baseline so direction is visible.
- A movement to the opposite side of the baseline that differs by at least `0.25` should generate a new event even if no sampled value entered the reset region.

### POV Hats

- DirectInput POV values are normally expressed in hundredths of a degree.
- Preserve `-1` as the depressed/centered value.
- Convert every non-negative POV value to degrees by dividing by `100.0`.
- Log only when the POV value changes.
- Values should therefore be reported as examples such as `-1`, `0`, `45`, `90`, `180`, or `270` degrees.

## Logging Contract

Use structured `ILogger` message templates. Required information:

```text
Button: "{DeviceName}" button {ButtonNumber} pressed
Axis:   "{DeviceName}" axis {AxisNumber} ({AxisName}) moved to {NormalizedValue}; baseline {BaselineValue}; change {NormalizedDifference}
POV:    "{DeviceName}" POV {PovNumber} moved to {Degrees} degrees
```

Additional lifecycle logs should cover:

- Device discovered
- XInput device excluded
- Device acquired
- Device disconnected
- Device reacquisition attempts
- Polling or metadata failures

Use `Information` for input and device lifecycle events, `Debug` for detailed filtering/metadata, `Warning` for recoverable device failures, and `Error` for failures that terminate a device session or the scanner.

## Error Handling and Lifetime

- Respect the host cancellation token throughout enumeration, polling, retry delays, and Rx subscriptions.
- Scope errors to the affected device whenever possible.
- Apply bounded retry/backoff for transient acquisition failures.
- Do not use broad empty `catch` blocks.
- Ensure COM/native resources are released exactly once.
- Protect shared device-session state from refresh-loop and shutdown races.
- Make add/remove operations idempotent by instance GUID.

## Testing Plan

### Unit Tests

- Button release-to-press logs once.
- Held buttons do not repeat.
- Button release alone does not log.
- Axis values normalize correctly to `-1.0..1.0` at native minimum, midpoint, maximum, and out-of-range inputs.
- The first valid sample establishes the axis baseline without logging.
- A resting baseline of `1.0` followed by `0.99` does not log.
- A resting baseline of `1.0` followed by `0.75` logs once.
- A resting baseline of `-0.4` followed by `-0.15` logs once.
- Axis movement with a baseline-relative difference below `0.25` does not log.
- Axis movement with a baseline-relative difference exactly equal to `0.25` logs once.
- Repeated samples beyond the threshold do not repeat while the axis remains active.
- Axis jitter around the activation threshold does not spam because of hysteresis.
- Axis returning to within less than `0.20` of its baseline rearms the event.
- Axis direction reversal across the baseline beyond the threshold emits a new event.
- POV `-1` remains `-1`.
- POV values `0`, `4500`, `9000`, `18000`, and `27000` become `0`, `45`, `90`, `180`, and `270` degrees.
- Unchanged POV values do not repeat.
- XInput `IG_` device identifiers are excluded.
- Non-XInput DirectInput devices remain included.
- One device stream failing does not terminate streams for other devices.

### Integration and Manual Verification

1. Build and run on Windows with the .NET 10 SDK.
2. Connect a DirectInput joystick or wheel and confirm discovery/acquisition logs.
3. Press and hold a button; confirm exactly one press log.
4. Leave controls untouched at startup and confirm their initial non-zero values are captured without logging.
5. Move each axis less than `0.25` from its startup value, then at least `0.25`; confirm only the significant baseline-relative crossing logs.
6. Move an axis back to within less than `0.20` of its startup baseline, then at least `0.25` away; confirm it logs again.
7. Move POV hats and confirm degree conversion and `-1` when depressed.
8. Connect an XInput controller and confirm it is excluded.
9. Hot-plug and unplug a DirectInput device and confirm sessions are added and removed without restarting.
10. Stop with Ctrl+C and confirm clean shutdown without native-resource errors.

## Completion Criteria

- `dotnet build` succeeds with no warnings introduced by the project.
- Automated tests for normalization, transition detection, POV conversion, and XInput filtering pass.
- The application discovers multiple simultaneous DirectInput devices.
- XInput-compatible devices do not produce input events.
- Required button, baseline-relative axis, and POV events are emitted once per qualifying transition through `ILogger`.
- Device disconnects and shutdown complete without leaked subscriptions or DirectInput resources.
