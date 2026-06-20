namespace Liakont.Modules.Ingestion.Infrastructure.Queries;

using System;
using Dapper;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Lectures des capacités déclarées de la source d'un agent (base système, schéma <c>ingestion</c>,
/// ADR-0004 D2 / RD401), scopées par <c>tenant_id</c>. Jamais de lecture cross-tenant. Consommé par
/// RD403 (ExposesPayments → F09, IsMutableAfterIssue → alerte d'altération) et les différés RD409.
/// </summary>
internal sealed class PostgresExtractorCapabilitiesQueries : IExtractorCapabilitiesQueries
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public PostgresExtractorCapabilitiesQueries(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task<ExtractorCapabilitiesSummaryDto?> GetByAgentAsync(
        string tenantId,
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT provides_source_documents, provides_unlinked_document_pool, has_detailed_lines,
                   has_credit_note_link, exposes_payments, regime_key_shape, emitter_identity_source,
                   has_stored_header_total, is_mutable_after_issue, number_uniqueness_scope, last_seen_at
            FROM ingestion.extractor_capabilities
            WHERE tenant_id = @TenantId AND agent_id = @AgentId
            """;

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { TenantId = tenantId, AgentId = agentId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return new ExtractorCapabilitiesSummaryDto
        {
            ProvidesSourceDocuments = (bool)row.provides_source_documents,
            ProvidesUnlinkedDocumentPool = (bool)row.provides_unlinked_document_pool,
            HasDetailedLines = (bool)row.has_detailed_lines,
            HasCreditNoteLink = (bool)row.has_credit_note_link,
            ExposesPayments = (bool)row.exposes_payments,
            RegimeKeyShape = (string?)row.regime_key_shape,
            EmitterIdentitySource = (string?)row.emitter_identity_source,
            HasStoredHeaderTotal = (bool)row.has_stored_header_total,
            IsMutableAfterIssue = (bool)row.is_mutable_after_issue,
            NumberUniquenessScope = (string?)row.number_uniqueness_scope,
            LastSeenAtUtc = IngestionRowReader.ToDateTimeOffset((object)row.last_seen_at),
        };
    }
}
