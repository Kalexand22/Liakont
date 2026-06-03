namespace Stratum.Modules.Notification.Domain.Entities;

public sealed class DeliveryRecord
{
    private DeliveryRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid? NotificationId { get; private set; }

    public string TemplateCode { get; private set; } = string.Empty;

    public string RecipientEmail { get; private set; } = string.Empty;

    public string? EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public int RetryCount { get; private set; }

    public bool SlaBreached { get; private set; }

    public Guid? CompanyId { get; private set; }

    public static DeliveryRecord Create(
        string templateCode,
        string recipientEmail,
        string? entityType,
        string? entityId,
        Guid? companyId,
        Guid? notificationId = null)
    {
        if (string.IsNullOrWhiteSpace(templateCode))
        {
            throw new ArgumentException("INV-NOTIF-031: Template code must not be empty.", nameof(templateCode));
        }

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            throw new ArgumentException("INV-NOTIF-032: Recipient email must not be empty.", nameof(recipientEmail));
        }

        return new DeliveryRecord
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            TemplateCode = templateCode.Trim(),
            RecipientEmail = recipientEmail.Trim(),
            EntityType = entityType?.Trim(),
            EntityId = entityId?.Trim(),
            SentAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
            SlaBreached = false,
            CompanyId = companyId,
        };
    }

    public static DeliveryRecord Reconstitute(
        Guid id,
        Guid? notificationId,
        string templateCode,
        string recipientEmail,
        string? entityType,
        string? entityId,
        DateTimeOffset sentAt,
        DateTimeOffset? deliveredAt,
        DateTimeOffset? failedAt,
        int retryCount,
        bool slaBreached,
        Guid? companyId)
    {
        return new DeliveryRecord
        {
            Id = id,
            NotificationId = notificationId,
            TemplateCode = templateCode,
            RecipientEmail = recipientEmail,
            EntityType = entityType,
            EntityId = entityId,
            SentAt = sentAt,
            DeliveredAt = deliveredAt,
            FailedAt = failedAt,
            RetryCount = retryCount,
            SlaBreached = slaBreached,
            CompanyId = companyId,
        };
    }

    public void MarkDelivered()
    {
        DeliveredAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        FailedAt = DateTimeOffset.UtcNow;
        RetryCount++;
    }

    public void MarkSlaBreached()
    {
        SlaBreached = true;
    }

    public void ClearFailureForRetry()
    {
        FailedAt = null;
    }
}
