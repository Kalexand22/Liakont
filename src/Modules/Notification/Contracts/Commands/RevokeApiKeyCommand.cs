namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;

public record RevokeApiKeyCommand : IRequest
{
    public required Guid ApiKeyId { get; init; }

    public required Guid CompanyId { get; init; }
}
