namespace Stratum.Modules.Notification.Contracts.Events;

public record WebhookDispatchedV1
{
    public required Guid SubscriptionId { get; init; }

    public required string EventType { get; init; }

    public required string TargetUrl { get; init; }

    public required DateTimeOffset DispatchedAt { get; init; }
}
