namespace Stratum.Modules.Notification.Contracts.DTOs;

public record RoutingResultDto
{
    public required string RuleCode { get; init; }

    public required string RuleName { get; init; }

    public required string ServiceCode { get; init; }

    public required string RecipientType { get; init; }

    public required string RecipientValue { get; init; }

    public required int Priority { get; init; }
}
