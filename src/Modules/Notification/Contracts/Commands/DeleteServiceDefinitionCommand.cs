namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record DeleteServiceDefinitionCommand : IRequest
{
    public required Guid ServiceDefinitionId { get; init; }
}
