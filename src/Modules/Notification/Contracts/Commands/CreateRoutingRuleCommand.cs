namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record CreateRoutingRuleCommand : IRequest<Guid>
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string EntityType { get; init; }

    public required string ServiceCode { get; init; }

    public required string RecipientType { get; init; }

    public required string RecipientValue { get; init; }

    public string? ConditionsJson { get; init; }

    public int Priority { get; init; }

    public Guid? CompanyId { get; init; }
}
