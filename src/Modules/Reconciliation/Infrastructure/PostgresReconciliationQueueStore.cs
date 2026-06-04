namespace Liakont.Modules.Reconciliation.Infrastructure;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Reconciliation.Application;
using Liakont.Modules.Reconciliation.Domain;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Persistance de la file d'attente de réconciliation dans <c>reconciliation.reconciliation_queue</c>
/// (item TRK07, F06 §7 §3). Tenant-scopée par la connexion (<see cref="IConnectionFactory"/> → base du
/// tenant courant ; pas de colonne tenant — database-per-tenant). Table MUTABLE (une proposition/un
/// orphelin peut être confirmé), à la différence de la piste d'audit append-only.
/// </summary>
internal sealed class PostgresReconciliationQueueStore : IReconciliationQueueStore
{
    private const string Columns =
        "id, pool_pdf_id, file_name, status, proposed_document_id, strategy, confidence, detail, created_utc, resolved_utc, operator_identity";

    private const string InsertSql = """
        INSERT INTO reconciliation.reconciliation_queue
            (id, pool_pdf_id, file_name, status, proposed_document_id, strategy, confidence, detail, created_utc, resolved_utc, operator_identity)
        VALUES
            (@Id, @PoolPdfId, @FileName, @Status, @ProposedDocumentId, @Strategy, @Confidence, @Detail, @CreatedUtc, @ResolvedUtc, @OperatorIdentity)
        """;

    private const string UpdateSql = """
        UPDATE reconciliation.reconciliation_queue
        SET status = @Status,
            proposed_document_id = @ProposedDocumentId,
            strategy = @Strategy,
            confidence = @Confidence,
            detail = @Detail,
            resolved_utc = @ResolvedUtc,
            operator_identity = @OperatorIdentity
        WHERE id = @Id
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresReconciliationQueueStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ReconciliationQueueEntry?> FindByPoolPdfIdAsync(string poolPdfId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT {Columns} FROM reconciliation.reconciliation_queue WHERE pool_pdf_id = @PoolPdfId LIMIT 1",
            new { PoolPdfId = poolPdfId },
            cancellationToken: cancellationToken));

        return row is null ? null : MapRow(row);
    }

    public async Task<ReconciliationQueueEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            $"SELECT {Columns} FROM reconciliation.reconciliation_queue WHERE id = @Id LIMIT 1",
            new { Id = id },
            cancellationToken: cancellationToken));

        return row is null ? null : MapRow(row);
    }

    public async Task AddAsync(ReconciliationQueueEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, ToParameters(entry), cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(ReconciliationQueueEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(UpdateSql, ToParameters(entry), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ReconciliationQueueEntry>> ListByStatusAsync(ReconciliationStatus status, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync(new CommandDefinition(
            $"SELECT {Columns} FROM reconciliation.reconciliation_queue WHERE status = @Status ORDER BY created_utc ASC, id ASC",
            new { Status = status.ToString() },
            cancellationToken: cancellationToken));

        var entries = new List<ReconciliationQueueEntry>();
        foreach (var row in rows)
        {
            entries.Add(MapRow(row));
        }

        return entries;
    }

    public async Task<IReadOnlyList<Guid>> ListReconciledDocumentIdsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT DISTINCT proposed_document_id
            FROM reconciliation.reconciliation_queue
            WHERE status IN ('ReconciledAuto', 'ReconciledManual') AND proposed_document_id IS NOT NULL
            """,
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    private static object ToParameters(ReconciliationQueueEntry entry) => new
    {
        entry.Id,
        entry.PoolPdfId,
        entry.FileName,
        Status = entry.Status.ToString(),
        entry.ProposedDocumentId,
        Strategy = entry.Strategy?.ToString(),
        Confidence = entry.Confidence?.ToString(),
        entry.Detail,
        CreatedUtc = entry.CreatedUtc.UtcDateTime,
        ResolvedUtc = entry.ResolvedUtc?.UtcDateTime,
        entry.OperatorIdentity,
    };

    private static ReconciliationQueueEntry MapRow(dynamic row)
    {
        object? proposed = row.proposed_document_id;
        object? strategy = row.strategy;
        object? confidence = row.confidence;
        object? resolved = row.resolved_utc;
        object? operatorIdentity = row.operator_identity;

        return ReconciliationQueueEntry.Reconstitute(
            (Guid)row.id,
            (string)row.pool_pdf_id,
            (string)row.file_name,
            Enum.Parse<ReconciliationStatus>((string)row.status),
            proposed is null ? null : (Guid)proposed,
            strategy is null ? null : Enum.Parse<MatchStrategy>((string)strategy),
            confidence is null ? null : Enum.Parse<MatchConfidence>((string)confidence),
            (string)row.detail,
            ReconciliationRowReader.ToDateTimeOffset((object)row.created_utc),
            resolved is null ? null : ReconciliationRowReader.ToDateTimeOffset(resolved),
            operatorIdentity is null ? null : (string)operatorIdentity);
    }
}
