namespace Stratum.Common.UI.Models;

/// <summary>
/// Metadata extracted from an enum value for rendering a step in <c>StratumEnumStepper</c>.
/// </summary>
public sealed record EnumStepInfo
{
    /// <summary>Ordinal position (0-based).</summary>
    public required int Ordinal { get; init; }

    /// <summary>Display label (from <c>[Display(Name)]</c> or humanized enum name).</summary>
    public required string Label { get; init; }

    /// <summary>Optional icon name (from <c>[StepIcon]</c>).</summary>
    public string? Icon { get; init; }

    /// <summary>Optional color token (from <c>[StepColor]</c>).</summary>
    public string? Color { get; init; }

    /// <summary>The raw enum integer value.</summary>
    public required int Value { get; init; }
}
