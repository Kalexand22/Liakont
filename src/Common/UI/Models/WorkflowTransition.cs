namespace Stratum.Common.UI.Models;

/// <summary>Describes a single transition available in a <c>WorkflowBar</c>.</summary>
public sealed record WorkflowTransition
{
    /// <summary>Target status value after the transition.</summary>
    public required string TargetStatus { get; init; }

    /// <summary>Human-readable label for the action button.</summary>
    public required string Label { get; init; }

    /// <summary>When true, the bar shows a confirmation modal before invoking <c>OnTransition</c>.</summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>When true, the button is rendered disabled.</summary>
    public bool Disabled { get; init; }

    /// <summary>Tooltip shown on the disabled button. Ignored when <see cref="Disabled"/> is false.</summary>
    public string? DisabledReason { get; init; }
}
