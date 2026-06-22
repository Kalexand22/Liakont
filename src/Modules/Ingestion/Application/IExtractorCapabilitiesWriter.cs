namespace Liakont.Modules.Ingestion.Application;

using System;

/// <summary>
/// Persiste (upsert) les capacités déclarées de la source d'un agent (ADR-0004 D2, RD401), par
/// agent/tenant, dans la base SYSTÈME (schéma <c>ingestion</c>). Métadonnée de push IDEMPOTENTE : une
/// déclaration ré-observée pour le même <c>(tenant, agent)</c> REMPLACE la précédente (jamais cumulée →
/// un retry ne corrompt rien) et rafraîchit son horodatage. Les formes énumérées sont conservées en
/// valeur BRUTE (nom de l'énumération source) : aucune interprétation ici (CLAUDE.md n°6). Consommé par
/// RD403 / RD409 via <c>IExtractorCapabilitiesQueries</c>.
/// </summary>
public interface IExtractorCapabilitiesWriter
{
    Task UpsertAsync(
        string tenantId,
        Guid agentId,
        ExtractorCapabilitiesRecord capabilities,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Capacités déclarées d'une source à persister (ADR-0004 D2). Les formes énumérées (régime, identité
/// émetteur, unicité du numéro) sont des chaînes BRUTES (nom de l'énumération source), jamais interprétées.
/// </summary>
public sealed record ExtractorCapabilitiesRecord
{
    public required bool ProvidesSourceDocuments { get; init; }

    public required bool ProvidesUnlinkedDocumentPool { get; init; }

    public required bool HasDetailedLines { get; init; }

    public required bool HasCreditNoteLink { get; init; }

    public required bool ExposesPayments { get; init; }

    public string? RegimeKeyShape { get; init; }

    public string? EmitterIdentitySource { get; init; }

    public required bool HasStoredHeaderTotal { get; init; }

    public required bool IsMutableAfterIssue { get; init; }

    public string? NumberUniquenessScope { get; init; }
}
