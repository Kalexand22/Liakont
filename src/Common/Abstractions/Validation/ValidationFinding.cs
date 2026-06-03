namespace Stratum.Common.Abstractions.Validation;

/// <summary>
/// A single finding produced by a validator.
/// </summary>
public sealed record ValidationFinding
{
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// The field that the finding relates to, or null for entity-level findings.
    /// </summary>
    public string? Field { get; init; }

    public required string Message { get; init; }

    /// <summary>
    /// Optional invariant code for programmatic identification (e.g., "INV-COMP-001").
    /// </summary>
    public string? Code { get; init; }
}
