namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record DeleteApiKeyCommand : IRequest
{
    public required Guid ApiKeyId { get; init; }

    public required Guid CompanyId { get; init; }
}
