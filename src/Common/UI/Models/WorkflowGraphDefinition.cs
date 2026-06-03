namespace Stratum.Common.UI.Models;

/// <summary>
/// Complete graph definition for the <c>WorkflowGraphEditor</c> (UIE03).
/// Contains positioned nodes, directed edges with optional guards,
/// and metadata for validation (initial state, etc.).
/// Immutable — all mutations return a new instance.
/// </summary>
public sealed record WorkflowGraphDefinition
{
    /// <summary>All nodes (states) in the workflow graph.</summary>
    public required IReadOnlyList<WorkflowGraphNode> Nodes { get; init; }

    /// <summary>All edges (transitions) in the workflow graph.</summary>
    public required IReadOnlyList<WorkflowGraphEdge> Edges { get; init; }

    /// <summary>Id of the initial state node. Exactly one node must be initial.</summary>
    public required string InitialNodeId { get; init; }

    /// <summary>Creates an empty graph with a single initial node.</summary>
    public static WorkflowGraphDefinition Default()
    {
        var initialNode = new WorkflowGraphNode
        {
            Id = "initial",
            Label = "Brouillon",
            Category = "initial",
            X = 200,
            Y = 80,
        };

        return new WorkflowGraphDefinition
        {
            Nodes = [initialNode],
            Edges = [],
            InitialNodeId = "initial",
        };
    }
}
