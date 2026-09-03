using System.Text.Json.Serialization;

namespace DScanner.Mapping;

/// <summary>
/// One labelled control in a device mapping file.
/// </summary>
/// <param name="Description">The user-supplied description for the control.</param>
/// <param name="Name">The source control name (for example an axis name), or an empty string when unavailable.</param>
/// <param name="Index">The index of the control on the device (button index, axis index, or POV index).</param>
/// <param name="Value">The integer value associated with the control. For buttons this is 1 (press), for
/// axes this is the direction (-1, 0, 1), and for POVs this is degrees or -1 for centre.</param>
/// <param name="Type">The kind of control the entry refers to.</param>
public sealed record DeviceMappingEntry(
    string Description,
    string Name,
    int Index,
    int Value,
    DeviceMappingInputType Type);
