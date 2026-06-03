namespace Stratum.Modules.Notification.Contracts.DTOs;

public record WebhookDispatchJobPayload
{
    public required Guid SubscriptionId { get; init; }

    public required string EventType { get; init; }

    public required string TargetUrl { get; init; }

    public required string PayloadJson { get; init; }
}
