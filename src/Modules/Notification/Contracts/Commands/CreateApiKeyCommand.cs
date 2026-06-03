namespace Stratum.Modules.Notification.Contracts.Commands;

using MediatR;
using Stratum.Modules.Notification.Contracts.DTOs;

public record CreateApiKeyCommand : IRequest<ApiKeyCreatedDto>
{
    public required string Name { get; init; }

    public required string[] Scopes { get; init; }

    public required int RateLimit { get; init; }

    public required Guid CompanyId { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}
