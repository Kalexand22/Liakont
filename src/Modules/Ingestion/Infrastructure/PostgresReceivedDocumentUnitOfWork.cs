namespace Liakont.Modules.Ingestion.Infrastructure;

using Dapper;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Domain.Entities;
using Npgsql;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Unité de travail Dapper de la RÉCEPTION de documents (PIV04), ouverte sur la base SYSTÈME (schéma
/// <c>ingestion</c>) comme le registre d'agents. Les écritures portent leur <c>tenant_id</c> et les
/// lectures sont scopées au tenant. Le registre de réception est append-only. Les événements
/// d'intégration sont écrits dans l'outbox DANS LA MÊME TRANSACTION que l'insertion : un document
/// reçu et l'événement qui en découle sont atomiques (l'outbox vit dans la base système, drainée par
/// le worker système — même connexion que cette unité de travail).
/// </summary>
internal sealed class PostgresReceivedDocumentUnitOfWork : IReceivedDocumentUnitOfWork
{
    private readonly TransactionScope _txn;
    private readonly IOutboxWriter _outboxWriter;

    private PostgresReceivedDocumentUnitOfWork(TransactionScope txn, IOutboxWriter outboxWriter)
    {
        _txn = txn;
        _outboxWriter = outboxWriter;
    }

    public static async Task<PostgresReceivedDocumentUnitOfWork> BeginAsync(
        ISystemConnectionFactory systemConnectionFactory,
        IOutboxWriter outboxWriter,
        CancellationToken cancellationToken = default)
    {
        var txn = await TransactionScope.BeginAsync(
            new SystemConnectionFactoryAdapter(systemConnectionFactory),
            cancellationToken);
        return new PostgresReceivedDocumentUnitOfWork(txn, outboxWriter);
    }

    public async Task<bool> PayloadHashExistsAsync(string tenantId, string payloadHash, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM ingestion.received_documents
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
            FROM ingestion.received_documents
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

    public async Task<Guid?> GetDocumentIdByPayloadHashAsync(string tenantId, string payloadHash, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT document_id
            FROM ingestion.received_documents
            WHERE tenant_id = @TenantId AND payload_hash = @PayloadHash
            LIMIT 1
            """;

        return await _txn.Connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, PayloadHash = payloadHash },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task InsertReceivedDocumentAsync(ReceivedDocument receivedDocument, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO ingestion.received_documents
                (id, tenant_id, source_reference, payload_hash, document_id, contract_version, received_at)
            VALUES
                (@Id, @TenantId, @SourceReference, @PayloadHash, @DocumentId, @ContractVersion, @ReceivedAtUtc)
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
                    receivedDocument.DocumentId,
                    receivedDocument.ContractVersion,
                    receivedDocument.ReceivedAtUtc,
                },
                _txn.Transaction,
                cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Course entre lots concurrents : l'empreinte (tenant + payload_hash) vient d'être insérée
            // par un autre push. On bloque cette insertion ; l'appelant la traite comme un doublon.
            throw new ConflictException("Empreinte de payload déjà reçue pour ce tenant (doublon concurrent).", ex);
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
