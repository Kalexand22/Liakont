namespace Liakont.Modules.Mandats.Infrastructure;

using Dapper;
using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Domain.Entities;
using Npgsql;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper de l'agrégat <see cref="SelfBilledAcceptance"/>. Persiste l'état d'acceptation
/// (mutable) de façon atomique, scopée par <c>company_id</c> (CLAUDE.md n°9, INV-MANDATS-1). CHAQUE
/// transition (création incluse) écrit l'agrégat ET son entrée de journal append-only
/// (<c>self_billed_acceptance_log</c>) dans la MÊME transaction (ADR-0024 §6 / INV-ACCEPT-5 : « pas de
/// transition sans ligne de journal »). La table <c>self_billed_acceptances</c> porte l'état courant
/// (update permis) ; le journal est append-only (immuabilité garantie par un trigger base, jamais par un
/// chemin de code).
/// </summary>
internal sealed class PostgresSelfBilledAcceptanceUnitOfWork : ISelfBilledAcceptanceUnitOfWork
{
    private const string InsertAcceptanceSql = """
        INSERT INTO mandats.self_billed_acceptances
            (company_id, document_id, state, allocated_number, pending_since, deadline_utc,
             created_at, updated_at)
        VALUES
            (@CompanyId, @DocumentId, @State, @AllocatedNumber, @PendingSince, @DeadlineUtc,
             @CreatedAt, @UpdatedAt)
        """;

    private const string LockAcceptanceForUpdateSql = """
        SELECT 1
        FROM mandats.self_billed_acceptances
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        FOR UPDATE
        """;

    private const string UpdateAcceptanceSql = """
        UPDATE mandats.self_billed_acceptances
        SET state = @State,
            allocated_number = @AllocatedNumber,
            updated_at = @UpdatedAt
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        """;

    private const string InsertLogSql = """
        INSERT INTO mandats.self_billed_acceptance_log
            (company_id, document_id, from_state, to_state, operator_id, operator_name)
        VALUES
            (@CompanyId, @DocumentId, @FromState, @ToState, @OperatorId, @OperatorName)
        """;

    private readonly TransactionScope _txn;

    private PostgresSelfBilledAcceptanceUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresSelfBilledAcceptanceUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken ct = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, ct);
        return new PostgresSelfBilledAcceptanceUnitOfWork(txn);
    }

    public async Task InsertAsync(
        SelfBilledAcceptance acceptance, SelfBilledAcceptanceLogEntry logEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(logEntry);

        try
        {
            await _txn.Connection.ExecuteAsync(
                new CommandDefinition(
                    InsertAcceptanceSql,
                    AcceptanceParameters(acceptance),
                    _txn.Transaction,
                    cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException(
                "Une acceptation existe déjà pour ce document self-billed dans ce tenant.", ex);
        }

        await InsertLogAsync(logEntry, ct);
    }

    public async Task<SelfBilledAcceptance?> GetForUpdateAsync(
        Guid companyId, Guid documentId, CancellationToken ct = default)
    {
        var locked = await _txn.Connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                LockAcceptanceForUpdateSql,
                new { CompanyId = companyId, DocumentId = documentId },
                _txn.Transaction,
                cancellationToken: ct));

        if (locked is null)
        {
            return null;
        }

        return await SelfBilledAcceptanceMaterializer.LoadAsync(
            _txn.Connection, companyId, documentId, _txn.Transaction, ct);
    }

    public async Task SaveTransitionAsync(
        SelfBilledAcceptance acceptance, SelfBilledAcceptanceLogEntry logEntry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(logEntry);

        var affected = await _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                UpdateAcceptanceSql,
                AcceptanceParameters(acceptance),
                _txn.Transaction,
                cancellationToken: ct));

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "L'acceptation ciblée par la transition est introuvable pour ce tenant.");
        }

        // Journal APPEND-ONLY, dans la même transaction → atomicité (ADR-0024 §6) : un échec ici annule
        // aussi la transition (et inversement).
        await InsertLogAsync(logEntry, ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _txn.CommitAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static object AcceptanceParameters(SelfBilledAcceptance acceptance)
        => new
        {
            acceptance.CompanyId,
            acceptance.DocumentId,
            State = (int)acceptance.State,
            acceptance.AllocatedNumber,
            acceptance.PendingSince,
            acceptance.DeadlineUtc,
            acceptance.CreatedAt,
            acceptance.UpdatedAt,
        };

    private Task<int> InsertLogAsync(SelfBilledAcceptanceLogEntry entry, CancellationToken ct)
        => _txn.Connection.ExecuteAsync(
            new CommandDefinition(
                InsertLogSql,
                new
                {
                    entry.CompanyId,
                    entry.DocumentId,
                    FromState = entry.FromState is null ? (int?)null : (int)entry.FromState.Value,
                    ToState = (int)entry.ToState,
                    entry.OperatorId,
                    entry.OperatorName,
                },
                _txn.Transaction,
                cancellationToken: ct));
}
