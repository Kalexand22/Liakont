namespace Liakont.Modules.Pipeline.Infrastructure.Queries;

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper du journal d'exécutions du pipeline (PIP01) sur la base DU TENANT courant
/// (<see cref="IConnectionFactory"/> route vers le tenant résolu — database-per-tenant, blueprint §7).
/// Aucune requête cross-tenant n'est possible : la connexion EST la frontière de tenant (CLAUDE.md n°9/17).
/// </summary>
public sealed class PostgresPipelineRunQueries : IPipelineRunQueries
{
    private const int MaxLimit = 200;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresPipelineRunQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PipelineRunLogDto>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var boundedLimit = limit < 1 ? 1 : Math.Min(limit, MaxLimit);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, run_type, run_trigger, started_at, completed_at,
                   documents_processed, documents_succeeded, documents_failed, detail
            FROM pipeline.run_logs
            ORDER BY started_at DESC, id
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { Limit = boundedLimit }, cancellationToken: cancellationToken));

        return rows.Select(MapRun).ToList();
    }

    public async Task<IReadOnlyList<PipelineRunLogDto>> GetRunsAsync(DateOnly? fromInclusive, DateOnly? toInclusive, int limit, CancellationToken cancellationToken = default)
    {
        var boundedLimit = limit < 1 ? 1 : Math.Min(limit, MaxLimit);

        // Bornes converties en timestamptz explicites : le filtre porte sur started_at (timestamptz) ;
        // la borne haute est INCLUSIVE au niveau du jour → lendemain à minuit, exclue.
        DateTimeOffset? fromTs = fromInclusive is { } f ? new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        DateTimeOffset? toExclusive = toInclusive is { } t ? new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;

        var where = (fromTs, toExclusive) switch
        {
            (not null, not null) => "WHERE started_at >= @From AND started_at < @ToExclusive",
            (not null, null) => "WHERE started_at >= @From",
            (null, not null) => "WHERE started_at < @ToExclusive",
            _ => string.Empty,
        };

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT id, run_type, run_trigger, started_at, completed_at,
                   documents_processed, documents_succeeded, documents_failed, detail
            FROM pipeline.run_logs
            {where}
            ORDER BY started_at DESC, id
            LIMIT @Limit
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { From = fromTs, ToExclusive = toExclusive, Limit = boundedLimit }, cancellationToken: cancellationToken));

        return rows.Select(MapRun).ToList();
    }

    private static PipelineRunLogDto MapRun(dynamic row)
    {
        return new PipelineRunLogDto
        {
            Id = (Guid)row.id,
            RunType = Enum.Parse<PipelineRunType>((string)row.run_type),
            Trigger = Enum.Parse<PipelineRunTrigger>((string)row.run_trigger),
            StartedAt = PipelineRowReader.ToDateTimeOffset((object)row.started_at),
            CompletedAt = PipelineRowReader.ToNullableDateTimeOffset((object?)row.completed_at),
            DocumentsProcessed = (int)row.documents_processed,
            DocumentsSucceeded = (int)row.documents_succeeded,
            DocumentsFailed = (int)row.documents_failed,
            Detail = (string?)row.detail,
        };
    }
}
