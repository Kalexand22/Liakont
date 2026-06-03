namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record CreateWebhookSubscriptionCommand : IRequest<Guid>
{
    public required string Name { get; init; }

    public required string EventType { get; init; }

    public required string TargetUrl { get; init; }

    public required string Secret { get; init; }

    public required Guid CompanyId { get; init; }
}
