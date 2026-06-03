namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record DeleteRoutingRuleCommand : IRequest
{
    public required Guid RoutingRuleId { get; init; }
}
