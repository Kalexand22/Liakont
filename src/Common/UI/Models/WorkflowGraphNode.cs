namespace Stratum.Common.UI.Models;

/// <summary>
/// A node (state) in a <see cref="WorkflowGraphDefinition"/> for the graph editor.
/// Extends the basic <see cref="WorkflowStep"/> concept with position and category.
/// Immutable — all mutations return a new instance via <c>with</c>.
/// </summary>
public sealed record WorkflowGraphNode
{
    /// <summary>Unique identifier for this node within the graph.</summary>
    public required string Id { get; init; }

    /// <summary>Display label shown on the node card.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Category for color-coding: initial, standard, decision, terminal, error.
    /// Maps to FSM token colors in the UI.
    /// </summary>
    public string Category { get; init; } = "standard";

    /// <summary>Optional description shown below the label.</summary>
    public string? Description { get; init; }

    /// <summary>X position on the canvas (in pixels).</summary>
    public double X { get; init; }

    /// <summary>Y position on the canvas (in pixels).</summary>
    public double Y { get; init; }

    /// <summary>Optional list of required permissions for this state.</summary>
    public IReadOnlyList<string> RequiredPermissions { get; init; } = [];

    /// <summary>Optional escalation timeout in hours. Null means no escalation.</summary>
    public int? EscalationTimeoutHours { get; init; }
}
