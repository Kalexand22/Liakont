namespace Stratum.Common.UI.Models;

/// <summary>Describes an active escalation rule displayed in the enriched <c>WorkflowBar</c>.</summary>
public sealed record WorkflowEscalationInfo
{
    /// <summary>Condition type (timeout, field_match, approaching_date).</summary>
    public required string ConditionType { get; init; }

    /// <summary>Condition parameters (JSON or descriptive string).</summary>
    public required string ConditionParams { get; init; }

    /// <summary>Action type (notify, reassign, mark_urgent, auto_transition).</summary>
    public required string ActionType { get; init; }
}
