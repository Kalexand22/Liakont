namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using Dapper;
using MediatR;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class DeleteApiKeyHandler : IRequestHandler<DeleteApiKeyCommand>
{
    private readonly IConnectionFactory _connectionFactory;

    public DeleteApiKeyHandler(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task Handle(DeleteApiKeyCommand request, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM notification.api_keys
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
