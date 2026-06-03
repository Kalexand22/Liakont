namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record UpdateDeliverySlaCommand : IRequest
{
    public required Guid Id { get; init; }

    public required int MaxDelaySeconds { get; init; }

    public string? EscalationAction { get; init; }

    public string? EscalationRecipient { get; init; }
}
