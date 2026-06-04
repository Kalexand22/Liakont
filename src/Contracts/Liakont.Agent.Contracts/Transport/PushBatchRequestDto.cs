namespace Liakont.Agent.Contracts.Transport;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Lot de documents poussé par l'agent vers la plateforme (POST /api/agent/v1/documents/batch —
/// F12 §3.4). Lot NON transactionnel ; la limite de taille (100 documents) est imposée par le
/// module Ingestion (PIV04), pas par ce DTO.
/// </summary>
public sealed class PushBatchRequestDto
{
    /// <summary>Crée une requête de lot.</summary>
    /// <param name="contractVersion">Version du contrat émise par l'agent (cf. <see cref="AgentContractVersion.ContractVersion"/>).</param>
    /// <param name="documents">Documents pivot du lot.</param>
    /// <param name="sourceTaxRegimes">
    /// Régimes de TVA observés dans la source (métadonnée de push). Champ AJOUTÉ en v1 (règle
    /// d'évolution add-only, contrat §4.1) : OPTIONNEL et en fin de DTO, donc compatible avec les
    /// agents N-1 (qui l'omettent) ; l'enveloppe du lot n'est pas hashée, l'empreinte des documents
    /// est inchangée. Persisté par tenant côté plateforme (PIV04) pour la détection de couverture
    /// TVA (TVA03). L'agent transmet le code BRUT, il n'interprète jamais le régime (CLAUDE.md n°2).
    /// </param>
    public PushBatchRequestDto(
        string contractVersion,
        IReadOnlyList<PivotDocumentDto>? documents = null,
        IReadOnlyList<SourceTaxRegimeDto>? sourceTaxRegimes = null)
    {
        ContractVersion = contractVersion;
        Documents = documents ?? Array.Empty<PivotDocumentDto>();
        SourceTaxRegimes = sourceTaxRegimes ?? Array.Empty<SourceTaxRegimeDto>();
    }

    /// <summary>Version du contrat émise par l'agent.</summary>
    public string ContractVersion { get; }

    /// <summary>Documents pivot du lot.</summary>
    public IReadOnlyList<PivotDocumentDto> Documents { get; }

    /// <summary>
    /// Régimes de TVA source observés (métadonnée de push, jamais hashée). Vide quand l'agent
    /// n'en transmet pas. Consommé par la détection de couverture TVA (TVA03).
    /// </summary>
    public IReadOnlyList<SourceTaxRegimeDto> SourceTaxRegimes { get; }
}
