# DirectInputWatcher.Configuration

`IConfiguration` binding for [DirectInputWatcher](https://github.com/drowhunter/dscanner) — register
the controller watcher straight from `appsettings.json` or any other .NET configuration provider.

The core `DirectInputWatcher` package deliberately takes no configuration dependency, so this one
exists to add a single overload. If you configure the watcher in code, you do not need it.

## Usage

```csharp
using DirectInputWatcher.Configuration;

services.AddLogging();
services.AddDirectInputWatcher(configuration);
```

Settings are read from the `DirectInputWatcher` section:

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

An optional setup action runs after binding, so it overrides anything the configuration supplied:

```csharp
services.AddDirectInputWatcher(configuration, options => options.PollFrequency = 30);
```

Everything else — what each option means, the `Lifecycle` and `Inputs` event streams, and how the
watcher behaves at runtime — is documented in the
[DirectInputWatcher README](https://github.com/drowhunter/dscanner/blob/master/DirectInputWatcher/README.md).
