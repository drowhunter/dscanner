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
    /// Finds an entry bound to a different control than <paramref name="entry"/> that already
    /// uses the same description (case-insensitively).
    /// </summary>
    /// <returns>The conflicting entry, or <see langword="null"/> when the description is unused or only
    /// bound to the same control as <paramref name="entry"/>.</returns>
    public static DeviceMappingEntry? FindConflictingEntry(IReadOnlyList<DeviceMappingEntry> entries, DeviceMappingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entry);

        foreach (DeviceMappingEntry existing in entries)
        {
            bool sameControl = existing.Type == entry.Type
                && existing.Index == entry.Index
                && existing.Value == entry.Value;

            if (sameControl)
            {
                continue;
            }

            if (string.Equals(existing.Description, entry.Description, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds <paramref name="entry"/>, replacing any entry already bound to the same control,
    /// and ensuring no two entries share the same description.
    /// </summary>
    /// <returns>The description that was replaced on the same control, or <see langword="null"/> when the entry is new.</returns>
    public static string? Upsert(IList<DeviceMappingEntry> entries, DeviceMappingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entry);

        int existingControlIndex = -1;
        string? replacedDescription = null;

        for (int index = 0; index < entries.Count; index++)
        {
            DeviceMappingEntry existing = entries[index];
            if (existing.Type != entry.Type
                || existing.Index != entry.Index
                || existing.Value != entry.Value)
            {
                continue;
            }

            existingControlIndex = index;
            replacedDescription = existing.Description;
            break;
        }

        for (int index = entries.Count - 1; index >= 0; index--)
        {
            if (index == existingControlIndex)
            {
                continue;
            }

            if (!string.Equals(entries[index].Description, entry.Description, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.RemoveAt(index);
            if (index < existingControlIndex)
            {
                existingControlIndex--;
            }
        }

        if (existingControlIndex >= 0)
        {
            entries[existingControlIndex] = entries[existingControlIndex] with
            {
                Description = entry.Description,
                Name = entry.Name
            };
            return replacedDescription;
        }

        entries.Add(entry);
        return null;
    }
}
