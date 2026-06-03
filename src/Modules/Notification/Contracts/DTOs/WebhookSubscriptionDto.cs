namespace Stratum.Modules.Notification.Contracts.DTOs;

public record WebhookSubscriptionDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string EventType { get; init; }

    public required string TargetUrl { get; init; }

    public required bool IsActive { get; init; }

    public required Guid CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
