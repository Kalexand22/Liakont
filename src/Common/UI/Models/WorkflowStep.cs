namespace Stratum.Common.UI.Models;

/// <summary>
/// A single step in a workflow definition.
/// </summary>
public sealed record WorkflowStep
{
    /// <summary>Unique identifier for this step within the workflow.</summary>
    public required string Id { get; init; }

    /// <summary>Display label shown beneath the step circle.</summary>
    public required string Label { get; init; }

    /// <summary>Optional icon (emoji or text) displayed inside the step circle.</summary>
    public string? Icon { get; init; }

    /// <summary>Optional color token (success, warning, error, info, neutral, primary).</summary>
    public string? Color { get; init; }
}
