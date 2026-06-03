namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using Dapper;
using MediatR;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class RevokeApiKeyHandler : IRequestHandler<RevokeApiKeyCommand>
{
    private readonly IConnectionFactory _connectionFactory;

    public RevokeApiKeyHandler(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE notification.api_keys
            SET is_revoked = true, revoked_at = now()
            WHERE id = @ApiKeyId AND company_id = @CompanyId
            """;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { request.ApiKeyId, request.CompanyId },
                cancellationToken: cancellationToken));
    }
}
