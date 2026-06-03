namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record DeleteWebhookSubscriptionCommand : IRequest
{
    public required Guid SubscriptionId { get; init; }
}
