namespace Stratum.Common.UI.Models;

/// <summary>
/// A directed edge (transition) between two nodes in a <see cref="WorkflowGraphDefinition"/>.
/// Optionally carries a guard condition (<see cref="LogicNode"/>) and a label.
/// </summary>
public sealed record WorkflowGraphEdge
{
    /// <summary>Unique identifier for this edge.</summary>
    public required string Id { get; init; }

    /// <summary>Source node Id.</summary>
    public required string FromId { get; init; }

    /// <summary>Target node Id.</summary>
    public required string ToId { get; init; }

    /// <summary>Display label on the arrow (e.g., "Approve", "Reject").</summary>
    public string? Label { get; init; }

    /// <summary>Optional guard condition tree (evaluated to allow the transition).</summary>
    public LogicNode? Guard { get; init; }

    /// <summary>Optional callback action name triggered on transition.</summary>
    public string? CallbackAction { get; init; }
}
