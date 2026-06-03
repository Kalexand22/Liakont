namespace Stratum.Common.UI.Models;

/// <summary>
/// A directed edge between two steps in a <see cref="WorkflowDefinition"/>.
/// Distinct from <see cref="WorkflowTransition"/> which represents a user-triggerable action.
/// </summary>
public sealed record WorkflowEdge
{
    /// <summary>Id of the source step.</summary>
    public required string FromId { get; init; }

    /// <summary>Id of the target step.</summary>
    public required string ToId { get; init; }
}
