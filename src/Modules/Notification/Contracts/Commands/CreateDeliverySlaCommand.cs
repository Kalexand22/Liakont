namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record CreateDeliverySlaCommand : IRequest<Guid>
{
    public required string Category { get; init; }

    public required int MaxDelaySeconds { get; init; }

    public string? EscalationAction { get; init; }

    public string? EscalationRecipient { get; init; }

    public Guid? CompanyId { get; init; }
}
