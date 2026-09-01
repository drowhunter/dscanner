using System.Text.Json.Serialization;

namespace DScanner.Mapping;

/// <summary>
/// One labelled control in a device mapping file.
/// </summary>
/// <param name="Label">The user-supplied name for the control.</param>
/// <param name="ButtonNumber">The button, axis, or POV number the label is bound to.</param>
/// <param name="Type">The kind of control <paramref name="ButtonNumber"/> refers to.</param>
/// <param name="Direction">
/// For <see cref="DeviceMappingInputType.Axis"/> entries, the sign of the movement that
/// triggered the binding, so that opposite deflections of one axis stay distinct.
/// Omitted for buttons and POVs.
/// </param>
public sealed record DeviceMappingEntry(
    string Label,
    int ButtonNumber,
    DeviceMappingInputType Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Direction = null);
