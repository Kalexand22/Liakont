namespace Stratum.Modules.Notification.Contracts.Events;

public record WebhookDispatchFailedV1
{
    public required Guid SubscriptionId { get; init; }

    public required string EventType { get; init; }

    public required string TargetUrl { get; init; }

    public required string ErrorMessage { get; init; }

    public required DateTimeOffset FailedAt { get; init; }
}
