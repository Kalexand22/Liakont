namespace Liakont.Modules.Documents.Infrastructure;

using Dapper;
using Liakont.Modules.Documents.Application;
using Liakont.Modules.Documents.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Unité de travail Dapper du module Documents (item TRK01), ouverte sur la base DU TENANT (la connexion
/// = le tenant — database-per-tenant, blueprint §7). Le document et sa piste d'audit (append-only) sont
/// écrits dans la MÊME transaction. Le journal <c>document_events</c> est immuable : son immuabilité est
/// garantie par un trigger base (CLAUDE.md n°4), jamais par un chemin de code.
/// </summary>
internal sealed class PostgresDocumentUnitOfWork : IDocumentUnitOfWork
{
    private const string InsertDocumentIfAbsentSql = """
        INSERT INTO documents.documents
            (id, source_reference, document_number, document_type, issue_date, supplier_siren,
             customer_name, customer_is_company_hint, total_net, total_tax, total_gross, state,
             payload_hash, pa_document_id, mapping_version, first_seen_utc, last_update_utc)
        VALUES
            (@Id, @SourceReference, @DocumentNumber, @DocumentType, @IssueDate, @SupplierSiren,
             @CustomerName, @CustomerIsCompanyHint, @TotalNet, @TotalTax, @TotalGross, @State,
             @PayloadHash, @PaDocumentId, @MappingVersion, @FirstSeenUtc, @LastUpdateUtc)
        ON CONFLICT (id) DO NOTHING
        """;

    private const string UpsertDocumentSql = """
        INSERT INTO documents.documents
            (id, source_reference, document_number, document_type, issue_date, supplier_siren,
             customer_name, customer_is_company_hint, total_net, total_tax, total_gross, state,
             payload_hash, pa_document_id, mapping_version, first_seen_utc, last_update_utc)
        VALUES
            (@Id, @SourceReference, @DocumentNumber, @DocumentType, @IssueDate, @SupplierSiren,
             @CustomerName, @CustomerIsCompanyHint, @TotalNet, @TotalTax, @TotalGross, @State,
             @PayloadHash, @PaDocumentId, @MappingVersion, @FirstSeenUtc, @LastUpdateUtc)
        ON CONFLICT (id) DO UPDATE SET
            source_reference         = excluded.source_reference,
            document_number          = excluded.document_number,
            document_type            = excluded.document_type,
            issue_date               = excluded.issue_date,
            supplier_siren           = excluded.supplier_siren,
            customer_name            = excluded.customer_name,
            customer_is_company_hint = excluded.customer_is_company_hint,
            total_net                = excluded.total_net,
            total_tax                = excluded.total_tax,
            total_gross              = excluded.total_gross,
            state                    = excluded.state,
            payload_hash             = excluded.payload_hash,
            pa_document_id           = excluded.pa_document_id,
            mapping_version          = excluded.mapping_version,
            last_update_utc          = excluded.last_update_utc
        """;

    private const string InsertEventSql = """
        INSERT INTO documents.document_events
            (id, document_id, timestamp_utc, event_type, detail, payload_snapshot,
             pa_response_snapshot, mapping_trace, operator_identity)
        VALUES
            (@Id, @DocumentId, @TimestampUtc, @EventType, @Detail, @PayloadSnapshot::jsonb,
             @PaResponseSnapshot::jsonb, @MappingTrace::jsonb, @OperatorIdentity)
        """;

    private readonly TransactionScope _txn;

    private PostgresDocumentUnitOfWork(TransactionScope txn)
    {
        _txn = txn;
    }

    public static async Task<PostgresDocumentUnitOfWork> BeginAsync(
        IConnectionFactory connectionFactory,
        CancellationToken cancellationToken = default)
    {
        var txn = await TransactionScope.BeginAsync(connectionFactory, cancellationToken);
        return new PostgresDocumentUnitOfWork(txn);
    }

    public async Task<bool> CreateDetectedAsync(Document document, DocumentEvent genesisEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(genesisEvent);

        var inserted = await _txn.Connection.ExecuteAsync(new CommandDefinition(
            InsertDocumentIfAbsentSql,
            ToDocumentParameters(document),
            _txn.Transaction,
            cancellationToken: cancellationToken));

        // Idempotence sur l'identifiant : si le document existait déjà (re-push d'ingestion), on n'écrit
        // NI le document NI l'événement de genèse — l'état déjà avancé n'est pas écrasé, l'audit pas dupliqué.
        if (inserted == 0)
        {
            return false;
        }

        await AppendEventAsync(genesisEvent, cancellationToken);
        return true;
    }

    public async Task UpsertDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            UpsertDocumentSql,
            ToDocumentParameters(document),
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task AppendEventAsync(DocumentEvent documentEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentEvent);

        await _txn.Connection.ExecuteAsync(new CommandDefinition(
            InsertEventSql,
            new
            {
                documentEvent.Id,
                documentEvent.DocumentId,
                documentEvent.TimestampUtc,
                EventType = documentEvent.EventType.ToString(),
                documentEvent.Detail,
                documentEvent.PayloadSnapshot,
                documentEvent.PaResponseSnapshot,
                documentEvent.MappingTrace,
                documentEvent.OperatorIdentity,
            },
            _txn.Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _txn.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _txn.DisposeAsync();
    }

    private static object ToDocumentParameters(Document document)
    {
        return new
        {
            document.Id,
            document.SourceReference,
            document.DocumentNumber,
            document.DocumentType,
            document.IssueDate,
            document.SupplierSiren,
            document.CustomerName,
            document.CustomerIsCompanyHint,
            document.TotalNet,
            document.TotalTax,
            document.TotalGross,
            State = document.State.ToString(),
            document.PayloadHash,
            document.PaDocumentId,
            document.MappingVersion,
            document.FirstSeenUtc,
            document.LastUpdateUtc,
        };
    }
}
