namespace Stratum.Common.UI.Models;

/// <summary>Describes a service opinion displayed in the enriched <c>WorkflowBar</c>.</summary>
public sealed record WorkflowApprovalInfo
{
    /// <summary>Service code that submitted the opinion.</summary>
    public required string ServiceCode { get; init; }

    /// <summary>Opinion value (approved, approved_with_conditions, needs_info, rejected).</summary>
    public required string Opinion { get; init; }

    /// <summary>Conditions text if the opinion was approved with conditions.</summary>
    public string? Conditions { get; init; }
}
