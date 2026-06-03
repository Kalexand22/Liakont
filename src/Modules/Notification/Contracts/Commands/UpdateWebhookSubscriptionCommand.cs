namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record UpdateWebhookSubscriptionCommand : IRequest
{
    public required Guid SubscriptionId { get; init; }

    public required string Name { get; init; }

    public required string EventType { get; init; }

    public required string TargetUrl { get; init; }

    public string? Secret { get; init; }

    public required bool IsActive { get; init; }
}
