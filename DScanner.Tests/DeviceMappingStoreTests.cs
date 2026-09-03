using System.Text.Json;
using DScanner.Mapping;
using Microsoft.Extensions.Options;

namespace DScanner.Tests;

public sealed class DeviceMappingStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"dscanner-map-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ResolvePath_UsesTheDeviceNameInTheOutputDirectory()
    {
        DeviceMappingStore store = Create(new DeviceMappingSettings
        {
            OutputDirectory = _directory
        });

        string path = store.ResolvePath("T.16000M", Guid.NewGuid());

        Assert.Equal(Path.Combine(_directory, "T.16000M.json"), path);
    }

    [Fact]
    public void ResolvePath_PrefersAnExplicitFilePath()
    {
        string explicitPath = Path.Combine(_directory, "left-stick.json");
        DeviceMappingStore store = Create(new DeviceMappingSettings
        {
            OutputDirectory = _directory,
            FilePath = explicitPath
        });

        Assert.Equal(explicitPath, store.ResolvePath("T.16000M", Guid.NewGuid()));
    }

    [Fact]
    public void Load_ReturnsEmptyWhenTheFileIsMissing()
    {
        DeviceMappingStore store = Create();

        Assert.Empty(store.Load(Path.Combine(_directory, "absent.json")));
    }

    [Fact]
    public void Save_WritesTheAgreedJsonShape()
    {
        DeviceMappingStore store = Create();
        string path = Path.Combine(_directory, "device.json");

        store.Save(
            path,
            [
                new DeviceMappingEntry("Fire", 0, 1, DeviceMappingInputType.Button),
                new DeviceMappingEntry("Throttle Up", 2, 1, DeviceMappingInputType.Axis),
                new DeviceMappingEntry("Hat Up", 0, -1, DeviceMappingInputType.Pov)
            ]);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(3, root.GetArrayLength());

        JsonElement button = root[0];
        Assert.Equal("Fire", button.GetProperty("label").GetString());
        Assert.Equal(0, button.GetProperty("index").GetInt32());
        Assert.Equal(1, button.GetProperty("value").GetInt32());
        Assert.Equal("button", button.GetProperty("type").GetString());

        JsonElement axis = root[1];
        Assert.Equal("axis", axis.GetProperty("type").GetString());
        Assert.Equal(1, axis.GetProperty("value").GetInt32());

        Assert.Equal("pov", root[2].GetProperty("type").GetString());
        Assert.Equal(-1, root[2].GetProperty("value").GetInt32());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        DeviceMappingStore store = Create();
        string path = Path.Combine(_directory, "device.json");
        DeviceMappingEntry[] entries =
        [
            new("Fire", 0, 1, DeviceMappingInputType.Button),
            new("Roll Left", 1, -1, DeviceMappingInputType.Axis)
        ];

        store.Save(path, entries);

        Assert.Equal(entries, store.Load(path));
    }

    [Fact]
    public void Save_ReplacesAnExistingFileAndLeavesNoTemporaryFile()
    {
        DeviceMappingStore store = Create();
        string path = Path.Combine(_directory, "device.json");

        store.Save(path, [new DeviceMappingEntry("Fire", 0, 1, DeviceMappingInputType.Button)]);
        store.Save(path, [new DeviceMappingEntry("Trigger", 1, 1, DeviceMappingInputType.Button)]);

        DeviceMappingEntry entry = Assert.Single(store.Load(path));
        Assert.Equal("Trigger", entry.Label);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Upsert_AppendsNewControls()
    {
        List<DeviceMappingEntry> entries = [];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Fire", 0, 1, DeviceMappingInputType.Button));

        Assert.Null(replaced);
        Assert.Single(entries);
    }

    [Fact]
    public void Upsert_ReplacesTheLabelBoundToTheSameControl()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Fire", 0, 1, DeviceMappingInputType.Button)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Trigger", 0, 1, DeviceMappingInputType.Button));

        Assert.Equal("Fire", replaced);
        Assert.Equal("Trigger", Assert.Single(entries).Label);
    }

    [Fact]
    public void Upsert_DoesNotUseLabelForMatching()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Primary Fire", 2, 1, DeviceMappingInputType.Button)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Alternate Fire", 2, 1, DeviceMappingInputType.Button));

        Assert.Equal("Primary Fire", replaced);
        DeviceMappingEntry updated = Assert.Single(entries);
        Assert.Equal("Alternate Fire", updated.Label);
        Assert.Equal(2, updated.Index);
        Assert.Equal(1, updated.Value);
        Assert.Equal(DeviceMappingInputType.Button, updated.Type);
    }

    [Fact]
    public void Upsert_TreatsOppositeAxisDirectionsAsSeparateControls()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Roll Left", 0, -1, DeviceMappingInputType.Axis)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Roll Right", 0, 1, DeviceMappingInputType.Axis));

        Assert.Null(replaced);
        Assert.Equal(2, entries.Count);
    }

    private DeviceMappingStore Create(DeviceMappingSettings? settings = null) =>
        new(Options.Create(settings ?? new DeviceMappingSettings { OutputDirectory = _directory }));
}
