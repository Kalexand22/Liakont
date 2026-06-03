namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using Dapper;
using MediatR;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.Commands;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Domain.Entities;

public sealed class CreateApiKeyHandler : IRequestHandler<CreateApiKeyCommand, ApiKeyCreatedDto>
{
    private readonly IConnectionFactory _connectionFactory;

    public CreateApiKeyHandler(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ApiKeyCreatedDto> Handle(CreateApiKeyCommand request, CancellationToken cancellationToken)
    {
        var (entity, fullKey) = ApiKey.Create(
            request.Name,
            request.Scopes,
            request.RateLimit,
            request.CompanyId,
            request.ExpiresAt);

        const string sql = """
            INSERT INTO notification.api_keys (id, name, key_prefix, key_hash, scopes, rate_limit, is_revoked, company_id, created_at, expires_at)
            VALUES (@Id, @Name, @KeyPrefix, @KeyHash, @Scopes, @RateLimit, @IsRevoked, @CompanyId, @CreatedAt, @ExpiresAt)
            """;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entity.Id,
                entity.Name,
                entity.KeyPrefix,
                entity.KeyHash,
                entity.Scopes,
                entity.RateLimit,
                entity.IsRevoked,
                entity.CompanyId,
                entity.CreatedAt,
                entity.ExpiresAt,
            },
            cancellationToken: cancellationToken));

        return new ApiKeyCreatedDto { Id = entity.Id, FullKey = fullKey };
    }
}
