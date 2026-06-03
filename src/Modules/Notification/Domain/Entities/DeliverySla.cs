namespace Stratum.Modules.Notification.Domain.Entities;

public sealed class DeliverySla
{
    private DeliverySla()
    {
    }

    public Guid Id { get; private set; }

    public TemplateCategory Category { get; private set; }

    public int MaxDelaySeconds { get; private set; }

    public string? EscalationAction { get; private set; }

    public string? EscalationRecipient { get; private set; }

    public Guid? CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static DeliverySla Create(
        TemplateCategory category,
        int maxDelaySeconds,
        string? escalationAction,
        string? escalationRecipient,
        Guid? companyId)
    {
        if (maxDelaySeconds <= 0)
        {
            throw new ArgumentException("INV-NOTIF-030: Max delay must be positive.", nameof(maxDelaySeconds));
        }

        return new DeliverySla
        {
            Id = Guid.NewGuid(),
            Category = category,
            MaxDelaySeconds = maxDelaySeconds,
            EscalationAction = escalationAction?.Trim(),
            EscalationRecipient = escalationRecipient?.Trim(),
            CompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static DeliverySla Reconstitute(
        Guid id,
        TemplateCategory category,
        int maxDelaySeconds,
        string? escalationAction,
        string? escalationRecipient,
        Guid? companyId,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new DeliverySla
        {
            Id = id,
            Category = category,
            MaxDelaySeconds = maxDelaySeconds,
            EscalationAction = escalationAction,
            EscalationRecipient = escalationRecipient,
            CompanyId = companyId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(int maxDelaySeconds, string? escalationAction, string? escalationRecipient)
    {
        if (maxDelaySeconds <= 0)
        {
            throw new ArgumentException("INV-NOTIF-030: Max delay must be positive.", nameof(maxDelaySeconds));
        }

        MaxDelaySeconds = maxDelaySeconds;
        EscalationAction = escalationAction?.Trim();
        EscalationRecipient = escalationRecipient?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
