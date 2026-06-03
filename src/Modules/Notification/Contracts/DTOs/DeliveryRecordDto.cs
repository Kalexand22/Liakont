namespace Stratum.Modules.Notification.Contracts.DTOs;

public record DeliveryRecordDto
{
    public required Guid Id { get; init; }

    public Guid? NotificationId { get; init; }

    public required string TemplateCode { get; init; }

    public required string RecipientEmail { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public required DateTimeOffset SentAt { get; init; }

    public DateTimeOffset? DeliveredAt { get; init; }

    public DateTimeOffset? FailedAt { get; init; }

    public required int RetryCount { get; init; }

    public required bool SlaBreached { get; init; }

    public Guid? CompanyId { get; init; }
}
