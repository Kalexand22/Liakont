namespace Stratum.Modules.Notification.Contracts.DTOs;

public record DeliverySlaDto
{
    public required Guid Id { get; init; }

    public required string Category { get; init; }

    public required int MaxDelaySeconds { get; init; }

    public string? EscalationAction { get; init; }

    public string? EscalationRecipient { get; init; }

    public Guid? CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
