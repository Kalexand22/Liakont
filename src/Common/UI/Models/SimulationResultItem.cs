namespace Stratum.Common.UI.Models;

/// <summary>
/// One matched routing rule in a dry-run simulation result.
/// Projected from the Notification module's <c>RoutingResultDto</c> — keeps Common.UI
/// independent of Notification.Contracts (R2).
/// </summary>
public record SimulationResultItem
{
    public required string RuleCode { get; init; }

    public required string RuleName { get; init; }

    public required string ServiceCode { get; init; }

    public required string RecipientType { get; init; }

    public required string RecipientValue { get; init; }

    public required int Priority { get; init; }
}
