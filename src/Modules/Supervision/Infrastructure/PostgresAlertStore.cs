namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Domain;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Persistance Dapper des alertes (item SUP01a) sur la base DU TENANT courant (<see cref="IConnectionFactory"/>
/// route vers le tenant résolu — database-per-tenant, blueprint §7). Surface interne au module (moteur +
/// acquittement). L'alerte est de l'état opérationnel mutable : <see cref="UpdateAsync"/> ne touche QUE les
/// colonnes mutables (résolution, acquittement) — jamais le déclenchement, la règle ni la gravité.
/// </summary>
internal sealed class PostgresAlertStore : IAlertStore
{
    private const string AlertColumns = """
        id, tenant_id, rule_key, severity, detail, triggered_utc, resolved_utc, acknowledged_by, acknowledged_utc
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresAlertStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Alert?> FindActiveByRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT {AlertColumns}
            FROM supervision.alerts
            WHERE rule_key = @RuleKey AND resolved_utc IS NULL
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { RuleKey = ruleKey }, cancellationToken: cancellationToken));

        return row is null ? null : MapAlert(row);
    }

    public async Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT {AlertColumns}
            FROM supervision.alerts
            WHERE id = @Id
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : MapAlert(row);
    }

    public async Task InsertAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // ON CONFLICT DO NOTHING : ferme la course anti-bruit (index unique partiel sur la règle active) —
        // une 2e insertion concurrente est ignorée plutôt que de lever ou de créer un doublon actif.
        const string sql = """
            INSERT INTO supervision.alerts
                (id, tenant_id, rule_key, severity, detail, triggered_utc, resolved_utc, acknowledged_by, acknowledged_utc)
            VALUES
                (@Id, @TenantId, @RuleKey, @Severity, @Detail, @TriggeredUtc, @ResolvedUtc, @AcknowledgedBy, @AcknowledgedUtc)
            ON CONFLICT DO NOTHING
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                alert.Id,
                alert.TenantId,
                alert.RuleKey,
                Severity = alert.Severity.ToString(),
                alert.Detail,
                alert.TriggeredUtc,
                alert.ResolvedUtc,
                alert.AcknowledgedBy,
                alert.AcknowledgedUtc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Seules les colonnes MUTABLES (résolution, acquittement) sont écrites — le déclenchement, le
        // tenant, la règle et la gravité d'une alerte ne changent jamais après sa création.
        const string sql = """
            UPDATE supervision.alerts
            SET resolved_utc = @ResolvedUtc,
                acknowledged_by = @AcknowledgedBy,
                acknowledged_utc = @AcknowledgedUtc
            WHERE id = @Id
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                alert.Id,
                alert.ResolvedUtc,
                alert.AcknowledgedBy,
                alert.AcknowledgedUtc,
            },
            cancellationToken: cancellationToken));
    }

    private static Alert MapAlert(dynamic row)
    {
        return Alert.Reconstitute(
            (Guid)row.id,
            (string)row.tenant_id,
            (string)row.rule_key,
            Enum.Parse<AlertSeverity>((string)row.severity),
            (string?)row.detail,
            AlertRowReader.ToDateTimeOffset((object)row.triggered_utc),
            AlertRowReader.ToNullableDateTimeOffset((object?)row.resolved_utc),
            (string?)row.acknowledged_by,
            AlertRowReader.ToNullableDateTimeOffset((object?)row.acknowledged_utc));
    }
}
