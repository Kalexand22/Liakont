namespace Liakont.Modules.Ingestion.Contracts.DTOs;

/// <summary>
/// Vue de lecture (plateforme) des capacités déclarées par la source d'un agent (ADR-0004 D2, RD401).
/// Persistée par agent/tenant à l'ingestion ; la plateforme s'y adapte — JAMAIS par <c>if (source is …)</c>.
/// Les formes énumérées (régime, identité émetteur, unicité du numéro) sont restituées en valeur BRUTE
/// (nom de l'énumération source) : leur interprétation appartient aux consommateurs métier (RD403, RD409).
/// </summary>
public sealed record ExtractorCapabilitiesSummaryDto
{
    /// <summary>La source fournit des PDF liés aux documents (pièces jointes).</summary>
    public required bool ProvidesSourceDocuments { get; init; }

    /// <summary>La source fournit un vrac de PDF non liés (pool, réconciliation).</summary>
    public required bool ProvidesUnlinkedDocumentPool { get; init; }

    /// <summary>La source porte des lignes détaillées (sinon : lignes synthétiques par taux).</summary>
    public required bool HasDetailedLines { get; init; }

    /// <summary>L'avoir référence sa facture d'origine de façon fiable.</summary>
    public required bool HasCreditNoteLink { get; init; }

    /// <summary>La source expose des encaissements datés (F09). Consommé par RD403.</summary>
    public required bool ExposesPayments { get; init; }

    /// <summary>Forme de la clé de régime TVA par ligne (nom brut de l'énumération source ; <c>null</c> si non déclaré).</summary>
    public string? RegimeKeyShape { get; init; }

    /// <summary>Origine de l'identité de l'émetteur (nom brut de l'énumération source ; <c>null</c> si non déclaré).</summary>
    public string? EmitterIdentitySource { get; init; }

    /// <summary>Un total d'entête stocké et réconciliable existe.</summary>
    public required bool HasStoredHeaderTotal { get; init; }

    /// <summary>La source autorise la modification d'un document émis (impacte l'idempotence R2). Consommé par RD403.</summary>
    public required bool IsMutableAfterIssue { get; init; }

    /// <summary>Granularité d'unicité du numéro de document (nom brut de l'énumération source ; <c>null</c> si non déclaré).</summary>
    public string? NumberUniquenessScope { get; init; }

    /// <summary>Horodatage de la dernière déclaration observée (UTC).</summary>
    public required DateTimeOffset LastSeenAtUtc { get; init; }
}
