namespace DScanner.Mapping;

/// <summary>
/// Reads and writes per-device mapping files.
/// </summary>
public interface IDeviceMappingStore
{
    /// <summary>
    /// Resolves the mapping file path for a device.
    /// </summary>
    string ResolvePath(string deviceName, Guid instanceGuid);

    /// <summary>
    /// Loads existing entries, returning an empty list when the file does not exist.
    /// </summary>
    IReadOnlyList<DeviceMappingEntry> Load(string path);

    /// <summary>
    /// Writes entries to <paramref name="path"/>, replacing any existing file atomically.
    /// </summary>
    void Save(string path, IReadOnlyList<DeviceMappingEntry> entries);
}
