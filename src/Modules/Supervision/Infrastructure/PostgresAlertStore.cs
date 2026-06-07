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
/// acquittement). Chaque méthode de mise à jour n'écrit QUE les colonnes qu'elle possède : aucune perte de
/// mise à jour concurrente entre la résolution automatique et l'acquittement opérateur.
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

    public async Task ResolveAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Seule la colonne resolved_utc est écrite — acknowledged_by/acknowledged_utc ne sont JAMAIS
        // touchées ici, ce qui évite la perte de mise à jour concurrente avec l'acquittement.
        // AND resolved_utc IS NULL : idempotent et interdit la résurrection si déjà résolue.
        const string sql = """
            UPDATE supervision.alerts
            SET resolved_utc = @ResolvedUtc
            WHERE id = @Id AND resolved_utc IS NULL
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                alert.Id,
                alert.ResolvedUtc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task AcknowledgeAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Seules les colonnes d'acquittement sont écrites — resolved_utc n'est JAMAIS touchée ici,
        // ce qui évite la perte de mise à jour concurrente avec l'auto-résolution.
        const string sql = """
            UPDATE supervision.alerts
            SET acknowledged_by = @AcknowledgedBy,
                acknowledged_utc = @AcknowledgedUtc
            WHERE id = @Id
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                alert.Id,
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
