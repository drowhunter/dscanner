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
                new DeviceMappingEntry("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button),
                new DeviceMappingEntry("Throttle Up", "X Axis", 2, 1, DeviceMappingInputType.Axis),
                new DeviceMappingEntry("Hat Up", string.Empty, 0, -1, DeviceMappingInputType.Pov)
            ]);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(3, root.GetArrayLength());

        JsonElement button = root[0];
        Assert.Equal("Fire", button.GetProperty("description").GetString());
        Assert.Equal(string.Empty, button.GetProperty("name").GetString());
        Assert.Equal(0, button.GetProperty("index").GetInt32());
        Assert.Equal(1, button.GetProperty("value").GetInt32());
        Assert.Equal("button", button.GetProperty("type").GetString());

        JsonElement axis = root[1];
        Assert.Equal("Throttle Up", axis.GetProperty("description").GetString());
        Assert.Equal("X Axis", axis.GetProperty("name").GetString());
        Assert.Equal("axis", axis.GetProperty("type").GetString());
        Assert.Equal(1, axis.GetProperty("value").GetInt32());

        Assert.Equal("Hat Up", root[2].GetProperty("description").GetString());
        Assert.Equal(string.Empty, root[2].GetProperty("name").GetString());
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
            new("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button),
            new("Roll Left", "X", 1, -1, DeviceMappingInputType.Axis)
        ];

        store.Save(path, entries);

        Assert.Equal(entries, store.Load(path));
    }

    [Fact]
    public void Save_ReplacesAnExistingFileAndLeavesNoTemporaryFile()
    {
        DeviceMappingStore store = Create();
        string path = Path.Combine(_directory, "device.json");

        store.Save(path, [new DeviceMappingEntry("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button)]);
        store.Save(path, [new DeviceMappingEntry("Trigger", string.Empty, 1, 1, DeviceMappingInputType.Button)]);

        DeviceMappingEntry entry = Assert.Single(store.Load(path));
        Assert.Equal("Trigger", entry.Description);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Upsert_AppendsNewControls()
    {
        List<DeviceMappingEntry> entries = [];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button));

        Assert.Null(replaced);
        Assert.Single(entries);
    }

    [Fact]
    public void Upsert_ReplacesTheLabelBoundToTheSameControl()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Trigger", "Button 0", 0, 1, DeviceMappingInputType.Button));

        Assert.Equal("Fire", replaced);
        DeviceMappingEntry entry = Assert.Single(entries);
        Assert.Equal("Trigger", entry.Description);
        Assert.Equal("Button 0", entry.Name);
    }

    [Fact]
    public void Upsert_DoesNotUseLabelForMatching()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Primary Fire", string.Empty, 2, 1, DeviceMappingInputType.Button)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Alternate Fire", "Trigger", 2, 1, DeviceMappingInputType.Button));

        Assert.Equal("Primary Fire", replaced);
        DeviceMappingEntry updated = Assert.Single(entries);
        Assert.Equal("Alternate Fire", updated.Description);
        Assert.Equal("Trigger", updated.Name);
        Assert.Equal(2, updated.Index);
        Assert.Equal(1, updated.Value);
        Assert.Equal(DeviceMappingInputType.Button, updated.Type);
    }

    [Fact]
    public void Upsert_TreatsOppositeAxisDirectionsAsSeparateControls()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Roll Left", "X", 0, -1, DeviceMappingInputType.Axis)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Roll Right", "X", 0, 1, DeviceMappingInputType.Axis));

        Assert.Null(replaced);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Upsert_PreventsDuplicateDescriptionsAcrossControls_IgnoringCase()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button),
            new("Brake", string.Empty, 1, 1, DeviceMappingInputType.Button)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("fIrE", "Button 2", 2, 1, DeviceMappingInputType.Button));

        Assert.Null(replaced);
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries.Count(entry => string.Equals(entry.Description, "Fire", StringComparison.OrdinalIgnoreCase)));
        DeviceMappingEntry fire = Assert.Single(entries.Where(entry => string.Equals(entry.Description, "fIrE", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("fIrE", fire.Description);
        Assert.Equal(2, fire.Index);
        Assert.Equal("Button 2", fire.Name);
    }

    [Fact]
    public void FindConflictingEntry_ReturnsTheOtherControlUsingTheSameDescription()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button),
            new("Brake", string.Empty, 1, 1, DeviceMappingInputType.Button)
        ];

        DeviceMappingEntry? conflict = DeviceMappingStore.FindConflictingEntry(
            entries,
            new DeviceMappingEntry("fIrE", "Button 2", 2, 1, DeviceMappingInputType.Button));

        Assert.NotNull(conflict);
        Assert.Equal("Fire", conflict.Description);
        Assert.Equal(0, conflict.Index);
    }

    [Fact]
    public void FindConflictingEntry_ReturnsNullWhenUpdatingTheSameControl()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Trigger", string.Empty, 0, 1, DeviceMappingInputType.Button)
        ];

        DeviceMappingEntry? conflict = DeviceMappingStore.FindConflictingEntry(
            entries,
            new DeviceMappingEntry("Trigger", "Button A", 0, 1, DeviceMappingInputType.Button));

        Assert.Null(conflict);
    }

    [Fact]
    public void FindConflictingEntry_ReturnsNullWhenNoDescriptionMatches()
    {
        List<DeviceMappingEntry> entries =
        [
            new("Fire", string.Empty, 0, 1, DeviceMappingInputType.Button)
        ];

        DeviceMappingEntry? conflict = DeviceMappingStore.FindConflictingEntry(
            entries,
            new DeviceMappingEntry("Brake", string.Empty, 1, 1, DeviceMappingInputType.Button));

        Assert.Null(conflict);
    }

    [Fact]
    public void Upsert_AllowsUpdatingSameInput_ButPreventsDuplicatesAcrossOtherInputs()
    {
        // Prevent duplicates across different inputs
        List<DeviceMappingEntry> entries =
        [
            new("Fire", "Button 1", 0, 1, DeviceMappingInputType.Button),
            new("Brake", "Button 2", 1, 1, DeviceMappingInputType.Button)
        ];

        string? replaced = DeviceMappingStore.Upsert(
            entries,
            new DeviceMappingEntry("Fire", "Button 3", 2, 1, DeviceMappingInputType.Button));

        Assert.Null(replaced);
        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries.Count(entry => string.Equals(entry.Description, "Fire", StringComparison.OrdinalIgnoreCase)));
        DeviceMappingEntry fire = Assert.Single(entries.Where(entry => string.Equals(entry.Description, "Fire", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, fire.Index);
        Assert.Equal("Button 3", fire.Name);

        // Allowed when updating the same input
        List<DeviceMappingEntry> entries2 =
        [
            new("Trigger", "Button A", 0, 1, DeviceMappingInputType.Button)
        ];

        string? replaced2 = DeviceMappingStore.Upsert(
            entries2,
            new DeviceMappingEntry("Trigger", "Button A (renamed)", 0, 1, DeviceMappingInputType.Button));

        Assert.Equal("Trigger", replaced2);
        Assert.Single(entries2);
        Assert.Equal("Trigger", entries2[0].Description);
        Assert.Equal("Button A (renamed)", entries2[0].Name);
    }

    private DeviceMappingStore Create(DeviceMappingSettings? settings = null) =>
        new(Options.Create(settings ?? new DeviceMappingSettings { OutputDirectory = _directory }));
}
