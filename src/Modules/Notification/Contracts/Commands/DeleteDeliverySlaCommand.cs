namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record DeleteDeliverySlaCommand : IRequest
{
    public required Guid Id { get; init; }
}
