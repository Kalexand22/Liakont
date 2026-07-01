namespace Liakont.Agent.Contracts.Ged;

using System;
using System.Collections.Generic;

/// <summary>
/// Corps de la requête d'ingestion d'un LOT de documents GÉRÉS (canal GED, F19 §2.4/§4.6), poussé par l'agent
/// sur <c>POST /api/agent/v1/managed-documents/batch</c> — DTO PUR, symétrique du <c>PushBatchRequestDto</c>
/// fiscal mais DISJOINT (aucun champ pivot). Porte les documents ingérés BRUTS et les capacités déclarées de
/// l'extracteur géré. La taille du lot est bornée par la plateforme (≤ 100, F12 §3.3), jamais par ce DTO.
/// </summary>
public sealed class ManagedDocumentBatchRequestDto
{
    /// <summary>Crée un corps de requête de lot GED.</summary>
    /// <param name="documents">Les documents gérés ingérés BRUTS (jamais nul ; coalescé en liste vide).</param>
    /// <param name="capabilities">Les capacités déclarées de l'extracteur géré, ou <c>null</c> si non fournies.</param>
    public ManagedDocumentBatchRequestDto(
        IReadOnlyList<IngestedDocumentDto>? documents,
        ManagedExtractorCapabilitiesDto? capabilities = null)
    {
        Documents = documents ?? Array.Empty<IngestedDocumentDto>();
        Capabilities = capabilities;
    }

    /// <summary>Les documents gérés ingérés BRUTS (chaque document produit un résultat individuel).</summary>
    public IReadOnlyList<IngestedDocumentDto> Documents { get; }

    /// <summary>Capacités déclarées de l'extracteur géré (add-only, ADR-0004 D2) ; <c>null</c> si non fournies.</summary>
    public ManagedExtractorCapabilitiesDto? Capabilities { get; }
}
