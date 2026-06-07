namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper des alertes (item SUP01a) sur la base DU TENANT courant (<see cref="IConnectionFactory"/>
/// route vers le tenant résolu — database-per-tenant, blueprint §7). Consommées par le dashboard SUP02,
/// qui agrège ces lectures tenant par tenant pour sa vue cross-tenant (lecture seule, blueprint §7 règle 2).
/// </summary>
internal sealed class PostgresAlertQueries : IAlertQueries
{
    private const int MaxPageSize = 500;

    private const string AlertColumns = """
        id, tenant_id, rule_key, severity, detail, triggered_utc, resolved_utc, acknowledged_by, acknowledged_utc
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresAlertQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AlertDto>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT {AlertColumns}
            FROM supervision.alerts
            WHERE resolved_utc IS NULL
            ORDER BY triggered_utc DESC, id
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(MapAlert).ToList();
    }

    public async Task<IReadOnlyList<AlertDto>> ListRecentAsync(int max, CancellationToken cancellationToken = default)
    {
        var boundedMax = max < 1 ? 1 : Math.Min(max, MaxPageSize);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT {AlertColumns}
            FROM supervision.alerts
            ORDER BY triggered_utc DESC, id
            LIMIT @Max
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { Max = boundedMax }, cancellationToken: cancellationToken));

        return rows.Select(MapAlert).ToList();
    }

    public async Task<AlertDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
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

    private static AlertDto MapAlert(dynamic row)
    {
        return new AlertDto
        {
            Id = (Guid)row.id,
            TenantId = (string)row.tenant_id,
            RuleKey = (string)row.rule_key,
            Severity = (string)row.severity,
            Detail = (string?)row.detail,
            TriggeredUtc = AlertRowReader.ToDateTimeOffset((object)row.triggered_utc),
            ResolvedUtc = AlertRowReader.ToNullableDateTimeOffset((object?)row.resolved_utc),
            AcknowledgedBy = (string?)row.acknowledged_by,
            AcknowledgedUtc = AlertRowReader.ToNullableDateTimeOffset((object?)row.acknowledged_utc),
        };
    }
}
