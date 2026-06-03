namespace Stratum.Modules.Notification.Infrastructure.Handlers.Commands;

using Dapper;
using MediatR;
using Stratum.Common.Infrastructure.Database;
using Stratum.Modules.Notification.Contracts.Commands;

public sealed class SaveIntegrationConfigHandler : IRequestHandler<SaveIntegrationConfigCommand, Guid>
{
    private readonly IConnectionFactory _connectionFactory;

    public SaveIntegrationConfigHandler(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> Handle(SaveIntegrationConfigCommand request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO notification.integration_configs (integration_type, config_json, is_enabled, company_id)
            VALUES (@IntegrationType, @ConfigJson::jsonb, @IsEnabled, @CompanyId)
            ON CONFLICT (integration_type, company_id) DO UPDATE
            SET config_json = @ConfigJson::jsonb,
                is_enabled  = @IsEnabled,
                updated_at  = now()
            RETURNING id
            """;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);
        var id = await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(
                sql,
                new
                {
                    request.IntegrationType,
                    request.ConfigJson,
                    request.IsEnabled,
                    request.CompanyId,
                },
                cancellationToken: cancellationToken));

        return id;
    }
}
