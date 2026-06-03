namespace Stratum.Common.Abstractions.Actions;

/// <summary>
/// A finding produced by an action step (validation error, warning, or informational message).
/// </summary>
public sealed record ActionFinding
{
    public required ActionFindingSeverity Severity { get; init; }

    public string? Field { get; init; }

    public required string Message { get; init; }

    /// <summary>
    /// Optional invariant code (e.g., "INV-COMP-001") for programmatic identification.
    /// </summary>
    public string? Code { get; init; }
}
