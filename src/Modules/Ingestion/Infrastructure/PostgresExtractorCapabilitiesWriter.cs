namespace Liakont.Modules.Ingestion.Infrastructure;

using System;
using Dapper;
using Liakont.Modules.Ingestion.Application;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Upsert des capacités déclarées de la source d'un agent (métadonnée de push, ADR-0004 D2 / RD401),
/// base SYSTÈME (schéma <c>ingestion</c>), scopé <c>(tenant_id, agent_id)</c>. IDEMPOTENT : la DERNIÈRE
/// déclaration remplace la précédente (jamais cumulée) — un lot rejoué (retry réseau) ne corrompt rien ;
/// l'horodatage est rafraîchi. Les formes énumérées sont conservées BRUTES (jamais interprétées,
/// CLAUDE.md n°6).
/// </summary>
internal sealed class PostgresExtractorCapabilitiesWriter : IExtractorCapabilitiesWriter
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;

    public PostgresExtractorCapabilitiesWriter(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task UpsertAsync(
        string tenantId,
        Guid agentId,
        ExtractorCapabilitiesRecord capabilities,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO ingestion.extractor_capabilities (
                tenant_id, agent_id,
                provides_source_documents, provides_unlinked_document_pool, has_detailed_lines,
                has_credit_note_link, exposes_payments, regime_key_shape, emitter_identity_source,
                has_stored_header_total, is_mutable_after_issue, number_uniqueness_scope, last_seen_at)
            VALUES (
                @TenantId, @AgentId,
                @ProvidesSourceDocuments, @ProvidesUnlinkedDocumentPool, @HasDetailedLines,
                @HasCreditNoteLink, @ExposesPayments, @RegimeKeyShape, @EmitterIdentitySource,
                @HasStoredHeaderTotal, @IsMutableAfterIssue, @NumberUniquenessScope, @LastSeenAt)
            ON CONFLICT (tenant_id, agent_id) DO UPDATE
            SET provides_source_documents       = EXCLUDED.provides_source_documents,
                provides_unlinked_document_pool = EXCLUDED.provides_unlinked_document_pool,
                has_detailed_lines              = EXCLUDED.has_detailed_lines,
                has_credit_note_link            = EXCLUDED.has_credit_note_link,
                exposes_payments                = EXCLUDED.exposes_payments,
                regime_key_shape                = EXCLUDED.regime_key_shape,
                emitter_identity_source         = EXCLUDED.emitter_identity_source,
                has_stored_header_total         = EXCLUDED.has_stored_header_total,
                is_mutable_after_issue          = EXCLUDED.is_mutable_after_issue,
                number_uniqueness_scope         = EXCLUDED.number_uniqueness_scope,
                last_seen_at                    = EXCLUDED.last_seen_at
            """;

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                AgentId = agentId,
                capabilities.ProvidesSourceDocuments,
                capabilities.ProvidesUnlinkedDocumentPool,
                capabilities.HasDetailedLines,
                capabilities.HasCreditNoteLink,
                capabilities.ExposesPayments,
                capabilities.RegimeKeyShape,
                capabilities.EmitterIdentitySource,
                capabilities.HasStoredHeaderTotal,
                capabilities.IsMutableAfterIssue,
                capabilities.NumberUniquenessScope,
                LastSeenAt = DateTimeOffset.UtcNow,
            },
            cancellationToken: cancellationToken));
    }
}
