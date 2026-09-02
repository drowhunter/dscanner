using System.Text.Json;
using System.Text.Json.Serialization;

namespace DirectInputWatcher;

internal sealed class DirectInputDeviceCache(
    DirectInputWatcherOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string? _cachePath = options.DeviceCachePath;

    public IReadOnlyList<DirectInputDeviceDescriptor> Load()
    {
        if (string.IsNullOrWhiteSpace(_cachePath)
            || !File.Exists(_cachePath))
        {
            return [];
        }

        string json = File.ReadAllText(_cachePath);
        return JsonSerializer.Deserialize<DirectInputDeviceDescriptor[]>(
            json,
            JsonOptions) ?? [];
    }

    public void Save(IReadOnlyList<DirectInputDeviceDescriptor> devices)
    {
        if (string.IsNullOrWhiteSpace(_cachePath))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(_cachePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath =
            $"{_cachePath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(devices, JsonOptions));
        File.Move(temporaryPath, _cachePath, overwrite: true);
    }
}
