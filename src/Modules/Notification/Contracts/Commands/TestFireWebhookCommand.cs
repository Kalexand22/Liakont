namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record TestFireWebhookCommand : IRequest<TestFireWebhookResult>
{
    public required Guid SubscriptionId { get; init; }

    public required Guid CompanyId { get; init; }
}
