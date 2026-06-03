namespace Stratum.Common.UI.Models;

/// <summary>
/// Defines a workflow with steps, edges, and a current step for rendering in
/// <see cref="Stratum.Common.UI.Components.StratumWorkflowStepper"/>.
/// The stepper renders a single main path (following first edges) with one-level-deep
/// branches. Deeper branch chains are not followed in the current implementation.
/// </summary>
public sealed record WorkflowDefinition
{
    /// <summary>All steps in the workflow (ordered — first step is the root).</summary>
    public required IReadOnlyList<WorkflowStep> Steps { get; init; }

    /// <summary>Directed edges between steps.</summary>
    public required IReadOnlyList<WorkflowEdge> Edges { get; init; }

    /// <summary>The Id of the currently active step.</summary>
    public required string CurrentStepId { get; init; }
}
