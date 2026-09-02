using DirectInputWatcher;
using DScanner.Mapping;
using DScanner.Services;
using DScanner.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vortice.DirectInput;

namespace DScanner.Tests;

public sealed class DeviceMappingServiceTests
{
    private static readonly Guid DeviceA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DeviceB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeConsoleUi _ui = new();
    private readonly FakeDirectInputWatcher _watcher = new();
    private readonly FakeConsoleKeyDispatcher _keys = new();
    private readonly FakeMappingStore _store = new();
    private readonly FakeApplicationLifetime _lifetime = new();

    [Fact]
    public async Task ButtonPress_IsWrittenAsAButtonEntry()
    {
        _ui.Labels.Enqueue("Fire");
        _ui.Labels.Enqueue(string.Empty);
        _ui.PromptActions.Enqueue(ConnectDeviceA);
        _ui.PromptActions.Enqueue(() => _watcher.InputSubject.OnNext(Button(DeviceA, 3)));

        await RunAsync();

        DeviceMappingEntry entry = Assert.Single(_store.Saved);
        Assert.Equal("Fire", entry.Label);
        Assert.Equal(3, entry.Index);
        Assert.Equal(1, entry.Value);
        Assert.Equal(DeviceMappingInputType.Button, entry.Type);
        Assert.Equal("Device A", _store.ResolvedDeviceName);
    }

    [Fact]
    public async Task AxisMovement_RecordsTheDirectionItWasPushed()
    {
        _ui.Labels.Enqueue("Roll Left");
        _ui.Labels.Enqueue(string.Empty);
        _ui.PromptActions.Enqueue(ConnectDeviceA);
        _ui.PromptActions.Enqueue(() => _watcher.InputSubject.OnNext(Axis(DeviceA, 1, -0.8)));

        await RunAsync();

        DeviceMappingEntry entry = Assert.Single(_store.Saved);
        Assert.Equal(1, entry.Index);
        Assert.Equal(DeviceMappingInputType.Axis, entry.Type);
        Assert.Equal(-1, entry.Value);
    }

    [Fact]
    public async Task CentredPov_IsIgnoredSoTheReleaseIsNotCaptured()
    {
        _ui.Labels.Enqueue("Hat Up");
        _ui.Labels.Enqueue(string.Empty);
        _ui.PromptActions.Enqueue(ConnectDeviceA);
        _ui.PromptActions.Enqueue(() =>
        {
            _watcher.InputSubject.OnNext(Pov(DeviceA, 0, -1));
            _watcher.InputSubject.OnNext(Pov(DeviceA, 0, 9000));
        });

        await RunAsync();

        DeviceMappingEntry entry = Assert.Single(_store.Saved);
        Assert.Equal(DeviceMappingInputType.Pov, entry.Type);
        Assert.Equal(0, entry.Index);
    }

    [Fact]
    public async Task InputFromAnotherDevice_IsIgnoredOnceTheDeviceIsLocked()
    {
        _ui.Labels.Enqueue("Fire");
        _ui.Labels.Enqueue("Brake");
        _ui.Labels.Enqueue(string.Empty);
        _ui.PromptActions.Enqueue(ConnectDeviceA);
        _ui.PromptActions.Enqueue(() => _watcher.InputSubject.OnNext(Button(DeviceA, 0)));
        _ui.PromptActions.Enqueue(() =>
        {
            _watcher.InputSubject.OnNext(Button(DeviceB, 7));
            _watcher.InputSubject.OnNext(Button(DeviceA, 1));
        });

        await RunAsync();

        Assert.Equal(2, _store.Saved.Count);
        Assert.Equal([0, 1], _store.Saved.Select(entry => entry.Index));
        Assert.Contains(_ui.Events, message => message.Contains("Ignoring input from Device B"));
    }

