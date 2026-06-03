namespace Stratum.Modules.Notification.Contracts.DTOs;

public record ApiKeyDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string KeyPrefix { get; init; }

    public required string[] Scopes { get; init; }

    public required int RateLimit { get; init; }

    public required bool IsRevoked { get; init; }

    public required Guid CompanyId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}
