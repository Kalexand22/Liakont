namespace Liakont.Modules.Documents.Infrastructure.Queries;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures Dapper du module Documents (item TRK01) sur la base DU TENANT courant
/// (<see cref="IConnectionFactory"/> route vers le tenant résolu — database-per-tenant, blueprint §7).
/// Aucune requête cross-tenant n'est possible : la connexion EST la frontière de tenant (CLAUDE.md n°9/17).
/// </summary>
public sealed class PostgresDocumentQueries : IDocumentQueries, IDocumentStateCountQueries, IPaTransmissionJournalQueries
{
    private const int MaxPageSize = 200;

    private const string DocumentColumns = """
        id, source_reference, document_number, document_type, issue_date, supplier_siren,
        customer_name, customer_is_company_hint, total_net, total_tax, total_gross, state,
        payload_hash, pa_document_id, mapping_version, first_seen_utc, last_update_utc,
        buyer_confirmed_as_individual
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresDocumentQueries(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var sql = $"""
            SELECT {DocumentColumns}
            FROM documents.documents
            WHERE id = @Id
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { Id = id }, cancellationToken: cancellationToken));

        return row is null ? null : MapDocument(row);
    }

    public async Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Le numéro n'est pas unique en base (remplacement après rejet, F06 §4) : on retourne le document
        // le PLUS RÉCENT pour ce numéro.
        var sql = $"""
            SELECT {DocumentColumns}
            FROM documents.documents
            WHERE document_number = @DocumentNumber
            ORDER BY last_update_utc DESC
            LIMIT 1
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { DocumentNumber = documentNumber }, cancellationToken: cancellationToken));

        return row is null ? null : MapDocument(row);
    }

    public async Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(
        string state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var boundedPage = page < 1 ? 1 : page;
        var boundedPageSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);
        var offset = (long)(boundedPage - 1) * boundedPageSize;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, document_number, document_type, issue_date, customer_name,
                   total_gross, state, last_update_utc
            FROM documents.documents
            WHERE state = @State
            ORDER BY last_update_utc DESC, id
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql,
            new { State = state, PageSize = boundedPageSize, Offset = offset },
            cancellationToken: cancellationToken));

        return rows.Select(MapSummary).ToList();
    }

    public async Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // F06 §5 : un document en cours d'envoi (Sending) dont l'issue n'est pas encore connue peut avoir
        // été émis côté PA malgré un timeout réseau. On retourne TOUS les documents Sending du tenant (file
        // attendue petite) pour que le pipeline (PIP01) raccroche avant de retenter — jamais de pagination
        // qui masquerait un document à vérifier.
        var sql = $"""
            SELECT id, document_number, document_type, issue_date, customer_name,
                   total_gross, state, last_update_utc
            FROM documents.documents
            WHERE state = @State
            ORDER BY last_update_utc DESC
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql,
            new { State = nameof(Domain.Entities.DocumentState.Sending) },
            cancellationToken: cancellationToken));

        return rows.Select(MapSummary).ToList();
    }

    public async Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Supervision (SUP01b) : le document le PLUS ANCIEN dans l'état (plus petit last_update_utc), borné à
        // UNE ligne. L'index ix_documents_state (state, last_update_utc DESC) couvre aussi ce tri ascendant.
        const string sql = """
            SELECT id, document_number, document_type, issue_date, customer_name,
                   total_gross, state, last_update_utc
            FROM documents.documents
            WHERE state = @State
            ORDER BY last_update_utc ASC, id
            LIMIT 1
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { State = state }, cancellationToken: cancellationToken));

        return row is null ? null : MapSummary(row);
    }

    public async Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(
        string sourceReference,
        string payloadHash,
        CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Clé (référence source, empreinte) : on retourne le document le PLUS RÉCENT pour la clé (un renvoi
        // idempotent partage l'empreinte ; un remplacement après rejet partage la référence source).
        const string sql = """
            SELECT id, document_number, state
            FROM documents.documents
            WHERE source_reference = @SourceReference AND payload_hash = @PayloadHash
            ORDER BY last_update_utc DESC
            LIMIT 1
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql,
            new { SourceReference = sourceReference, PayloadHash = payloadHash },
            cancellationToken: cancellationToken));

        return row is null
            ? null
            : new DocumentStatusDto
            {
                Id = (Guid)row.id,
                DocumentNumber = (string)row.document_number,
                State = (string)row.state,
            };
    }

    public async Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, document_id, timestamp_utc, event_type, detail,
                   payload_snapshot::text AS payload_snapshot,
                   pa_response_snapshot::text AS pa_response_snapshot,
                   mapping_trace::text AS mapping_trace,
                   operator_identity, operator_name
            FROM documents.document_events
            WHERE document_id = @DocumentId
            ORDER BY timestamp_utc
            """;

        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));

        return rows.Select(MapEvent).ToList();
    }

    public async Task<PaTransmissionJournalDto?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Recherche par clé d'idempotence (FX06, F16 §7) — sert l'index partiel ix_document_events_idempotency_key.
        // Une clé doit être unique, mais on borne à la plus récente par sûreté (jamais d'exception sur un doublon).
        const string sql = """
            SELECT id, document_id, timestamp_utc, idempotency_key, pa_account, pa_plugin_id,
                   pa_request_utc, pa_response_utc, transmitted_artifact_hash
            FROM documents.document_events
            WHERE idempotency_key = @IdempotencyKey
            ORDER BY timestamp_utc DESC
            LIMIT 1
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { IdempotencyKey = idempotencyKey }, cancellationToken: cancellationToken));

        return row is null ? null : MapPaTransmissionJournal(row);
    }

    public async Task<DocumentListResult> GetDocumentsAsync(
        DocumentListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var boundedPage = filter.Page < 1 ? 1 : filter.Page;
        var boundedPageSize = filter.PageSize < 1 ? 1 : Math.Min(filter.PageSize, MaxPageSize);
        var offset = (long)(boundedPage - 1) * boundedPageSize;

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Clauses du PÉRIMÈTRE (dates/type/recherche) partagées avec GetStateCountsAsync — les
        // compteurs montrent la répartition de TOUS les états du périmètre courant. La liste et le
        // total ajoutent l'état par-dessus.
        var (withoutState, parameters) = BuildScopeFilterClauses(filter);
        var withState = new List<string>(withoutState);

        if (!string.IsNullOrWhiteSpace(filter.State))
        {
            withState.Add("state = @State");
            parameters.Add("State", filter.State);
        }

        var whereWithState = withState.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", withState);

        var listSql = $"""
            SELECT id, document_number, document_type, issue_date, customer_name,
                   total_gross, state, last_update_utc
            FROM documents.documents
            {whereWithState}
            ORDER BY last_update_utc DESC, id
            LIMIT @PageSize OFFSET @Offset
            """;

        var listParameters = new DynamicParameters(parameters);
        listParameters.Add("PageSize", boundedPageSize);
        listParameters.Add("Offset", offset);

        var rows = await conn.QueryAsync(new CommandDefinition(
            listSql, listParameters, cancellationToken: cancellationToken));
        var items = rows.Select(MapSummary).ToList();

        var totalSql = $"""
            SELECT count(*)
            FROM documents.documents
            {whereWithState}
            """;
        var totalCount = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            totalSql, parameters, cancellationToken: cancellationToken));

        // Même requête de compteurs que GetStateCountsAsync : la couverture d'intégration de
        // l'endpoint liste (compteurs du bandeau) exerce donc aussi le chemin du tableau de bord.
        var countsByState = await QueryStateCountsAsync(conn, filter, cancellationToken);

        return new DocumentListResult
        {
            Items = items,
            Page = boundedPage,
            PageSize = boundedPageSize,
            TotalCount = (int)totalCount,
            CountsByState = countsByState,
        };
    }

    public async Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // documents.archive_entries (TRK05) : alimentée par le module Archive à l'émission, dans le schéma
        // du tenant. Un document peut avoir plusieurs entrées (addendum de réconciliation TRK07) ; on
        // retourne la PLUS RÉCENTE comme référence courante du coffre. Aucune mutation : table WORM.
        const string sql = """
            SELECT package_path, package_hash, chain_hash, archived_utc
            FROM documents.archive_entries
            WHERE document_id = @DocumentId
            ORDER BY archived_utc DESC, id
            LIMIT 1
            """;

        var row = await conn.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql, new { DocumentId = documentId }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new ArchiveReferenceDto
            {
                PackagePath = (string)row.package_path,
                PackageHash = (string)row.package_hash,
                ChainHash = (string)row.chain_hash,
                ArchivedUtc = DocumentRowReader.ToDateTimeOffset((object)row.archived_utc),
            };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> GetStateCountsAsync(
        DocumentListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        return await QueryStateCountsAsync(conn, filter, cancellationToken);
    }

    /// <summary>
    /// Clauses WHERE du PÉRIMÈTRE d'un filtre (dates d'émission, type, recherche) — SANS l'état ni
    /// la pagination. Partagées entre la liste paginée (qui ajoute l'état) et les compteurs par état
    /// (qui couvrent tous les états du périmètre) : un filtre ajouté ici vaut pour les deux chemins.
    /// </summary>
    private static (List<string> Clauses, DynamicParameters Parameters) BuildScopeFilterClauses(DocumentListFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new DynamicParameters();

        if (filter.From is { } from)
        {
            clauses.Add("issue_date >= @From");
            parameters.Add("From", from);
        }

        if (filter.To is { } to)
        {
            clauses.Add("issue_date <= @To");
            parameters.Add("To", to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            clauses.Add("document_type = @Type");
            parameters.Add("Type", filter.Type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Recherche « contient », insensible à la casse ; les jokers LIKE de la saisie sont échappés
            // (backslash = caractère d'échappement LIKE par défaut de PostgreSQL) pour rester littéraux.
            clauses.Add("(document_number ILIKE @Search OR source_reference ILIKE @Search OR customer_name ILIKE @Search)");
            parameters.Add("Search", "%" + EscapeLike(filter.Search) + "%");
        }

        return (clauses, parameters);
    }

    /// <summary>
    /// Compteurs par état (GROUP BY) sur le périmètre du filtre — la SEULE requête des consommateurs
    /// de synthèse (<see cref="GetStateCountsAsync"/>), réutilisée par la liste paginée pour son
    /// bandeau de compteurs.
    /// </summary>
    private static async Task<Dictionary<string, int>> QueryStateCountsAsync(
        IDbConnection conn,
        DocumentListFilter filter,
        CancellationToken cancellationToken)
    {
        var (clauses, parameters) = BuildScopeFilterClauses(filter);
        var where = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);

        var sql = $"""
            SELECT state, count(*) AS cnt
            FROM documents.documents
            {where}
            GROUP BY state
            """;
        var rows = await conn.QueryAsync(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken));

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            counts[(string)row.state] = (int)(long)row.cnt;
        }

        return counts;
    }

    /// <summary>
    /// Échappe les jokers LIKE/ILIKE (<c>%</c>, <c>_</c>) et le caractère d'échappement (<c>\</c>) d'une
    /// saisie de recherche, pour qu'ils soient traités littéralement (PostgreSQL utilise <c>\</c> comme
    /// caractère d'échappement LIKE par défaut).
    /// </summary>
    private static string EscapeLike(string term)
    {
        return term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static DocumentDto MapDocument(dynamic row)
    {
        return new DocumentDto
        {
            Id = (Guid)row.id,
            SourceReference = (string)row.source_reference,
            DocumentNumber = (string)row.document_number,
            DocumentType = (string)row.document_type,
            IssueDate = DocumentRowReader.ToDateOnly((object)row.issue_date),
            SupplierSiren = (string?)row.supplier_siren,
            CustomerName = (string?)row.customer_name,
            CustomerIsCompanyHint = (bool)row.customer_is_company_hint,
            TotalNet = (decimal)row.total_net,
            TotalTax = (decimal)row.total_tax,
            TotalGross = (decimal)row.total_gross,
            State = (string)row.state,
            PayloadHash = (string)row.payload_hash,
            PaDocumentId = (string?)row.pa_document_id,
            MappingVersion = (string?)row.mapping_version,
            FirstSeenUtc = DocumentRowReader.ToDateTimeOffset((object)row.first_seen_utc),
            LastUpdateUtc = DocumentRowReader.ToDateTimeOffset((object)row.last_update_utc),
            BuyerConfirmedAsIndividual = (bool)row.buyer_confirmed_as_individual,
        };
    }

    private static DocumentSummaryDto MapSummary(dynamic row)
    {
        return new DocumentSummaryDto
        {
            Id = (Guid)row.id,
            DocumentNumber = (string)row.document_number,
            DocumentType = (string)row.document_type,
            IssueDate = DocumentRowReader.ToDateOnly((object)row.issue_date),
            CustomerName = (string?)row.customer_name,
            TotalGross = (decimal)row.total_gross,
            State = (string)row.state,
            LastUpdateUtc = DocumentRowReader.ToDateTimeOffset((object)row.last_update_utc),
        };
    }

    private static DocumentEventDto MapEvent(dynamic row)
    {
        return new DocumentEventDto
        {
            Id = (Guid)row.id,
            DocumentId = (Guid)row.document_id,
            TimestampUtc = DocumentRowReader.ToDateTimeOffset((object)row.timestamp_utc),
            EventType = (string)row.event_type,
            Detail = (string?)row.detail,
            PayloadSnapshot = (string?)row.payload_snapshot,
            PaResponseSnapshot = (string?)row.pa_response_snapshot,
            MappingTrace = (string?)row.mapping_trace,
            OperatorIdentity = (string?)row.operator_identity,
            OperatorName = (string?)row.operator_name,
        };
    }

    private static PaTransmissionJournalDto MapPaTransmissionJournal(dynamic row)
    {
        return new PaTransmissionJournalDto
        {
            EventId = (Guid)row.id,
            DocumentId = (Guid)row.document_id,
            TimestampUtc = DocumentRowReader.ToDateTimeOffset((object)row.timestamp_utc),
            IdempotencyKey = (string)row.idempotency_key,
            PaAccount = (string)row.pa_account,
            PaPluginId = (string)row.pa_plugin_id,
            PaRequestUtc = DocumentRowReader.ToDateTimeOffset((object)row.pa_request_utc),
            PaResponseUtc = DocumentRowReader.ToDateTimeOffset((object)row.pa_response_utc),
            TransmittedArtifactHash = (string)row.transmitted_artifact_hash,
        };
    }
}
