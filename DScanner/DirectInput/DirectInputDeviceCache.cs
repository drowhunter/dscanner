using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DScanner.DirectInput;

public sealed class DirectInputDeviceCache(
    ILogger<DirectInputDeviceCache> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "DScanner",
        "devices.json");

    public IReadOnlyList<DirectInputDeviceDescriptor> Load()
    {
        if (!File.Exists(_cachePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(_cachePath);
            return JsonSerializer.Deserialize<DirectInputDeviceDescriptor[]>(
                json,
                JsonOptions) ?? [];
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            logger.LogWarning(
                exception,
                "Could not read the DirectInput device cache {CachePath}.",
                _cachePath);
            return [];
        }
    }

    public void Save(IReadOnlyList<DirectInputDeviceDescriptor> devices)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cachePath);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = $"{_cachePath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(devices, JsonOptions));
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Could not write the DirectInput device cache {CachePath}.",
                _cachePath);
        }
    }
}
