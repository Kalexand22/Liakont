namespace Stratum.Common.UI.Models;

/// <summary>
/// Describes a variable available for selection in <c>LogicTreeEditor</c>.
/// The editor auto-completes on these. The <see cref="DataType"/> is used
/// to filter which <see cref="LogicOperatorDef"/> options are offered.
/// </summary>
public sealed record LogicVariable
{
    /// <summary>Machine-readable code (matches field codes in FormEngine, workflow, routing).</summary>
    public required string Code { get; init; }

    /// <summary>Human label shown in the dropdown.</summary>
    public required string Label { get; init; }

    /// <summary>Data type hint — <c>"string"</c>, <c>"number"</c>, <c>"date"</c>,
    /// <c>"boolean"</c>, <c>"enum"</c>. Used to filter compatible operators.</summary>
    public string DataType { get; init; } = "string";

    /// <summary>When <see cref="DataType"/> is <c>"enum"</c>, the allowed values.</summary>
    public IReadOnlyList<string>? EnumValues { get; init; }
}
