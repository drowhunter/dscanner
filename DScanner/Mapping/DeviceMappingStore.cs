using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DScanner.Mapping;

public sealed class DeviceMappingStore(
    IOptions<DeviceMappingSettings> settings)
    : IDeviceMappingStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly DeviceMappingSettings _settings = settings.Value;

    public string ResolvePath(string deviceName, Guid instanceGuid)
    {
        if (!string.IsNullOrWhiteSpace(_settings.FilePath))
        {
            return Path.GetFullPath(_settings.FilePath);
        }

        string directory = string.IsNullOrWhiteSpace(_settings.OutputDirectory)
            ? Directory.GetCurrentDirectory()
            : _settings.OutputDirectory;

        return Path.GetFullPath(
            Path.Combine(directory, DeviceMappingFileName.Create(deviceName, instanceGuid)));
    }

    public IReadOnlyList<DeviceMappingEntry> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return [];
        }

        string json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<DeviceMappingEntry[]>(json, JsonOptions) ?? [];
    }

    public void Save(string path, IReadOnlyList<DeviceMappingEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>
    /// Adds <paramref name="entry"/>, replacing any entry already bound to the same control.
    /// </summary>
    /// <returns>The label that was replaced, or <see langword="null"/> when the entry is new.</returns>
    public static string? Upsert(IList<DeviceMappingEntry> entries, DeviceMappingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entry);

        for (int index = 0; index < entries.Count; index++)
        {
            DeviceMappingEntry existing = entries[index];
            if (existing.Type != entry.Type
                || existing.Index != entry.Index
                || existing.Value != entry.Value)
            {
                continue;
            }

            entries[index] = entry;
            return existing.Label;
        }

        entries.Add(entry);
        return null;
    }
}
