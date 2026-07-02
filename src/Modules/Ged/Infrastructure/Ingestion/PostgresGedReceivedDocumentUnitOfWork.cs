namespace Liakont.Modules.Ged.Infrastructure.Ingestion;

using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Application.Ingestion;
using Liakont.Modules.Ged.Domain.Ingestion;
using Npgsql;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Unité de travail Dapper de la RÉCEPTION du canal GED (GED05b), ouverte sur la base SYSTÈME (schéma
/// <c>ged_ingestion</c>) — miroir EXACT de <c>PostgresReceivedDocumentUnitOfWork</c> (canal fiscal) mais dans un
/// espace de hash STRICTEMENT SÉPARÉ (RL-01/§4.3.1). Les écritures portent leur <c>tenant_id</c> et les lectures sont
/// scopées au tenant. Le registre est append-only. L'événement <c>ManagedDocumentReceivedV1</c> est écrit dans
/// l'outbox DANS LA MÊME TRANSACTION que l'insertion (atomicité registre + événement — l'outbox vit dans la même
/// base système, drainée par le worker système).
/// </summary>
internal sealed class PostgresGedReceivedDocumentUnitOfWork : IGedReceivedDocumentUnitOfWork
{
    private readonly TransactionScope _txn;
    private readonly IOutboxWriter _outboxWriter;

    private PostgresGedReceivedDocumentUnitOfWork(TransactionScope txn, IOutboxWriter outboxWriter)
    {
        _txn = txn;
        _outboxWriter = outboxWriter;
    }

    public static async Task<PostgresGedReceivedDocumentUnitOfWork> BeginAsync(
        ISystemConnectionFactory systemConnectionFactory,
        IOutboxWriter outboxWriter,
        CancellationToken cancellationToken = default)
    {
        var txn = await TransactionScope.BeginAsync(
            new GedSystemConnectionFactoryAdapter(systemConnectionFactory),
            cancellationToken);
        return new PostgresGedReceivedDocumentUnitOfWork(txn, outboxWriter);
    }

    public async Task<bool> PayloadHashExistsAsync(string tenantId, string payloadHash, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM ged_ingestion.ged_received_documents
                WHERE tenant_id = @TenantId AND payload_hash = @PayloadHash)
            """;

        return await _txn.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, PayloadHash = payloadHash },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetLatestHashForSourceReferenceAsync(string tenantId, string sourceReference, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT payload_hash
            FROM ged_ingestion.ged_received_documents
            WHERE tenant_id = @TenantId AND source_reference = @SourceReference
            ORDER BY received_at DESC
            LIMIT 1
            """;

        return await _txn.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, SourceReference = sourceReference },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task InsertReceivedDocumentAsync(GedReceivedDocument receivedDocument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivedDocument);

        const string sql = """
            INSERT INTO ged_ingestion.ged_received_documents
                (id, tenant_id, source_reference, payload_hash, managed_document_id, contract_version, received_at)
            VALUES
                (@Id, @TenantId, @SourceReference, @PayloadHash, @ManagedDocumentId, @ContractVersion, @ReceivedAtUtc)
            """;

        try
        {
            await _txn.Connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    receivedDocument.Id,
                    receivedDocument.TenantId,
                    receivedDocument.SourceReference,
                    receivedDocument.PayloadHash,
                    receivedDocument.ManagedDocumentId,
                    receivedDocument.ContractVersion,
                    receivedDocument.ReceivedAtUtc,
                },
                _txn.Transaction,
                cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Course entre lots concurrents : l'empreinte (tenant + payload_hash) vient d'être insérée par un autre
            // push GED. On bloque cette insertion ; l'appelant la traite comme un doublon (miroir du canal fiscal).
            throw new ConflictException("Empreinte de payload GED déjà reçue pour ce tenant (doublon concurrent).", ex);
        }
    }

    public async Task WriteEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken cancellationToken = default)
    {
        await _outboxWriter.WriteAsync(_txn, integrationEvent, cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _txn.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }
}
