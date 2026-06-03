namespace Stratum.Common.UI.Models;

/// <summary>
/// Describes a comparison operator available in <c>LogicTreeEditor</c>.
/// Compatible data types are listed so the editor hides irrelevant operators
/// when a variable is selected.
/// </summary>
public sealed record LogicOperatorDef
{
    /// <summary>Machine code stored in the DSL (e.g. <c>"eq"</c>, <c>"contains"</c>).</summary>
    public required string Code { get; init; }

    /// <summary>Human label shown in the dropdown (e.g. "est", "contient").</summary>
    public required string Label { get; init; }

    /// <summary>Data types this operator applies to (e.g. <c>["string", "number", "date", "enum"]</c>).
    /// An empty list means "compatible with all types".</summary>
    public IReadOnlyList<string> CompatibleTypes { get; init; } = [];

    /// <summary>When true, the value input is hidden (e.g. <c>is_empty</c>, <c>is_not_empty</c>).</summary>
    public bool HidesValue { get; init; }
}
