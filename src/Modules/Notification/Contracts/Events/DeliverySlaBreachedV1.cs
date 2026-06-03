namespace Stratum.Modules.Notification.Contracts.Events;

public record DeliverySlaBreachedV1
{
    public required Guid DeliveryRecordId { get; init; }

    public required string TemplateCode { get; init; }

    public required int ExpectedDelaySeconds { get; init; }

    public required int ActualDelaySeconds { get; init; }

    public Guid? CompanyId { get; init; }

    public required DateTimeOffset DetectedAt { get; init; }
}
