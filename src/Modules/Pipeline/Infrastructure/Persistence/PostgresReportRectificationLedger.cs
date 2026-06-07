namespace Liakont.Modules.Pipeline.Infrastructure.Persistence;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Pipeline.Application;
using Liakont.Modules.Pipeline.Domain.Rectification;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Liakont.Modules.Transmission.Contracts;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Écriture/lecture Dapper du journal des rectificatifs d'e-reporting (<c>pipeline.report_rectifications</c>,
/// PIP04) sur la base DU TENANT courant (<see cref="IConnectionFactory"/> route vers le tenant —
/// database-per-tenant, blueprint §7). APPEND-ONLY (triggers base, INV-PIPELINE-035) : seul un INSERT est émis,
/// jamais d'UPDATE/DELETE. Le flux et le statut sont persistés par NOM d'énumération (lisibilité d'audit) ;
/// le contenu rectifié (<c>payload_snapshot</c>) est du jsonb produit par le service avec montants en chaînes
/// invariantes — jamais de float (CLAUDE.md n°1).
/// </summary>
public sealed class PostgresReportRectificationLedger : IReportRectificationLedger
{
    private readonly IConnectionFactory _connectionFactory;

    public PostgresReportRectificationLedger(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ReportRectificationEntry?> GetLatestAsync(
        PaymentReportFlux flux,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, flux, period_start, period_end, content_hash, status,
                   pa_report_id, payload_snapshot, pa_response_snapshot, detail, created_utc
            FROM pipeline.report_rectifications
            WHERE flux = @Flux AND period_start = @PeriodStart AND period_end = @PeriodEnd
            ORDER BY created_utc DESC, seq DESC
            LIMIT 1
            """;

        var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            sql,
            new { Flux = flux.ToString(), PeriodStart = periodStart, PeriodEnd = periodEnd },
            cancellationToken: cancellationToken));

        return row is null ? null : Map(row);
    }

    public async Task AppendAsync(ReportRectificationEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO pipeline.report_rectifications
                (id, flux, period_start, period_end, content_hash, status,
                 pa_report_id, payload_snapshot, pa_response_snapshot, detail, created_utc)
            VALUES
                (@Id, @Flux, @PeriodStart, @PeriodEnd, @ContentHash, @Status,
                 @PaReportId, @PayloadSnapshot::jsonb, @PaResponseSnapshot, @Detail, @CreatedUtc)
            """;

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entry.Id,
                Flux = entry.Flux.ToString(),
                entry.PeriodStart,
                entry.PeriodEnd,
                entry.ContentHash,
                Status = entry.Status.ToString(),
                entry.PaReportId,
                entry.PayloadSnapshot,
                entry.PaResponseSnapshot,
                entry.Detail,
                entry.CreatedUtc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<RectificationPeriodKey>> ListDeclaredPeriodsAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT DISTINCT flux, period_start, period_end
            FROM pipeline.report_rectifications
            ORDER BY period_start, period_end, flux
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(row => new RectificationPeriodKey
        {
            Flux = Enum.Parse<PaymentReportFlux>((string)row.flux),
            PeriodStart = ToDateOnly((object)row.period_start),
            PeriodEnd = ToDateOnly((object)row.period_end),
        }).ToList();
    }

    public async Task<IReadOnlyList<ReportRectificationEntry>> ListByPeriodAsync(
        PaymentReportFlux flux,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, flux, period_start, period_end, content_hash, status,
                   pa_report_id, payload_snapshot, pa_response_snapshot, detail, created_utc
            FROM pipeline.report_rectifications
            WHERE flux = @Flux AND period_start = @PeriodStart AND period_end = @PeriodEnd
            ORDER BY created_utc, seq
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql,
            new { Flux = flux.ToString(), PeriodStart = periodStart, PeriodEnd = periodEnd },
            cancellationToken: cancellationToken));

        return rows.Select(row => (ReportRectificationEntry)Map(row)).ToList();
    }

    private static ReportRectificationEntry Map(dynamic row)
    {
        return new ReportRectificationEntry
        {
            Id = (Guid)row.id,
            Flux = Enum.Parse<PaymentReportFlux>((string)row.flux),
            PeriodStart = ToDateOnly((object)row.period_start),
            PeriodEnd = ToDateOnly((object)row.period_end),
            ContentHash = (string)row.content_hash,
            Status = Enum.Parse<ReportRectificationStatus>((string)row.status),
            PaReportId = (string?)row.pa_report_id,
            PayloadSnapshot = (string?)row.payload_snapshot,
            PaResponseSnapshot = (string?)row.pa_response_snapshot,
            Detail = (string?)row.detail,
            CreatedUtc = PipelineRowReader.ToDateTimeOffset((object)row.created_utc),
        };
    }

    private static DateOnly ToDateOnly(object value) => value switch
    {
        DateOnly date => date,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => throw new InvalidOperationException($"Type de date inattendu : {value.GetType()}"),
    };
}