    [Fact]
    public async Task Escape_SkipsTheLabelWithoutWritingAnEntry()
    {
        _ui.Labels.Enqueue("Fire");
        _ui.Labels.Enqueue(string.Empty);
        _ui.PromptActions.Enqueue(ConnectDeviceA);
        _ui.PromptActions.Enqueue(() => _keys.Press(ConsoleKey.Escape));

        await RunAsync();

        Assert.Equal(0, _store.SaveCount);
        Assert.Contains(_ui.Events, message => message.Contains("Skipped 'Fire'"));
    }

    [Fact]
    public async Task ExistingEntries_ArePreservedAndTheSameControlIsReplaced()
    {
        _store.Existing.Add(new DeviceMappingEntry("Old Fire", 3, 1, DeviceMappingInputType.Button));
        _store.Existing.Add(new DeviceMappingEntry("Keep Me", 9, 1, DeviceMappingInputType.Button));

        _ui.Labels.Enqueue("Fire");
        _ui.Labels.Enqueue(string.Empty);
        _ui.PromptActions.Enqueue(ConnectDeviceA);
        _ui.PromptActions.Enqueue(() => _watcher.InputSubject.OnNext(Button(DeviceA, 3)));

        await RunAsync();

        Assert.Equal(["Fire", "Keep Me"], _store.Saved.Select(entry => entry.Label));
    }

    [Fact]
    public async Task NoCaptures_WritesNothing()
    {
        _ui.Labels.Enqueue(string.Empty);
        _ui.PromptActions.Enqueue(ConnectDeviceA);

        await RunAsync();

        Assert.Equal(0, _store.SaveCount);
        Assert.True(_ui.PromptCleared);
        Assert.Contains(_ui.Events, message => message.Contains("nothing was written"));
    }

    private async Task RunAsync()
    {
        DeviceMappingService service = new(
            _watcher,
            _ui,
            _keys,
            _store,
            Options.Create(new DeviceMappingSettings { SettleDelay = TimeSpan.Zero }),
            Options.Create(new DirectInputWatcherOptions
            {
                AxisBaselineCalibrationDuration = TimeSpan.Zero
            }),
            _lifetime,
            NullLogger<DeviceMappingService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await _lifetime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    private void ConnectDeviceA() =>
        _watcher.LifecycleSubject.OnNext(
            new DeviceConnected(
                DateTimeOffset.UtcNow,
                new DirectInputDeviceDescriptor(
                    DeviceA,
                    Guid.NewGuid(),
                    "Device A",
                    DeviceType.Joystick,
                    VendorId: 0x044F,
                    ProductId: 0xB10A,
                    InterfacePath: null),
                FromCache: false));

    private static ButtonPressedEvent Button(Guid deviceId, int number) =>
        new(deviceId, DeviceName(deviceId), DateTimeOffset.UtcNow, number);

    private static AxisMovedEvent Axis(Guid deviceId, int number, double difference) =>
        new(
            deviceId,
            DeviceName(deviceId),
            DateTimeOffset.UtcNow,
            number,
            "X",
            Value: difference,
            Baseline: 0,
            Difference: difference);

    private static PovChangedEvent Pov(Guid deviceId, int number, int rawValue) =>
        new(deviceId, DeviceName(deviceId), DateTimeOffset.UtcNow, number, rawValue);

    private static string DeviceName(Guid deviceId) =>
        deviceId == DeviceA ? "Device A" : "Device B";

    private sealed class FakeMappingStore : IDeviceMappingStore
    {
        public List<DeviceMappingEntry> Existing { get; } = [];

        public List<DeviceMappingEntry> Saved { get; private set; } = [];

        public int SaveCount { get; private set; }

        public string? ResolvedDeviceName { get; private set; }

        public string ResolvePath(string deviceName, Guid instanceGuid)
        {
            ResolvedDeviceName = deviceName;
            return Path.Combine(Path.GetTempPath(), $"{deviceName}.json");
        }

        public IReadOnlyList<DeviceMappingEntry> Load(string path) => Existing;

        public void Save(string path, IReadOnlyList<DeviceMappingEntry> entries)
        {
            SaveCount++;
            Saved = [.. entries];
        }
    }
}
