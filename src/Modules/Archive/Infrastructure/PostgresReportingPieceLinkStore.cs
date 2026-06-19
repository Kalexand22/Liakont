namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Archive.Contracts;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Persistance de la traçabilité reporting↔pièces dans <c>documents.reporting_piece_links</c> (migration V011,
/// item B2C03). Tenant-scopée par la connexion (base par tenant, blueprint §7) ET, en défense en profondeur,
/// par un filtre explicite <c>company_id = @CompanyId</c> sur chaque requête (n°9 ; le <c>companyId</c> est
/// fourni par l'appelant). La table est append-only côté base (triggers anti UPDATE/DELETE/TRUNCATE de V011)
/// — ce store n'expose QUE des ajouts idempotents et des lectures dans les deux sens.
/// </summary>
internal sealed class PostgresReportingPieceLinkStore : IReportingPieceLinkStore
{
    // ON CONFLICT DO NOTHING : un lien déjà gelé (même tenant/transmission/pièce) est un no-op — jamais un
    // UPDATE (append-only préservé), ce qui rend un rejeu d'envoi sûr (idempotence du gel).
    private const string InsertSql = """
        INSERT INTO documents.reporting_piece_links
            (id, company_id, document_id, source_reference, linked_at_utc)
        VALUES
            (@Id, @CompanyId, @DocumentId, @SourceReference, @LinkedAtUtc)
        ON CONFLICT (company_id, document_id, source_reference) DO NOTHING
        """;

    private const string ByDocumentSql = """
        SELECT id, company_id, document_id, source_reference, linked_at_utc
        FROM documents.reporting_piece_links
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        ORDER BY linked_at_utc ASC, id ASC
        """;

    private const string BySourceSql = """
        SELECT id, company_id, document_id, source_reference, linked_at_utc
        FROM documents.reporting_piece_links
        WHERE company_id = @CompanyId AND source_reference = @SourceReference
        ORDER BY linked_at_utc ASC, id ASC
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresReportingPieceLinkStore(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ReportingPieceLink>> AppendAsync(
        Guid companyId,
        Guid documentId,
        IReadOnlyCollection<string> sourceReferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceReferences);

        DateTimeOffset linkedAtUtc = DateTimeOffset.UtcNow;

        await using var scope = await TransactionScope.BeginAsync(_connectionFactory, cancellationToken);

        foreach (string sourceReference in sourceReferences)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);
            await scope.Connection.ExecuteAsync(new CommandDefinition(
                InsertSql,
                new
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    DocumentId = documentId,
                    SourceReference = sourceReference,
                    LinkedAtUtc = linkedAtUtc.UtcDateTime,
                },
                scope.Transaction,
                cancellationToken: cancellationToken));
        }

        await scope.CommitAsync(cancellationToken);

        return await GetByDocumentAsync(companyId, documentId, cancellationToken);
    }

    public async Task<IReadOnlyList<ReportingPieceLink>> GetByDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await QueryAsync(connection, ByDocumentSql, new { CompanyId = companyId, DocumentId = documentId }, cancellationToken);
    }

    public async Task<IReadOnlyList<ReportingPieceLink>> GetBySourceReferenceAsync(Guid companyId, string sourceReference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);
        using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await QueryAsync(connection, BySourceSql, new { CompanyId = companyId, SourceReference = sourceReference }, cancellationToken);
    }

    private static async Task<IReadOnlyList<ReportingPieceLink>> QueryAsync(
        System.Data.IDbConnection connection,
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var links = new List<ReportingPieceLink>();
        foreach (var row in rows)
        {
            links.Add(new ReportingPieceLink(
                (Guid)row.id,
                (Guid)row.company_id,
                (Guid)row.document_id,
                (string)row.source_reference,
                ToUtcOffset((object)row.linked_at_utc)));
        }

        return links;
    }

    private static DateTimeOffset ToUtcOffset(object value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        _ => throw new InvalidCastException($"Type d'horodatage inattendu lu en base : {value.GetType().FullName}."),
    };
}
